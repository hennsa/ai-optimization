using System.Security.Cryptography;
using System.Text;
using AIReviewDesk.Core;

namespace AIReviewDesk.Infrastructure;

/// <summary>Builds a bounded, secret-screened review context from local Git state.</summary>
public sealed class GitReviewContext
{
    private readonly Func<string, CancellationToken, Task<string>> executionFingerprint;
    public GitReviewContext() => executionFingerprint = FingerprintAsync;
    internal GitReviewContext(Func<string, CancellationToken, Task<string>> fingerprint) => executionFingerprint = fingerprint;
    private const int MaxDiffCharacters = 1_500_000;
    internal const int MaxContextCharacters = 1_600_000;
    private const int MaxUntrackedBytes = 128 * 1024;

    public async Task<ReviewInput> PrepareAsync(
        ProjectRegistration project,
        ReviewScope scope,
        IReadOnlyList<string>? selectedPaths = null,
        CancellationToken ct = default,
        bool preview = false, IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ct.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(scope)) throw new InvalidOperationException("The selected review scope is not supported.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try { return await PrepareCoreAsync(project, scope, selectedPaths, linked.Token, preview, progress); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new PreparationException(PreparationFailure.Timeout); }
    }

    private async Task<ReviewInput> PrepareCoreAsync(ProjectRegistration project, ReviewScope scope, IReadOnlyList<string>? selectedPaths, CancellationToken ct, bool preview, IProgress<string>? progress)
    {
        progress?.Report("Preparing repository");
        var before = preview ? await PreviewStampAsync(project.RepositoryPath, ct) : await ExecutionFingerprintAsync(project.RepositoryPath, ct, progress);
        var snapshot = await new GitInspector().InspectAsync(project.RepositoryPath, project.DefaultBase, ct);
        var root = Path.GetFullPath(snapshot.RootPath);
        EnsureReviewableSnapshot(snapshot);

        var allChanged = scope == ReviewScope.BranchVsBase
            ? await GetBranchPathsAsync(root, snapshot, ct)
            : snapshot.ChangedPaths;
        var paths = scope == ReviewScope.SelectedPaths
            ? ValidateSelectedPaths(selectedPaths, allChanged)
            : Array.Empty<string>();
        var effectivePaths = paths.Count == 0 && scope != ReviewScope.SelectedPaths
            ? allChanged
            : paths;
        if (effectivePaths.Count == 0 && !preview)
            throw new ReviewValidationException(scope == ReviewScope.BranchVsBase
                ? "The current branch has no changes compared with its configured base."
                : "There are no working changes to review.");

        foreach (var path in effectivePaths)
            if (IsSecretPath(path))
                throw new PreparationException(PreparationFailure.SensitiveFile);

        progress?.Report("Building review context");
        var context = effectivePaths.Count == 0
            ? (scope == ReviewScope.BranchVsBase ? "No branch changes are currently present compared with the configured base." : "No working changes are currently present.")
            : scope == ReviewScope.BranchVsBase
            ? await BuildBranchContextAsync(root, snapshot, effectivePaths, ct)
            : await BuildWorkingContextAsync(root, effectivePaths, ct, scope == ReviewScope.SelectedPaths);

        var after = preview ? await PreviewStampAsync(root, ct) : await ExecutionFingerprintAsync(root, ct, progress);
        if (!string.Equals(before, after, StringComparison.Ordinal))
            throw new PreparationException(PreparationFailure.RepositoryChanged);

        return new ReviewInput
        {
            Project = project,
            Snapshot = snapshot,
            Scope = scope,
            SelectedPaths = paths,
            Context = context,
            HasReviewableChanges = effectivePaths.Count > 0,
            IsPreview = preview,
            Fingerprint = preview ? "" : after
        };
    }

    private async Task<string> ExecutionFingerprintAsync(string root, CancellationToken ct, IProgress<string>? progress)
    {
        progress?.Report("Fingerprinting repository");
        return await executionFingerprint(root, ct);
    }

