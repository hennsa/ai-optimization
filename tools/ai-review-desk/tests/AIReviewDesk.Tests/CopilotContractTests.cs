using System.Diagnostics;
using System.Text.Json;
using AIReviewDesk.Core;
using AIReviewDesk.Infrastructure;

namespace AIReviewDesk.Tests;

public sealed class CopilotContractTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ard-contract-" + Guid.NewGuid().ToString("N"));
    private string Profile => Path.Combine(root, "profile");
    private string Run => Path.Combine(root, "run");
    public CopilotContractTests() { Directory.CreateDirectory(Profile); Directory.CreateDirectory(Run); }
    public void Dispose() { Directory.Delete(root, true); }

    [Theory]
    [InlineData("GitHub Copilot CLI 1.0.91.\nRun 'copilot update' to check for updates.", "1.0.91")]
    [InlineData("GitHub Copilot CLI 1.0.92.", "1.0.92")]
    [InlineData("unrecognized 1.0.91", null)]
    public void VersionDetectionIsExplicit(string output, string? expected) => Assert.Equal(expected, CopilotContract.ParseVersion(output));

    [Fact] public void VersionPolicyRejectsOtherVersions() { Assert.True(CopilotContract.IsSupported("1.0.91")); Assert.False(CopilotContract.IsSupported("1.0.92")); Assert.False(CopilotContract.IsSupported(null)); }
    [Fact] public void MissingExecutableDetected() => Assert.Null(CopilotContract.Detect(root));
    [Fact] public void RelativeAndEmptyPathComponentsAreIgnored() => Assert.Null(CopilotContract.Detect(";.;relative"));
    [Fact] public void DetectsNativeCliWithoutExecutingShellWrapper() { File.WriteAllText(Path.Combine(root, "copilot.exe"), ""); Assert.Equal(Path.Combine(root, "copilot.exe"), CopilotContract.Detect(root)!.Executable); }

    [Fact]
    public void InvocationPreservesMandatoryBoundary()
    {
        var repo = Path.Combine(root, "repo with spaces");
        var args = CopilotContract.ReviewArguments(repo, Path.Combine(root, "logs"));
        Assert.Contains("--disallow-temp-dir", args);
        foreach (var flag in new[] { "--disable-builtin-mcps", "--no-custom-instructions", "--no-remote", "--no-remote-export", "--no-ask-user", "--no-auto-update", "--no-auto-login" }) Assert.Contains(flag, args);
        var info = CopilotContract.StartInfo(new("copilot.exe", []), Run, Profile, Path.Combine(root, "cache"), args);
        Assert.False(info.UseShellExecute);
        Assert.Equal(Run, info.WorkingDirectory);
        Assert.NotEqual(repo, info.WorkingDirectory);
        Assert.Equal(repo, args[1]);
        Assert.Equal("view,grep,glob", args[4]);
        Assert.Equal("view,grep,glob", args[6]);
        Assert.DoesNotContain("--allow-all", args);
        Assert.DoesNotContain("--allow-all-paths", args);
        Assert.DoesNotContain("--prompt", args);
        Assert.DoesNotContain("--resume", args);
        Assert.DoesNotContain("GH_TOKEN", info.Environment.Keys);
        Assert.DoesNotContain("NODE_OPTIONS", info.Environment.Keys);
    }

    [Fact] public void EmptyProfileAndDefenceInDepthSettingAreSafe() { File.WriteAllText(Path.Combine(Profile, "settings.json"), "{\"disableAllHooks\":true}"); Assert.NotEmpty(CopilotPreflight.Inspect(Profile, Run)); }
    [Fact]
    public void StructuralPreflightCannotBypassIncompleteAuthenticatedAcceptance()
    {
        File.WriteAllText(Path.Combine(Profile, "config.json"), "{\"loggedInUsers\":[],\"lastLoggedInUser\":null}");
        Assert.NotEmpty(CopilotPreflight.Inspect(Profile, Run));
        Assert.False(CopilotContract.ReviewContractVerified);
        Assert.Contains("could not authenticate", CopilotContract.ReviewExecutionBlockReason);
    }
    [Fact]
    public void ChildEnvironmentOnlyReadsBenignVariablesAndDropsAuthorityOverrides()
    {
        var requested = new List<string>();
        var info = CopilotContract.StartInfo(new("copilot.exe", []), Run, Profile, Path.Combine(root, "cache"), [], key =>
        {
            requested.Add(key);
            return "synthetic operating system value";
        });
        var authorityVariables = new[]
        {
            "COPILOT_GITHUB_TOKEN", "GH_TOKEN", "GITHUB_TOKEN", "GH_HOST", "COPILOT_GH_HOST", "GH_CONFIG_DIR",
            "COPILOT_ALLOW_ALL", "COPILOT_CUSTOM_INSTRUCTIONS_DIRS", "COPILOT_PROVIDER_API_KEY",
            "COPILOT_PROVIDER_API_KEY_COMMAND", "COPILOT_PROVIDER_BASE_URL", "COPILOT_PROVIDERS_CONFIG",
            "NODE_OPTIONS", "COPILOT_CLI_VERSION", "COPILOT_OTEL_ENABLED", "OTEL_EXPORTER_OTLP_HEADERS"
        };
        foreach (var key in authorityVariables)
        {
            Assert.DoesNotContain(key, requested);
            Assert.DoesNotContain(key, info.Environment.Keys);
        }
        Assert.Equal(Path.GetFullPath(Profile), info.Environment["COPILOT_HOME"]);
        Assert.Equal("false", info.Environment["COPILOT_AUTO_UPDATE"]);
        Assert.Equal("false", info.Environment["USE_TGREP"]);
        foreach (var key in new[] { "GITHUB_COPILOT_PROMPT_MODE_REPO_HOOKS", "GITHUB_COPILOT_PROMPT_MODE_EXTENSIONS" })
        {
            Assert.DoesNotContain(key, requested);
            Assert.Equal("false", info.Environment[key]);
        }
        Assert.Equal(requested.Count + 6, info.Environment.Count);
    }

    [Theory]
    [InlineData(false, false, false, false, "Copilot is unavailable")]
    [InlineData(true, false, false, false, "Copilot version is unsupported")]
    [InlineData(true, true, false, false, "Sign-in status unavailable")]
    [InlineData(true, true, true, false, "Signed in")]
    [InlineData(true, false, false, true, "Copilot setup is blocked")]
    public void AccountStateDoesNotPresentUnknownAuthenticationAsSignedOut(bool available, bool supported, bool authenticated, bool blocked, string expected)
        => Assert.Equal(expected, new CopilotAccountState(available, supported, authenticated, null, null, "", blocked).DisplayStatus);
    [Theory]
    [InlineData("hooks", true)] [InlineData("extensions", true)] [InlineData("installed-plugins", true)]
    [InlineData("mcp-config.json", false)] [InlineData("lsp-config.json", false)] [InlineData("unknown.json", false)]
    public void ExecutableAndUnknownSourcesBlock(string name, bool directory)
    {
        var path = Path.Combine(Profile, name);
        if (directory) { Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "contribution.json"), "{}"); }
        else File.WriteAllText(path, "{}");
        Assert.Throws<InvalidOperationException>(() => CopilotPreflight.Inspect(Profile, Run));
    }
    [Theory]
    [InlineData("{\"disableAllHooks\":true,\"hooks\":{}}")]
    [InlineData("{\"disableAllHooks\":true,\"enabledPlugins\":{}}")]
    [InlineData("{\"disableAllHooks\":true,\"statusLine\":{\"command\":\"unsafe\"}}")]
    [InlineData("{\"disableAllHooks\":false}")]
    public void InlineAndUnknownSettingsBlock(string json) { File.WriteAllText(Path.Combine(Profile, "settings.json"), json); Assert.Throws<InvalidOperationException>(() => CopilotPreflight.Inspect(Profile, Run)); }
    [Fact] public void UnreadableAuthenticationFileFailsClosed()
    {
        var path = Path.Combine(Profile, "config.json");
        File.WriteAllText(path, "opaque synthetic state");
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var error = Assert.Throws<CopilotConfigBlockedException>(() => CopilotPreflight.Inspect(Profile, Run));
        Assert.Equal(CopilotConfigBlock.Unreadable, error.Category);
        Assert.Contains("Credential values were not decoded or returned", error.Message);
    }
    [Fact] public void NonemptyRunDirectoryBlocks() { File.WriteAllText(Path.Combine(Run, ".mcp.json"), "{}"); Assert.Throws<InvalidOperationException>(() => CopilotPreflight.Inspect(Profile, Run)); }
    [Fact] public void RepositoryLanguageServerConfigurationBlocks()
    {
        var repository = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(repository, ".github"));
        File.WriteAllText(Path.Combine(repository, ".github", "lsp.json"), "{}");
        Assert.Throws<InvalidOperationException>(() => CopilotPreflight.Inspect(Profile, Run, repository));
    }

    private static readonly string[] ValidFrames =
    [
        "{\"type\":\"session.tools_updated\",\"data\":{\"model\":\"fixture\"}}",
        "{\"type\":\"session.mcp_servers_loaded\",\"data\":{\"servers\":[{\"name\":\"github-mcp-server\",\"status\":\"disabled\"},{\"name\":\"githubiq\",\"status\":\"disabled\"}]}}",
        "{\"type\":\"tool.execution_start\",\"data\":{\"toolName\":\"view\"}}",
        "{\"type\":\"session.usage_checkpoint\",\"data\":{\"tools\":[{\"name\":\"view\"},{\"name\":\"grep\"},{\"name\":\"glob\"}]}}",
        "{\"type\":\"assistant.message\",\"data\":{\"content\":\"{\\\"findings\\\":[]}\"}}",
        "{\"type\":\"result\"}"
    ];
    private static CopilotStreamValidator Stream(IEnumerable<string>? frames = null) { var v = new CopilotStreamValidator(); foreach (var f in frames ?? ValidFrames) v.Accept(f); return v; }
    [Fact] public void ManifestAfterFirstExecutionAndValidZeroFindingsAreAccepted() => Assert.Empty(Stream().Complete(0, false).Findings);
    [Fact] public void NonzeroExitRejected() => Assert.Throws<InvalidOperationException>(() => Stream().Complete(1, false));
    [Fact] public void CancellationRejectsEvenCompleteFrames() => Assert.Throws<OperationCanceledException>(() => Stream().Complete(0, true));
    [Fact] public void MissingTerminalRejected() => Assert.Throws<InvalidOperationException>(() => Stream(ValidFrames.SkipLast(1)).Complete(0, false));
    [Fact] public void MissingManifestRejected() => Assert.Throws<InvalidOperationException>(() => Stream(ValidFrames.Where(f => !f.Contains("usage_checkpoint"))).Complete(0, false));
    [Fact] public void MissingMcpEvidenceRejected() => Assert.Throws<InvalidOperationException>(() => Stream(ValidFrames.Where(f => !f.Contains("mcp_servers_loaded"))).Complete(0, false));
    [Fact] public void ExtraToolInManifestRejected() => Assert.Throws<InvalidOperationException>(() => Stream(ValidFrames.Select(f => f.Replace("\"name\":\"glob\"", "\"name\":\"powershell\""))).Complete(0, false));
    [Fact] public void UnexpectedToolExecutionRejected() => Assert.Throws<InvalidOperationException>(() => Stream(ValidFrames.Select(f => f.Replace("\"toolName\":\"view\"", "\"toolName\":\"edit\""))).Complete(0, false));
    [Fact] public void UnexpectedMcpRejected() => Assert.Throws<InvalidOperationException>(() => Stream(ValidFrames.Select(f => f.Replace("githubiq", "custom"))).Complete(0, false));
    [Fact] public void EnabledMcpRejected() => Assert.Throws<InvalidOperationException>(() => Stream(ValidFrames.Select(f => f.Replace("disabled", "connected"))).Complete(0, false));
    [Fact] public void MalformedJsonlRejected() => Assert.Throws<InvalidOperationException>(() => Stream(ValidFrames.Prepend("{partial")).Complete(0, false));
    [Fact] public void DuplicateTerminalRejected() => Assert.Throws<InvalidOperationException>(() => Stream(ValidFrames.Append(ValidFrames[^1])).Complete(0, false));
    [Fact] public void ContradictoryTerminalStatusRejected() => Assert.Throws<InvalidOperationException>(() => Stream(ValidFrames.Select(f => f == ValidFrames[^1] ? "{\"type\":\"result\",\"data\":{\"success\":false}}" : f)).Complete(0, false));
    [Fact] public void UnexpectedHookEvidenceRejected() => Assert.Throws<InvalidOperationException>(() => Stream(ValidFrames.Prepend("{\"type\":\"hook.execution_start\"}")).Complete(0, false));
    [Fact] public void DuplicateJsonFieldsRejected() => Assert.Throws<InvalidOperationException>(() => Stream(ValidFrames.Prepend("{\"type\":\"x\",\"type\":\"y\"}")).Complete(0, false));
    [Fact] public void UnknownInformationalFrameTolerated() => Assert.Empty(Stream(ValidFrames.Prepend("{\"type\":\"session.future_information\",\"data\":{}} ")).Complete(0, false).Findings);
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"findings\":null}")]
    [InlineData("{\"findings\":[{\"title\":\"incomplete\"}]}")]
    [InlineData("{\"findings\":[],\"findings\":[{}]}")]
    public void InvalidStructuredFindingsRejected(string json) => Assert.Throws<InvalidOperationException>(() => CopilotStreamValidator.ParseResult(json));

    [Fact]
    public async Task HistoryDiscardsPartialAndStaleFindings()
    {
        var store = new ReviewHistoryStore(root);
        var projectId = Guid.NewGuid();
        await store.SaveAsync(new ReviewRecord { ProjectId = projectId, Status = ReviewStatus.Stale, Result = new ReviewResult { Findings = [new ReviewFinding { Title = "partial" }] } });
        var loaded = Assert.Single(await store.LoadAsync(projectId));
        Assert.Equal(ReviewStatus.Stale, loaded.Status);
        Assert.Empty(loaded.Result.Findings);
    }

    [Fact]
    public async Task CancellationTerminatesControlledProcessAndItsChild()
    {
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var info = new ProcessStartInfo(powershell)
        {
            WorkingDirectory = Run, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add("$child = Start-Process -FilePath \"$env:SystemRoot\\System32\\ping.exe\" -ArgumentList '127.0.0.1 -t' -WindowStyle Hidden -PassThru; Write-Output $child.Id; Start-Sleep -Seconds 60");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var childStarted = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = CopilotProcess.RunAsync(info, null, line => { if (int.TryParse(line, out var id)) childStarted.TrySetResult(id); }, cancellation.Token);
        var childId = await childStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var child = Process.GetProcessById(childId);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(child.HasExited);
    }
}
