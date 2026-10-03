using System.Security.Cryptography;
using System.Text;
using AIReviewDesk.Core;

namespace AIReviewDesk.Infrastructure;

public sealed class CopilotService
{
    private readonly string dataDirectory;
    private string Profile => Path.Combine(dataDirectory, "Copilot");
    public CopilotService(string? dataDirectory = null) => this.dataDirectory = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIReviewDesk");

    public async Task<CopilotAccountState> GetAccountAsync(CancellationToken cancellationToken = default)
    {
        var installation = CopilotContract.Detect();
        if (installation == null) return new(false, false, false, null, null, "Copilot CLI was not found. Install GitHub Copilot CLI to continue.");
        try { CopilotMachinePolicy.Inspect(); }
        catch (InvalidOperationException ex) { return new(true, false, false, null, null, ex.Message, ConfigurationBlocked: true); }
        var run = CreateRunDirectory();
        try
        {
            try { CopilotGitHubCliIsolation.Inspect(installation, run); }
            catch (InvalidOperationException ex) { return new(true, false, false, null, null, ex.Message, ConfigurationBlocked: true); }
            var text = new StringBuilder();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var result = await CopilotProcess.RunAsync(CopilotContract.StartInfo(installation, run, Profile, Path.Combine(run, "cache"), ["--no-auto-update", "--version"]), null, line => text.AppendLine(line), timeout.Token);
            var version = result.ExitCode == 0 ? CopilotContract.ParseVersion(text.ToString()) : null;
            if (!CopilotContract.IsSupported(version)) return new(true, false, false, null, version, $"Unsupported Copilot CLI. The verified Windows contract requires {CopilotContract.SupportedVersion}.");
            var preflightRun = CreateRunDirectory();
            try { CopilotPreflight.Inspect(Profile, preflightRun); }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Text.Json.JsonException)
            { return new(true, true, false, null, version, ex.Message, ConfigurationBlocked: true); }
            finally { DeleteRunDirectory(preflightRun); }
            return new(true, true, false, null, version, "Account status is unknown: CLI 1.0.91 provides no supported noninteractive account-status command. Configuration passed structural preflight; the official CLI owns authentication. Credential values are never decoded or retained.", ConfigurationBlocked: false);
        }
        finally { DeleteRunDirectory(run); }
    }

    public async Task SignInAsync(CancellationToken cancellationToken = default)
    {
        var installation = CopilotContract.Detect() ?? throw new InvalidOperationException("Copilot CLI is missing.");
        var account = await GetAccountAsync(cancellationToken);
        if (!account.Supported || account.ConfigurationBlocked) throw new InvalidOperationException(account.Message);
        var run = CreateRunDirectory();
        try
        {
            CopilotPreflight.Inspect(Profile, run);
            CopilotGitHubCliIsolation.Inspect(installation, run);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            var result = await CopilotProcess.RunAsync(CopilotContract.StartInfo(installation, run, Profile, Path.Combine(run, "cache"), ["login", "--web-flow"]), null, _ => { }, timeout.Token);
            if (result.ExitCode != 0) throw new InvalidOperationException("GitHub sign-in did not complete. Try the official Copilot browser flow again.");
        }
        finally { DeleteRunDirectory(run); }
    }

    public Task SignOutAsync(CancellationToken cancellationToken = default) =>
        Task.FromException(new InvalidOperationException("CLI 1.0.91 has no supported noninteractive sign-out command. AI Review Desk does not automate interactive account commands. No credentials were changed."));

    public async Task<ReviewRecord> RunAsync(ReviewInput input, IEnumerable<string> profileIds, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var ids = profileIds.ToArray();
        var prompt = PromptComposer.Compose(input, ids);
        var profiles = BuiltInProfiles.All.Where(p => ids.Contains(p.Id, StringComparer.OrdinalIgnoreCase)).ToArray();
        var record = new ReviewRecord
        {
            SchemaVersion = 2,
            ProjectId = input.Project.Id, ProjectName = input.Project.DisplayName, RepositoryPath = input.Project.RepositoryPath,
            Branch = input.Snapshot.Branch, HeadSha = input.Snapshot.HeadSha, BaseRef = input.Snapshot.BaseRef, MergeBaseSha = input.Snapshot.MergeBaseSha,
            Scope = input.Scope, SelectedPaths = input.SelectedPaths, ProfileIds = profiles.Select(p => p.Id).ToArray(),
            ProfileVersions = profiles.ToDictionary(p => p.Id, p => p.Version), SharedPolicyVersion = SharedReviewerPolicy.Version, AppVersion = "0.1.0",
            ProfileNames = profiles.ToDictionary(p => p.Id, p => p.Name),
            ChangedFileCount = input.Snapshot.ChangedFileCount, FingerprintBefore = input.Fingerprint,
            DiffHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.Context))), Status = ReviewStatus.Failed
        };
        string? run = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Preparing review");
            var integrity = new GitReviewContext();
            if (await integrity.FingerprintAsync(input.Project.RepositoryPath, cancellationToken) != input.Fingerprint)
                return record with { Status = ReviewStatus.Stale, Diagnostic = "The repository changed after prompt preparation. Preview a fresh snapshot before reviewing." };
            progress?.Report("Validating Copilot environment");
            var account = await GetAccountAsync(cancellationToken);
            record = record with { CopilotCliVersion = account.Version ?? "" };
            if (!account.Available || !account.Supported) return record with { Status = ReviewStatus.Unsupported, Diagnostic = account.Message };
            run = CreateRunDirectory();
            var configuration = CopilotPreflight.Inspect(Profile, run, input.Project.RepositoryPath);
            var info = CopilotContract.StartInfo(CopilotContract.Detect()!, run, Profile, Path.Combine(run, "cache"), CopilotContract.ReviewArguments(input.Project.RepositoryPath, Path.Combine(run, "logs")));
            progress?.Report("Starting Copilot");
            // Recheck immediately before Process.Start. Same-user external writes remain a documented desktop trust assumption.
            if (CopilotPreflight.Inspect(Profile, run, input.Project.RepositoryPath) != configuration) throw new InvalidOperationException("Copilot configuration changed during launch preparation.");
            CopilotGitHubCliIsolation.Inspect(CopilotContract.Detect()!, run);
            var validator = new CopilotStreamValidator();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            progress?.Report("Reviewing");
            var process = await CopilotProcess.RunAsync(info, prompt, validator.Accept, timeout.Token);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Validating result");
            var after = await integrity.FingerprintAsync(input.Project.RepositoryPath, cancellationToken);
            record = record with { FingerprintAfter = after, CopilotModel = validator.Model };
            if (after != input.Fingerprint) return record with { Status = ReviewStatus.Stale, Diagnostic = "Repository contents or Git state changed during review. Findings were discarded." };
            var result = validator.Complete(process.ExitCode, process.Cancelled);
            progress?.Report("Completed");
            return record with { Status = ReviewStatus.Completed, Result = result };
        }
        catch (OperationCanceledException)
        {
            return record with { Status = cancellationToken.IsCancellationRequested ? ReviewStatus.Cancelled : ReviewStatus.Failed, Diagnostic = cancellationToken.IsCancellationRequested ? "Review cancelled. Partial findings were discarded." : "Copilot exceeded the review time limit. Partial findings were discarded." };
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            return record with { Status = ReviewStatus.Failed, Diagnostic = ex is System.ComponentModel.Win32Exception ? "Copilot could not be started." : ex.Message };
        }
        finally { if (run != null) DeleteRunDirectory(run); }
    }

    private string CreateRunDirectory()
    {
        var path = Path.Combine(dataDirectory, "Runs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        CopilotPreflight.AssertNoReparseAncestors(path);
        return path;
    }
    private void DeleteRunDirectory(string path)
    {
        var root = Path.GetFullPath(Path.Combine(dataDirectory, "Runs")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Run cleanup target is outside application storage.");
        if (!Directory.Exists(path)) return;
        DeleteTreeWithoutFollowingLinks(path);
    }
    private static void DeleteTreeWithoutFollowingLinks(string path)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            if (Directory.Exists(entry))
            {
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) Directory.Delete(entry);
                else DeleteTreeWithoutFollowingLinks(entry);
            }
            else File.Delete(entry);
        }
        Directory.Delete(path);
    }
}