    // Advisory consistency only: Git identity/index/status plus changed-file stat
    // metadata. No whole-repository file-content reads. Same-size/mtime edits may
    // escape this check; previews never authorize execution.
    private static async Task<string> PreviewStampAsync(string repository, CancellationToken ct)
    {
        var root = (await RunGitAsync(repository, ["rev-parse", "--show-toplevel"], ct)).Trim();
        var head = await RunGitResultAsync(root, ["rev-parse", "--verify", "HEAD^{commit}"], ct);
        var headRef = await RunGitResultAsync(root, ["symbolic-ref", "--quiet", "HEAD"], ct);
        var refs = await RunGitAsync(root, ["for-each-ref", "--format=%(refname)%00%(objectname)%00%(symref)", "refs"], ct);
        var index = await RunGitAsync(root, ["ls-files", "--stage", "-z"], ct);
        var status = await RunGitAsync(root, ["status", "--porcelain=v1", "-z", "--untracked-files=all"], ct);
        var evidence = new StringBuilder();
        AppendField(evidence, "head", head.StandardOutput);
        AppendField(evidence, "head-ref", headRef.StandardOutput);
        AppendField(evidence, "refs", refs);
        AppendField(evidence, "index", index);
        AppendField(evidence, "status", status);
        var entries = status.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry.Length < 4) throw new InvalidOperationException("Git returned invalid preview status.");
            var path = entry[3..];
            var file = new FileInfo(ResolveSafePath(root, path, allowMissing: true));
            AppendField(evidence, path, file.Exists ? $"{file.Length}:{file.LastWriteTimeUtc.Ticks}:{file.Attributes}" : "<missing>");
            if (entry[0] is 'R' or 'C' || entry[1] is 'R' or 'C') i++;
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence.ToString())));
    }

    /// <summary>Lists locally changed paths for the selected scope for use in a path picker.</summary>
    public async Task<IReadOnlyList<string>> GetChangedPathsAsync(
        ProjectRegistration project,
        ReviewScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!Enum.IsDefined(scope)) throw new InvalidOperationException("The selected review scope is not supported.");
        var snapshot = await new GitInspector().InspectAsync(project.RepositoryPath, project.DefaultBase, ct);
        EnsureReviewableSnapshot(snapshot);
        if (scope == ReviewScope.BranchVsBase)
            return await GetBranchPathsAsync(snapshot.RootPath, snapshot, ct);
        return snapshot.ChangedPaths;
    }

    /// <summary>
    /// Fingerprints HEAD, local refs, the index, and content for all tracked and
    /// non-ignored untracked paths. Symlinks are fingerprinted by link target only.
    /// </summary>
    public async Task<string> FingerprintAsync(string repositoryPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath)) throw new InvalidOperationException("Choose a repository folder to fingerprint.");
        var startingDirectory = Path.GetFullPath(repositoryPath);
        if (!Directory.Exists(startingDirectory)) throw new InvalidOperationException($"The repository folder does not exist: {startingDirectory}");
        var rootText = await RunGitAsync(startingDirectory, ["rev-parse", "--show-toplevel"], ct);
        var root = Path.GetFullPath(rootText.Trim());
        var head = await RunGitResultAsync(root, ["rev-parse", "--verify", "HEAD^{commit}"], ct);
        var output = new StringBuilder();
        AppendField(output, "head", head.ExitCode == 0 ? head.StandardOutput.Trim() : "<unborn>");
        if (head.ExitCode == 0) await EnsureNoSubmodulesInCommitAsync(root, head.StandardOutput.Trim(), ct);
        var headRef = await RunGitResultAsync(root, ["symbolic-ref", "--quiet", "HEAD"], ct);
        AppendField(output, "head-ref", headRef.ExitCode == 0 ? headRef.StandardOutput.Trim() : "<detached>");

        var refs = await RunGitAsync(root, ["for-each-ref", "--format=%(refname)%00%(objectname)%00%(symref)", "refs"], ct);
        AppendField(output, "refs", refs);
        var status = await RunGitAsync(root, ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=all"], ct);
        if (status.Split('\0', StringSplitOptions.RemoveEmptyEntries).Any(IsConflictEntry))
            throw new PreparationException(PreparationFailure.Conflicts);
        AppendField(output, "status", status);

        var indexPathText = (await RunGitAsync(root, ["rev-parse", "--git-path", "index"], ct)).Trim();
        var indexPath = Path.IsPathRooted(indexPathText) ? indexPathText : Path.GetFullPath(Path.Combine(root, indexPathText));
        if (File.Exists(indexPath))
        {
            await using var indexStream = new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            AppendField(output, "index", Convert.ToHexString(await SHA256.HashDataAsync(indexStream, ct)));
        }
        else
        {
            AppendField(output, "index", "<missing>");
        }

        var staged = await RunGitAsync(root, ["ls-files", "--stage", "-z", "--full-name"], ct);
        var tracked = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var record in staged.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = record.IndexOf('\t');
            if (separator < 0) throw new InvalidOperationException("Git returned an invalid index record; review fingerprinting was stopped.");
            var metadata = record[..separator];
            var path = record[(separator + 1)..];
            var fields = metadata.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 3 || fields[0] == "160000")
                throw new InvalidOperationException($"Submodule or invalid index entry '{path}' cannot be safely fingerprinted for review.");
            if (fields[2] != "0")
                throw new InvalidOperationException($"Unresolved index stages for '{path}' must be resolved before review.");
            tracked[path] = fields[0] == "120000";
        }
        var untrackedText = await RunGitAsync(root, ["ls-files", "--others", "--exclude-standard", "-z", "--"], ct);
        var paths = tracked.Keys.Concat(untrackedText.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();

        var fieldsByPath = new string[paths.Length];
        await Parallel.ForEachAsync(Enumerable.Range(0, paths.Length), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (i, token) =>
        {
            var path = paths[i];
            var fileEvidence = new StringBuilder();
            token.ThrowIfCancellationRequested();
            ValidateRepositoryPath(path);
            AppendField(fileEvidence, "path", path);
            var absolutePath = ResolveSafePath(root, path, allowMissing: true);
            if (!PathExistsIncludingLink(absolutePath))
            {
                AppendField(fileEvidence, "content", "<deleted>");
                fieldsByPath[i] = fileEvidence.ToString(); return;
            }

            var attributes = File.GetAttributes(absolutePath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                var target = new FileInfo(absolutePath).LinkTarget ?? new DirectoryInfo(absolutePath).LinkTarget;
                if (target is null)
                    throw new InvalidOperationException($"Unexpected reparse point at '{path}' cannot be safely fingerprinted.");
                AppendField(fileEvidence, "symlink", target ?? "<unreadable-target>");
                fieldsByPath[i] = fileEvidence.ToString(); return;
            }
            if ((attributes & FileAttributes.Directory) != 0)
                throw new InvalidOperationException($"Directory entry '{path}' cannot be safely fingerprinted as a Git file.");

            await using var stream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var digest = await SHA256.HashDataAsync(stream, token);
            AppendField(fileEvidence, "file-sha256", Convert.ToHexString(digest));
            fieldsByPath[i] = fileEvidence.ToString();
        });
        foreach (var evidence in fieldsByPath) output.Append(evidence);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(output.ToString())));
    }

    private static async Task<IReadOnlyList<string>> GetBranchPathsAsync(string root, RepositorySnapshot snapshot, CancellationToken ct)
    {
        if (snapshot.HeadSha is null)
            throw new ReviewValidationException("The current branch has no commit to compare with its configured base.");
        if (string.IsNullOrWhiteSpace(snapshot.BaseRef) || string.IsNullOrWhiteSpace(snapshot.MergeBaseSha))
            throw new ReviewValidationException(snapshot.BaseWarning ?? "A locally available configured base and merge-base are required for branch review.");
        await EnsureNoSubmodulesInCommitAsync(root, snapshot.MergeBaseSha, ct);
        await EnsureNoSubmodulesInCommitAsync(root, snapshot.HeadSha, ct);
        var output = await RunGitAsync(root,
            ["diff", "--name-only", "-z", "--no-ext-diff", "--no-textconv", "--no-color", snapshot.MergeBaseSha, snapshot.HeadSha, "--"], ct);
        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }

    private static async Task EnsureNoSubmodulesInCommitAsync(string root, string commit, CancellationToken ct)
    {
        var tree = await RunGitAsync(root, ["ls-tree", "-r", "-z", commit, "--"], ct);
        foreach (var record in tree.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = record.IndexOf('\t');
            if (separator < 0) throw new InvalidOperationException("Git returned an invalid tree record; review fingerprinting was stopped.");
            var metadata = record[..separator].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (metadata.Length >= 3 && metadata[0] == "160000")
                throw new InvalidOperationException($"Submodule path '{record[(separator + 1)..]}' cannot be safely fingerprinted for review.");
        }
    }

    private static IReadOnlyList<string> ValidateSelectedPaths(IReadOnlyList<string>? requested, IReadOnlyList<string> changed)
    {
        if (requested is null || requested.Count == 0)
            throw new ReviewValidationException("Select one or more changed paths to review.");
        var changedSet = changed.ToHashSet(StringComparer.Ordinal);
        var selected = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var raw in requested)
        {
            var path = (raw ?? string.Empty).Replace('\\', '/');
            ValidateRepositoryPath(path);
            if (!changedSet.Contains(path))
                throw new ReviewValidationException("A selected path is no longer changed. Select meaningful changed paths before starting.");
            selected.Add(path);
        }
        return selected.ToArray();
    }

    private static async Task<string> BuildBranchContextAsync(string root, RepositorySnapshot snapshot, IReadOnlyList<string> paths, CancellationToken ct)
    {
        var args = new List<string> { "diff", "--no-ext-diff", "--no-textconv", "--no-color", "--no-renames", "--binary", snapshot.MergeBaseSha!, snapshot.HeadSha!, "--" };
        // Entire branch scope needs no per-file arguments. Windows limits command-line size.
        var (diff, truncated) = await RunGitBoundedAsync(root, args, MaxDiffCharacters, ct);
        return FinishContext($"Scope: branch {snapshot.Branch} versus {snapshot.BaseRef} (merge-base {snapshot.MergeBaseSha}).", diff, truncated, []);
    }

    private static async Task<string> BuildWorkingContextAsync(string root, IReadOnlyList<string> paths, CancellationToken ct, bool selectedScope)
    {
        if (selectedScope && paths.Sum(p => p.Length + 16) > 24_000)
        {
            throw new PreparationException(PreparationFailure.ScopeTooLarge);
        }
        var literal = paths.Select(path => ":(literal)" + path).ToArray();
        var stagedArgs = new List<string> { "diff", "--cached", "--no-ext-diff", "--no-textconv", "--no-color", "--no-renames", "--binary", "--" };
        if (selectedScope) stagedArgs.AddRange(literal);
        var unstagedArgs = new List<string> { "diff", "--no-ext-diff", "--no-textconv", "--no-color", "--no-renames", "--binary", "--" };
        if (selectedScope) unstagedArgs.AddRange(literal);
        var staged = await RunGitBoundedAsync(root, stagedArgs, MaxDiffCharacters / 2, ct);
        var unstaged = await RunGitBoundedAsync(root, unstagedArgs, MaxDiffCharacters / 2, ct);
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(staged.Text)) sb.AppendLine("### Staged changes").AppendLine(staged.Text);
        if (!string.IsNullOrEmpty(unstaged.Text)) sb.AppendLine("### Unstaged changes").AppendLine(unstaged.Text);
        var truncated = staged.Truncated || unstaged.Truncated;
        var omitted = new List<string>();
        var untrackedText = await RunGitAsync(root, ["ls-files", "--others", "--exclude-standard", "-z", "--"], ct);
        var included = paths.ToHashSet(StringComparer.Ordinal);
        foreach (var path in untrackedText.Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(included.Contains))
        {
            ct.ThrowIfCancellationRequested();
            var file = ResolveSafePath(root, path, allowMissing: false);
            var info = new FileInfo(file);
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
            {
                var target = info.LinkTarget ?? new DirectoryInfo(file).LinkTarget;
                if (target is null) throw new InvalidOperationException($"Untracked reparse point '{path}' cannot be safely included in review context.");
                sb.AppendLine($"### Untracked symbolic link: {path}").AppendLine($"Target: {target}");
                continue;
            }
            if (info.Length > MaxUntrackedBytes)
            {
                omitted.Add($"{path} (larger than {MaxUntrackedBytes / 1024} KiB)");
                continue;
            }
            var bytes = await File.ReadAllBytesAsync(file, ct);
            if (bytes.Contains((byte)0))
            {
                omitted.Add($"{path} (binary content)");
                continue;
            }
            var content = Encoding.UTF8.GetString(bytes);
            if (sb.Length + content.Length > MaxDiffCharacters)
            {
                omitted.Add($"{path} (review context size limit)");
                continue;
            }
            sb.AppendLine($"### Untracked file: {path}").AppendLine(content);
        }
        return FinishContext("Scope: current working changes (staged, unstaged, and selected untracked file content).", sb.ToString(), truncated, omitted);
    }

    private static string FinishContext(string header, string diff, bool truncated, IReadOnlyList<string> omitted)
    {
        var builder = new StringBuilder(header).AppendLine();
        if (!string.IsNullOrWhiteSpace(diff)) builder.AppendLine(diff);
        if (truncated) builder.AppendLine("[Diff context truncated at the application size limit.]");
        foreach (var item in omitted.Take(100)) builder.AppendLine($"[Untracked content omitted: {item}]");
        if (omitted.Count > 100) builder.AppendLine($"[{omitted.Count - 100} additional untracked files omitted at the context size limit.]");
        if (builder.Length > MaxContextCharacters)
        {
            builder.Length = MaxContextCharacters - 100;
            if (char.IsHighSurrogate(builder[^1])) builder.Length--;
            builder.AppendLine().AppendLine("[Review context truncated at the application size limit.]");
        }
        return builder.ToString();
    }

    private static void EnsureReviewableSnapshot(RepositorySnapshot snapshot)
    {
        if (snapshot.ConflictCount > 0)
            throw new PreparationException(PreparationFailure.Conflicts);
    }

    private static bool IsConflictEntry(string entry) => entry.Length >= 2 &&
        (entry[0] == 'U' || entry[1] == 'U' || (entry[0] == 'A' && entry[1] == 'A') || (entry[0] == 'D' && entry[1] == 'D'));

    private static bool IsSecretPath(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        return name == ".env" || name.StartsWith(".env.", StringComparison.Ordinal) ||
            name.Contains("credential", StringComparison.Ordinal) || name.Contains("secret", StringComparison.Ordinal) ||
            name is "id_rsa" or "id_ed25519" or "id_ecdsa" ||
            name.EndsWith(".pem", StringComparison.Ordinal) || name.EndsWith(".key", StringComparison.Ordinal) ||
            name.EndsWith(".pfx", StringComparison.Ordinal) || name.EndsWith(".p12", StringComparison.Ordinal);
    }

    private static void ValidateRepositoryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') ||
            Path.IsPathRooted(path) || path.Contains(':') || path.Contains('\0'))
            throw new InvalidOperationException("A selected path is not a safe repository-relative path.");
        var segments = path.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
            throw new InvalidOperationException($"'{path}' is not a normalized repository-relative path.");
    }

    private static string ResolveSafePath(string root, string path, bool allowMissing)
    {
        ValidateRepositoryPath(path);
        var current = root;
        var segments = path.Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            current = Path.Combine(current, segments[i]);
            if (!PathExistsIncludingLink(current))
            {
                if (allowMissing) return current;
                throw new InvalidOperationException($"Changed path '{path}' disappeared while review context was being prepared.");
            }
            var attrs = File.GetAttributes(current);
            if ((attrs & FileAttributes.ReparsePoint) != 0 && i < segments.Length - 1)
                throw new InvalidOperationException($"Path '{path}' traverses a reparse point and cannot be safely included.");
        }
        var full = Path.GetFullPath(current);
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Path '{path}' resolves outside the repository.");
        return full;
    }

    private static void AppendField(StringBuilder builder, string name, string value) =>
        builder.Append(name.Length).Append(':').Append(name).Append(value.Length).Append(':').Append(value).Append('\n');

    private static bool PathExistsIncludingLink(string path) =>
        File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget is not null || new DirectoryInfo(path).LinkTarget is not null;

    private static async Task<string> RunGitAsync(string root, IReadOnlyList<string> args, CancellationToken ct)
    {
        var (text, exitCode, error, _) = await RunGitCoreAsync(root, args, int.MaxValue, ct);
        if (exitCode != 0) throw new PreparationException(PreparationFailure.GitCommand);
        return text;
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunGitResultAsync(string root, IReadOnlyList<string> args, CancellationToken ct)
    {
        var (stdout, exitCode, error, _) = await RunGitCoreAsync(root, args, 1024 * 1024, ct);
        return (exitCode, stdout, error);
    }

    private static async Task<(string Text, bool Truncated)> RunGitBoundedAsync(string root, IReadOnlyList<string> args, int limit, CancellationToken ct)
    {
        var (text, exitCode, error, truncated) = await RunGitCoreAsync(root, args, limit, ct);
        if (exitCode != 0) throw new PreparationException(PreparationFailure.GitCommand);
        return (text, truncated);
    }

    private static Task<(string Text, int ExitCode, string Error, bool Truncated)> RunGitCoreAsync(string root, IReadOnlyList<string> args, int charLimit, CancellationToken ct) =>
        GitRunner.RunAsync(root, args, charLimit, ct);
}
