using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using System.Text.Json;
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
            bool savedAccount;
            try { savedAccount = HasSavedAccountMetadata(Profile); }
            catch (InvalidOperationException ex) { return new(true, true, false, null, version, ex.Message, ConfigurationBlocked: true); }
            return new(true, true, false, null, version, "Live account status unavailable: CLI 1.0.91 provides no supported noninteractive account-status command. Configuration passed structural preflight; the official CLI owns authentication. Credential values are never decoded or retained.", ConfigurationBlocked: false, SavedAccountConfigured: savedAccount);
        }
        finally { DeleteRunDirectory(run); }
    }

    public static bool HasSavedAccountMetadata(string profile)
    {
        var config = Path.Combine(profile, "config.json");
        return File.Exists(config) && CopilotConfigScanner.InspectFile(config).SavedAccountPresent;
    }

    public async Task<CopilotMetadata> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        var account = await GetAccountAsync(cancellationToken);
        if (!account.Supported || account.ConfigurationBlocked) return CopilotMetadata.Unavailable;
        var run = CreateRunDirectory();
        Process? process = null; Task? stderr = null;
        try
        {
            var installation = CopilotContract.Detect()!;
            var configuration = CopilotPreflight.Inspect(Profile, run);
            CopilotGitHubCliIsolation.Inspect(installation, run);
            var info = CopilotContract.StartInfo(installation, run, Profile, Path.Combine(run, "cache"), CopilotContract.MetadataArguments(Path.Combine(run, "logs")));
            if (CopilotPreflight.Inspect(Profile, run) != configuration) throw new InvalidOperationException("Configuration changed.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            process = Process.Start(info) ?? throw new InvalidOperationException("Metadata runtime unavailable.");
            // Never retain, log or display runtime diagnostics (including authentication failures).
            stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
            var rpc = new CopilotMetadataRpc(process);
            using var connected = await rpc.RequestAsync("connect", timeout.Token);
            if (connected.RootElement.GetProperty("result").GetProperty("protocolVersion").GetInt32() != 3)
                throw new InvalidOperationException("Unsupported SDK protocol.");
            using var status = await rpc.RequestAsync("status.get", timeout.Token);
            CopilotMetadataParser.ValidateRuntime(status.RootElement.GetProperty("result"));
            using var models = await rpc.RequestAsync("models.list", timeout.Token);
            var choices = CopilotMetadataParser.Models(models.RootElement.GetProperty("result"));
            CopilotQuota? quota = null;
            try
            {
                using var response = await rpc.RequestAsync("account.getQuota", timeout.Token);
                quota = CopilotMetadataParser.Quota(response.RootElement.GetProperty("result"), DateTimeOffset.UtcNow);
            }
            catch (Exception ex) when (ex is InvalidOperationException or JsonException or KeyNotFoundException or IOException or OperationCanceledException) { }
            return new(choices, quota, CopilotMetadata.Unavailable.Message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or KeyNotFoundException or IOException or OperationCanceledException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        { return CopilotMetadata.Unavailable; }
        finally
        {
            if (process != null)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                await process.WaitForExitAsync();
                if (stderr != null) await stderr;
                process.Dispose();
            }
            DeleteRunDirectory(run);
        }
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
            var validationRun = CreateRunDirectory();
            try { CopilotPreflight.Inspect(Profile, validationRun); }
            finally { DeleteRunDirectory(validationRun); }
        }
        finally { DeleteRunDirectory(run); }
    }

    public Task SignOutAsync(CancellationToken cancellationToken = default) =>
        Task.FromException(new InvalidOperationException("CLI 1.0.91 has no supported noninteractive sign-out command. AI Review Desk does not automate interactive account commands. No credentials were changed."));

    public async Task<ReviewRecord> RunAsync(ReviewInput input, IEnumerable<string> profileIds, IProgress<string>? progress = null, CancellationToken cancellationToken = default, ReviewExecutionSettings? execution = null)
    {
        if (!input.HasReviewableChanges) throw new ReviewValidationException("The selected scope has no changes to review. Preview remains available.");
        var ids = profileIds.ToArray();
        var prompt = PromptComposer.Compose(input, ids);
        var profiles = BuiltInProfiles.All.Where(p => ids.Contains(p.Id, StringComparer.OrdinalIgnoreCase)).ToArray();
        var record = new ReviewRecord
        {
            SchemaVersion = 4, RequestedExecution = execution ?? new(),
            ProjectId = input.Project.Id, ProjectName = input.Project.DisplayName, RepositoryPath = input.Project.RepositoryPath,
            Branch = input.Snapshot.Branch, HeadSha = input.Snapshot.HeadSha, BaseRef = input.Snapshot.BaseRef, MergeBaseSha = input.Snapshot.MergeBaseSha,
            Scope = input.Scope, SelectedPaths = input.SelectedPaths, ProfileIds = profiles.Select(p => p.Id).ToArray(),
            ProfileVersions = profiles.ToDictionary(p => p.Id, p => p.Version), SharedPolicyVersion = SharedReviewerPolicy.Version, AppVersion = "0.1.0",
            ProfileNames = profiles.ToDictionary(p => p.Id, p => p.Name),
            ChangedFileCount = input.Snapshot.ChangedFileCount, TrackedChangedCount = input.Snapshot.TrackedChangedCount, UntrackedCount = input.Snapshot.UntrackedCount, FingerprintBefore = input.Fingerprint,
            DiffHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.Context))), Status = ReviewStatus.Failed
        };
        string? run = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Fingerprinting repository");
            var integrity = new GitReviewContext();
            if (await integrity.FingerprintAsync(input.Project.RepositoryPath, cancellationToken) != input.Fingerprint)
                return record with { Status = ReviewStatus.Stale, Diagnostic = "The repository changed after prompt preparation. Preview a fresh snapshot before reviewing." };
            progress?.Report("Validating Copilot environment");
            var account = await GetAccountAsync(cancellationToken);
            record = record with { CopilotCliVersion = account.Version ?? "" };
            if (!account.Available || !account.Supported) return record with { Status = ReviewStatus.Unsupported, Diagnostic = account.Message };
            CopilotModelPolicy.Validate(record.RequestedExecution!, account.Version);
            if (!record.RequestedExecution!.IsAutoModel)
            {
                var metadata = await GetMetadataAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                CopilotModelPolicy.Validate(record.RequestedExecution, account.Version, metadata.Models);
            }
            run = CreateRunDirectory();
            var configuration = CopilotPreflight.Inspect(Profile, run, input.Project.RepositoryPath);
            var usageFile = Path.Combine(run, "usage.json");
            var info = CopilotContract.StartInfo(CopilotContract.Detect()!, run, Profile, Path.Combine(run, "cache"), CopilotContract.ReviewArguments(input.Project.RepositoryPath, Path.Combine(run, "logs"), record.RequestedExecution, usageFile));
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
            ReviewUsage? usage = null;
            try
            {
                if (File.Exists(usageFile) && new FileInfo(usageFile).Length <= 128 * 1024 && (File.GetAttributes(usageFile) & FileAttributes.ReparsePoint) == 0)
                    usage = CopilotUsageParser.Parse(await File.ReadAllTextAsync(usageFile, cancellationToken));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or IOException or UnauthorizedAccessException) { }
            progress?.Report("Completed");
            return record with { Status = ReviewStatus.Completed, Result = result, Usage = usage };
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
