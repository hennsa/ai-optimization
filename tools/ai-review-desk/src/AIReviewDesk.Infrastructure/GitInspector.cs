using System.Diagnostics;
using AIReviewDesk.Core;

namespace AIReviewDesk.Infrastructure;

public sealed class GitInspector
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);

    public async Task<RepositorySnapshot> InspectAsync(
        string path,
        string? configuredBase = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Choose a repository folder to inspect.");

        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException("The repository folder path is not valid.", ex);
        }
        if (!Directory.Exists(fullPath))
            throw new InvalidOperationException($"The repository folder does not exist: {fullPath}");

        var root = (await RunGitAsync(fullPath, ["rev-parse", "--show-toplevel"], cancellationToken)).Trim();
        if (root.Length == 0)
            throw new InvalidOperationException("Git did not return a repository folder.");
        root = Path.GetFullPath(root);

        var branchResult = await RunGitResultAsync(root, ["branch", "--show-current"], cancellationToken);
        var branchName = branchResult.ExitCode == 0 ? branchResult.StandardOutput.Trim() : string.Empty;
        var headResult = await RunGitResultAsync(root, ["rev-parse", "--verify", "HEAD^{commit}"], cancellationToken);
        var headSha = headResult.ExitCode == 0 ? headResult.StandardOutput.Trim() : null;
        var branch = headSha is null
            ? $"Unborn branch ({(string.IsNullOrEmpty(branchName) ? "unknown" : branchName)})"
            : string.IsNullOrEmpty(branchName) ? $"Detached at {headSha[..Math.Min(7, headSha.Length)]}" : branchName;

        var status = await RunGitAsync(root, ["status", "--porcelain=v1", "-z", "--untracked-files=all"], cancellationToken);
        var counts = ParsePorcelain(status);
        var candidateText = await RunGitAsync(root,
            ["for-each-ref", "--format=%(refname:short)", "refs/heads", "refs/remotes"], cancellationToken);
        var candidates = candidateText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(candidate => !candidate.EndsWith("/HEAD", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(candidate => candidate, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string? baseRef = null;
        string? baseCommitSha = null;
        string? baseWarning = null;
        if (!string.IsNullOrWhiteSpace(configuredBase))
        {
            var configuredResult = await RunGitResultAsync(root,
                ["rev-parse", "--verify", "--end-of-options", $"{configuredBase}^{{commit}}"], cancellationToken);
            if (configuredResult.ExitCode == 0)
            {
                baseRef = configuredBase;
                baseCommitSha = configuredResult.StandardOutput.Trim();
            }
            else
                baseWarning = $"Configured base '{configuredBase}' is not available locally. No base was selected.";
        }
        else
        {
            baseRef = await ChooseLikelyBaseAsync(root, branchName, candidates, cancellationToken);
            if (baseRef is not null)
            {
                var resolved = await RunGitResultAsync(root,
                    ["rev-parse", "--verify", "--end-of-options", $"{baseRef}^{{commit}}"], cancellationToken);
                if (resolved.ExitCode == 0)
                    baseCommitSha = resolved.StandardOutput.Trim();
                else
                    baseRef = null;
            }
        }

        string? mergeBase = null;
        if (baseRef is not null && headSha is not null)
        {
            var mergeResult = await RunGitResultAsync(root, ["merge-base", "HEAD", baseCommitSha!], cancellationToken);
            if (mergeResult.ExitCode == 0)
                mergeBase = mergeResult.StandardOutput.Trim();
            else
                baseWarning = $"Base '{baseRef}' has no merge base with the current HEAD.";
        }
        else if (baseRef is not null && headSha is null)
        {
            baseWarning = $"Base '{baseRef}' is available, but this repository has no commit yet.";
        }

        var diffSummary = await GetDiffSummaryAsync(root, headSha is not null, counts.Untracked, cancellationToken);
        return new RepositorySnapshot
        {
            RootPath = root,
            Branch = branch,
            HeadSha = headSha,
            BaseRef = baseRef,
            MergeBaseSha = mergeBase,
            BaseWarning = baseWarning,
            BaseCandidates = candidates,
            StagedCount = counts.Staged,
            UnstagedCount = counts.Unstaged,
            UntrackedCount = counts.Untracked,
            ConflictCount = counts.Conflicts,
            ChangedFileCount = counts.Files,
            DiffSummary = diffSummary
        };
    }

    private static async Task<string?> ChooseLikelyBaseAsync(
        string root, string currentBranch, IReadOnlyList<string> candidates, CancellationToken cancellationToken)
    {
        var symbolic = await RunGitResultAsync(root,
            ["symbolic-ref", "--quiet", "--short", "refs/remotes/origin/HEAD"], cancellationToken);
        var preferred = new List<string>();
        if (symbolic.ExitCode == 0 && !string.IsNullOrWhiteSpace(symbolic.StandardOutput))
            preferred.Add(symbolic.StandardOutput.Trim());

        preferred.AddRange(["main", "master", "origin/main", "origin/master"]);
        foreach (var name in preferred.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(name, currentBranch, StringComparison.OrdinalIgnoreCase))
                continue;
            if (candidates.Contains(name, StringComparer.OrdinalIgnoreCase) &&
                (await RunGitResultAsync(root, ["rev-parse", "--verify", "--end-of-options", $"{name}^{{commit}}"], cancellationToken)).ExitCode == 0)
                return name;
        }

        return null;
    }

    private static (int Staged, int Unstaged, int Untracked, int Conflicts, int Files) ParsePorcelain(string output)
    {
        var staged = 0;
        var unstaged = 0;
        var untracked = 0;
        var conflicts = 0;
        var files = 0;
        var entries = output.Split('\0');
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry.Length < 3)
                continue;
            var x = entry[0];
            var y = entry[1];
            if (x == '?' && y == '?')
            {
                untracked++;
                files++;
                continue;
            }

            files++;
            if (x != ' ')
                staged++;
            if (y != ' ')
                unstaged++;
            if (x == 'U' || y == 'U' || (x == 'A' && y == 'A') || (x == 'D' && y == 'D'))
                conflicts++;
            if (x is 'R' or 'C' || y is 'R' or 'C')
                i++; // -z emits the original pathname as a second NUL-delimited field.
        }
        return (staged, unstaged, untracked, conflicts, files);
    }

    private static async Task<string> GetDiffSummaryAsync(string root, bool hasHead, int untracked, CancellationToken cancellationToken)
    {
        var stagedSummary = await RunGitAsync(root,
            ["diff", "--shortstat", "--no-ext-diff", "--no-textconv", "--cached"], cancellationToken);
        var unstagedSummary = await RunGitAsync(root,
            ["diff", "--shortstat", "--no-ext-diff", "--no-textconv"], cancellationToken);
        var stagedStats = ParseShortStat(stagedSummary);
        var unstagedStats = ParseShortStat(unstagedSummary);
        var stagedPaths = await RunGitAsync(root,
            ["diff", "--name-only", "-z", "--no-ext-diff", "--no-textconv", "--cached"], cancellationToken);
        var unstagedPaths = await RunGitAsync(root,
            ["diff", "--name-only", "-z", "--no-ext-diff", "--no-textconv"], cancellationToken);
        var changedPaths = stagedPaths.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Concat(unstagedPaths.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.Ordinal);
        var changed = changedPaths.Count;
        var inserted = stagedStats.Insertions + unstagedStats.Insertions;
        var deleted = stagedStats.Deletions + unstagedStats.Deletions;

        if (changed == 0 && untracked == 0)
            return "No changes";
        var parts = new List<string>();
        if (changed > 0)
            parts.Add($"{changed} tracked file{(changed == 1 ? "" : "s")} changed, {inserted} insertion{(inserted == 1 ? "" : "s")}(+), {deleted} deletion{(deleted == 1 ? "" : "s")}(-)");
        if (untracked > 0)
            parts.Add($"{untracked} untracked file{(untracked == 1 ? "" : "s")}");
        return string.Join("; ", parts);
    }

    private static (int Files, int Insertions, int Deletions) ParseShortStat(string summary)
    {
        var files = ParseCountBefore(summary, " file");
        var insertions = ParseCountBefore(summary, " insertion");
        var deletions = ParseCountBefore(summary, " deletion");
        return (files, insertions, deletions);
    }

    private static int ParseCountBefore(string summary, string marker)
    {
        var index = summary.IndexOf(marker, StringComparison.Ordinal);
        if (index <= 0)
            return 0;
        var start = index - 1;
        while (start >= 0 && char.IsDigit(summary[start])) start--;
        return int.TryParse(summary[(start + 1)..index], out var value) ? value : 0;
    }

    private static async Task<string> RunGitAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await RunGitResultAsync(workingDirectory, arguments, cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(FormatGitError(result.StandardError));
        return result.StandardOutput;
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunGitResultAsync(
        string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git.exe",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("--no-optional-locks");
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add("core.fsmonitor=false");
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Git could not be started.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException("Git was not found. Install Git for Windows and try again.", ex);
        }

        using var timeout = new CancellationTokenSource(CommandTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(linked.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(linked.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
            return (process.ExitCode, await stdoutTask, await stderrTask);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            if (cancellationToken.IsCancellationRequested)
                throw;
            throw new InvalidOperationException("Git took too long to respond. Try again when the repository is available.");
        }
    }

    private static string FormatGitError(string error)
    {
        var message = error.Trim();
        if (message.Contains("not a git repository", StringComparison.OrdinalIgnoreCase))
            return "This folder is not inside a Git repository.";
        return string.IsNullOrEmpty(message) ? "Git could not inspect this repository." : $"Git could not inspect this repository: {message}";
    }
}
