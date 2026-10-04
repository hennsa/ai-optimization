using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AIReviewDesk.App;
using AIReviewDesk.Core;
using AIReviewDesk.Infrastructure;
using Repo = AIReviewDesk.Tests.GitReviewContextTests.TemporaryGitRepository;

namespace AIReviewDesk.Tests;

public sealed class ModelUsageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ARD-ModelTests-" + Guid.NewGuid().ToString("N"));
    public ModelUsageTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    [Theory]
    [InlineData("claude-sonnet-5.5", "high")]
    [InlineData("claude-sonnet-5", "max")]
    [InlineData("claude-sonnet-5.5", "auto")]
    [InlineData("auto", "auto")]
    public void Verified_combinations_pass(string model, string effort) => CopilotModelPolicy.Validate(new(model, effort), "1.0.91");

    [Theory]
    [InlineData("gpt-6-luna", "high")]
    [InlineData("made-up", "high")]
    [InlineData("claude-haiku-4.5", "high")]
    [InlineData("claude-haiku-4.5", "auto")]
    [InlineData("claude-sonnet-5", "minimal")]
    [InlineData("auto", "high")]
    public void Unsupported_or_authority_incompatible_combinations_block(string model, string effort) =>
        Assert.Throws<ReviewValidationException>(() => CopilotModelPolicy.Validate(new(model, effort), "1.0.91"));

    [Fact] public void New_CLI_versions_do_not_inherit_model_verification() =>
        Assert.Throws<ReviewValidationException>(() => CopilotModelPolicy.Validate(CopilotModelPolicy.Default, "1.0.92"));

    [Theory]
    [InlineData("auto", "auto", false, false)]
    [InlineData("claude-sonnet-5.5", "auto", true, false)]
    [InlineData("claude-sonnet-5.5", "high", true, true)]
    public void Execution_flags_only_append_to_exact_production_authority(string model, string effort, bool modelFlag, bool effortFlag)
    {
        var baseline = CopilotContract.ReviewArguments(root, root);
        var arguments = CopilotContract.ReviewArguments(root, root, new(model, effort), Path.Combine(root, "usage.json"));
        Assert.Equal(baseline, arguments.Take(baseline.Count));
        Assert.Equal(modelFlag, arguments.Contains("--model")); Assert.Equal(effortFlag, arguments.Contains("--reasoning-effort"));
        if (modelFlag) Assert.Equal(model, arguments[arguments.ToList().IndexOf("--model") + 1]);
        if (effortFlag) Assert.Equal(effort, arguments[arguments.ToList().IndexOf("--reasoning-effort") + 1]);
        Assert.DoesNotContain("--allow-all", arguments); Assert.DoesNotContain("--no-auto-login", arguments);
        Assert.Equal(Path.Combine(root, "usage.json"), arguments.Last());
    }

    [Fact]
    public void Metadata_has_same_security_flags_but_no_repository_or_session_authority()
    {
        var metadata = CopilotContract.MetadataArguments(root);
        var review = CopilotContract.ReviewArguments(root, root);
        foreach (var flag in new[] { "--disallow-temp-dir", "--disable-builtin-mcps", "--no-custom-instructions", "--no-remote", "--no-remote-export", "--no-ask-user", "--no-auto-update", "--no-eager-powershell-resolution", "--no-experimental" })
        { Assert.Contains(flag, metadata); Assert.Contains(flag, review); }
        Assert.Equal("view,grep,glob", metadata[metadata.ToList().IndexOf("--available-tools") + 1]);
        Assert.Equal(CopilotContract.DeniedTools, metadata[metadata.ToList().IndexOf("--deny-tool") + 1]);
        Assert.DoesNotContain("--add-dir", metadata); Assert.DoesNotContain("--auth-token-env", metadata);
    }

    [Fact]
    public async Task Metadata_path_rejects_credential_and_agent_RPCS_before_any_process_access()
    {
        using var process = new Process(); var rpc = new CopilotMetadataRpc(process);
        foreach (var method in new[] { "account.getCurrentAuth", "account.getAllUsers", "auth.getStatus", "session.create", "session.send", "session.delete", "tools.list" })
            await Assert.ThrowsAsync<InvalidOperationException>(() => rpc.RequestAsync(method, CancellationToken.None));
    }

    [Fact]
    public async Task Framed_transport_validates_length_and_duplicate_properties()
    {
        var payload = Encoding.UTF8.GetBytes("{\"result\":{\"name\":\"mødèl\"}}");
        using var input = new MemoryStream(Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n").Concat(payload).ToArray());
        using var doc = await CopilotMetadataRpc.ReadFrameAsync(input, CancellationToken.None);
        Assert.Equal("mødèl", doc.RootElement.GetProperty("result").GetProperty("name").GetString());
        foreach (var frame in new[] { "Content-Length: 9999999\r\n\r\n", "Content-Length: 1\r\nContent-Length: 1\r\n\r\n0", "Content-Length: 13\r\n\r\n{\"a\":1,\"a\":2}" })
        {
            using var invalid = new MemoryStream(Encoding.ASCII.GetBytes(frame));
            await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotMetadataRpc.ReadFrameAsync(invalid, CancellationToken.None));
        }
    }

    [Fact]
    public void Models_are_account_intersection_with_pinned_policy_and_model_specific_efforts()
    {
        using var json = JsonDocument.Parse("""{"models":[{"id":"gpt-6-luna"},{"id":"claude-sonnet-5.5","supportedReasoningEfforts":["low","high","new-effort"],"policy":{"state":"enabled"}},{"id":"claude-sonnet-5","policy":{"state":"disabled"}},{"id":"claude-haiku-4.5"}]}""");
        var models = CopilotMetadataParser.Models(json.RootElement);
        Assert.Equal(new[] { "auto", "gpt-6-luna", "claude-sonnet-5.5", "claude-haiku-4.5" }, models.Select(m => m.Id));
        Assert.Equal(new[] { "auto", "low", "high", "new-effort" }, models[2].Efforts);
        Assert.Throws<ReviewValidationException>(() => CopilotModelPolicy.Validate(new("claude-sonnet-5.5", "max"), "1.0.91", new CertificationRegistry(root).Selectable(models, "1.0.91")));
    }

    [Theory]
    [InlineData("1.0.92", 3)] [InlineData("1.0.91", 4)]
    public void Unsupported_SDK_CLI_combination_is_rejected(string version, int protocol)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { version, protocolVersion = protocol }));
        Assert.Throws<InvalidOperationException>(() => CopilotMetadataParser.ValidateRuntime(json.RootElement));
    }

    [Fact]
    public async Task Project_defaults_persist_and_New_review_prepopulates_but_allows_overrides()
    {
        using var repo = new Repo();
        var project = repo.Project with { DefaultExecution = new("claude-sonnet-5", "medium") };
        var vm = new DeskViewModel(root); vm.RebuildProfileChoices(["standard"]);
        await vm.SaveProjectAsync(project, true);
        vm.ShowNewReviewForm(); Assert.Equal(project.DefaultExecution, vm.Execution);
        vm.SelectedEffort = "high"; Assert.Equal("high", vm.Execution.ReasoningEffort);
        Assert.Equal("medium", vm.Selected!.DefaultExecution.ReasoningEffort);
        vm.ShowNewReviewForm(); Assert.Equal(project.DefaultExecution, vm.Execution);
        var saved = Assert.Single((await new RegistryStore(root).LoadAsync()).State.Projects);
        Assert.Equal(project.DefaultExecution, saved.DefaultExecution);
    }

    [Fact]
    public void Model_changes_preserve_valid_effort_and_explain_fallback()
    {
        var vm = new DeskViewModel(root);
        vm.SelectedEffort = "medium"; vm.SelectedModelId = "claude-sonnet-5"; Assert.Equal("medium", vm.SelectedEffort);
        vm.SelectedModelId = "auto"; Assert.Equal("auto", vm.SelectedEffort); Assert.NotEmpty(vm.ExecutionNotice);
        Assert.Single(vm.EffortChoices); Assert.Contains("leaves both settings", vm.ReasoningHint);
        vm.SelectedEffort = "high"; Assert.Equal("auto", vm.SelectedEffort);
        vm.SelectedModelId = "auto"; Assert.Single(vm.EffortChoices); Assert.Contains("no override", vm.AutoExplanation);
    }

    [Fact]
    public void Refresh_keeps_binding_collections_and_selects_current_choice_objects()
    {
        var vm = new DeskViewModel(root); vm.SelectedEffort = "medium";
        var models = vm.Models; var efforts = vm.EffortChoices;
        var refreshed = CopilotModelPolicy.Verified.Select(m => m with { Efforts = m.Efforts.ToArray() }).Reverse().ToArray();
        vm.SetMetadata(new(refreshed, null, "Unavailable"));
        Assert.Same(models, vm.Models); Assert.Same(efforts, vm.EffortChoices);
        Assert.Same(vm.Models.Single(m => m.Id == "claude-sonnet-5.5"), vm.SelectedModel);
        Assert.Contains(vm.SelectedReasoningChoice!, vm.EffortChoices);
        Assert.Equal(new ReviewExecutionSettings("claude-sonnet-5.5", "medium"), vm.Execution);
    }

    [Fact]
    public async Task Review_again_reuses_execution_and_prepares_fresh_context()
    {
        using var repo = new Repo();
        repo.Write("changed.txt", "original"); repo.Commit("fixture");
        File.AppendAllText(Path.Combine(repo.Root, "changed.txt"), "\nnew snapshot marker");
        var project = repo.Project;
        var vm = new DeskViewModel(root); vm.RebuildProfileChoices(["standard"]); await vm.SaveProjectAsync(project, true);
        var requested = new ReviewExecutionSettings("claude-sonnet-5", "low");
        vm.ShowHistoryRecord(new ReviewRecord { ProjectId = project.Id, ProfileIds = ["standard"], RequestedExecution = requested });
        var setup = await vm.UsePreviousSetupAsync(); Assert.Equal(requested, setup!.Execution); Assert.Equal(requested, vm.Execution);
        Assert.Null(vm.PreparedInput);
        var prompt = await vm.PreparePromptAsync(ReviewScope.WorkingChanges, []);
        Assert.Contains("new snapshot marker", prompt); Assert.DoesNotContain("Requested model", prompt);
        Assert.Contains("Claude Sonnet 5", vm.ExecutionDisplay);
    }

    [Fact]
    public void Unavailable_historical_selection_falls_back_with_explanation_and_legacy_is_unknown()
    {
        var setup = ReusedReviewSetup.From(new ReviewRecord { RequestedExecution = new("old-model", "high") }, [], true);
        Assert.Equal(new ReviewExecutionSettings(), setup.Execution); Assert.Contains("Execution settings adjusted", setup.Message);
        var legacy = new ReviewDetails(new()); Assert.Contains("not recorded", legacy.ExecutionText); Assert.Contains("unavailable", legacy.ExecutionText);
    }

    [Theory] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Legacy_history_and_requested_observed_metadata_roundtrip(int schema)
    {
        var projectId = Guid.NewGuid();
        var record = new ReviewRecord { SchemaVersion = schema, ProjectId = projectId, Status = ReviewStatus.Completed,
            RequestedExecution = new("auto", "auto"), CopilotModel = "claude-sonnet-5", CopilotCliVersion = "1.0.91",
            Usage = CopilotUsageParser.Parse(UsageFixture) };
        await new ReviewHistoryStore(root).SaveAsync(record);
        var loaded = Assert.Single(await new ReviewHistoryStore(root).LoadAsync(projectId));
        Assert.Equal("claude-sonnet-5", loaded.CopilotModel);
        Assert.Equal(schema == 4, loaded.RequestedExecution != null); Assert.Equal(schema == 4, loaded.Usage != null);
        var details = new ReviewDetails(loaded);
        if (schema == 4)
        {
            Assert.Contains("Requested model: Auto", details.ExecutionText); Assert.Contains("Observed model: claude-sonnet-5", details.ExecutionText);
            Assert.Contains("not reported", details.ExecutionText); Assert.Equal(1.5m, loaded.Usage!.AiCredits);
            Assert.Contains("Requested model: Auto", HandoffFormatter.FormatResult(loaded));
        }
    }

    private const string UsageFixture = """{"totalNanoAiu":1500000000,"totalPremiumRequestCost":1,"modelMetrics":{"claude-sonnet-5":{"usage":{"inputTokens":100,"outputTokens":20,"cacheReadTokens":50,"cacheWriteTokens":10,"reasoningTokens":5}}}}""";
    [Fact]
    public async Task Invalid_optional_history_usage_is_omitted_without_losing_completed_result()
    {
        var projectId = Guid.NewGuid(); var store = new ReviewHistoryStore(root);
        foreach (var usage in new ReviewUsage[] { new([new(null!, 1, null, null, null, null)], null, null), new([], -1, null), new([], 1_000_000_000_000_000_001m, null), new([new("m", null, null, null, null, null)], null, null) })
            await store.SaveAsync(new ReviewRecord { SchemaVersion = 4, ProjectId = projectId, Status = ReviewStatus.Completed, Usage = usage, Result = new ReviewResult { Summary = "Accepted" } });
        var records = await store.LoadAsync(projectId);
        Assert.Equal(4, records.Count);
        Assert.All(records, r => { Assert.Null(r.Usage); Assert.Equal("Accepted", r.Result.Summary); });
    }
    [Fact]
    public void Structured_usage_preserves_runtime_totals_and_separate_cache_counts()
    {
        var usage = CopilotUsageParser.Parse(UsageFixture); var model = Assert.Single(usage.Models);
        Assert.Equal(100, model.InputTokens); Assert.Equal(50, model.CacheReadTokens); Assert.Equal(10, model.CacheWriteTokens);
        Assert.Equal(20, model.OutputTokens); Assert.Equal(1.5m, usage.AiCredits);
        Assert.Contains("includes cache", usage.Display); Assert.DoesNotContain("tokens left", usage.Display);
        var partial = CopilotUsageParser.Parse("{\"totalPremiumRequestCost\":1}"); Assert.Null(partial.NanoAiUnits); Assert.Empty(partial.Models);
    }
    [Theory]
    [InlineData("{\"totalNanoAiu\":-1}")]
    [InlineData("{\"totalNanoAiu\":\"12\"}")]
    [InlineData("{\"totalNanoAiu\":1,\"totalNanoAiu\":2}")]
    [InlineData("{\"modelMetrics\":{\"m\":{\"usage\":{\"inputTokens\":0.5}}}}")]
    [InlineData("{}")]
    [InlineData("{\"modelMetrics\":{\"m\":{\"usage\":{}}}}")]
    public void Malformed_usage_never_becomes_a_fabricated_zero(string json) => Assert.Throws<InvalidOperationException>(() => CopilotUsageParser.Parse(json));

    [Theory] [InlineData(true, "AI credits")] [InlineData(false, "Premium requests")]
    public void Quota_success_uses_actual_units_and_omits_bogus_reset_date(bool tokenBased, string unit)
    {
        var json = JsonSerializer.Serialize(new { quotaSnapshots = new { premium_interactions = new { hasQuota = true, entitlementRequests = 300, usedRequests = 42, remainingPercentage = 86, tokenBasedBilling = tokenBased, isUnlimitedEntitlement = false, resetDate = "2026-10-03T18:00:00Z" } } });
        using var document = JsonDocument.Parse(json);
        var quota = CopilotMetadataParser.Quota(document.RootElement, DateTimeOffset.UtcNow)!;
        Assert.Equal(unit, quota.Unit); Assert.Contains("86% remaining", quota.Display); Assert.Contains("42 of 300 used", quota.Display);
        Assert.Contains("Reset date unavailable", quota.Display); Assert.DoesNotContain("tokens left", quota.Display);
        var vm = new DeskViewModel(root); vm.SetMetadata(new(CopilotModelPolicy.Verified, quota, "")); Assert.Equal(quota.Display, vm.CopilotUsage);
    }

    [Fact]
    public void Quota_unavailable_and_model_refresh_are_truthful()
    {
        var vm = new DeskViewModel(root); vm.SetMetadata(CopilotMetadata.Unavailable);
        Assert.Contains("quota is not available", vm.CopilotUsage); Assert.DoesNotContain("0%", vm.CopilotUsage);
        Assert.Equal(new ReviewExecutionSettings(), vm.Execution); Assert.NotEmpty(vm.ExecutionNotice);
        using var absent = JsonDocument.Parse("{\"quotaSnapshots\":{}}"); Assert.Null(CopilotMetadataParser.Quota(absent.RootElement, DateTimeOffset.UtcNow));
        using var malformed = JsonDocument.Parse("""{"quotaSnapshots":{"premium_interactions":{"hasQuota":true,"entitlementRequests":300,"usedRequests":42,"remainingPercentage":101,"tokenBasedBilling":false,"isUnlimitedEntitlement":false}}}""");
        Assert.Throws<InvalidOperationException>(() => CopilotMetadataParser.Quota(malformed.RootElement, DateTimeOffset.UtcNow));
    }
}
