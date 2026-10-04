using System.Text.Json;
using AIReviewDesk.Core;
using AIReviewDesk.Infrastructure;
using AIReviewDesk.App;

namespace AIReviewDesk.Tests;

public sealed class CertificationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ARD-CertTests-" + Guid.NewGuid().ToString("N"));
    private readonly CopilotModelChoice luna = new("gpt-6-luna", "GPT-6 Luna", ["auto", "low", "high", "max"]);
    public CertificationTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    private CertificationRegistry Registry => new(root);
    private ModelCertificate Certificate => new()
    {
        ModelId = luna.Id, DisplayName = luna.Name, CliVersion = "1.0.91", SuiteVersion = CertificationContract.SuiteVersion,
        AuthorityContract = CertificationContract.AuthorityVersion, OutputContract = CertificationContract.OutputVersion,
        TestedAt = DateTimeOffset.UtcNow, ReasoningEfforts = ["high"], ExpectedTools = ["view", "rg", "glob"],
        TechnicalTools = ["view", "grep", "glob"], Probes = CertificationContract.RequiredProbes.Select(p => new CertificationProbe(p, true)).ToArray(), Status = CertificationStatus.Certified
    };
    [Fact] public void Discovery_preserves_unknown_models_without_selecting_them()
    {
        using var doc = JsonDocument.Parse("""{"models":[{"id":"future-model","name":"Future model","supportedReasoningEfforts":["high","max"]}]}""");
        var live = CopilotMetadataParser.Models(doc.RootElement);
        var row = Registry.Discover(live, "1.0.91").Single(r => r.Model.Id == "future-model");
        Assert.Equal(CertificationStatus.Unverified, row.Status); Assert.Equal(new[] { "auto", "high", "max" }, row.Model.Efforts);
        Assert.Single(Registry.Selectable(live, "1.0.91"));
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void Discovery_cannot_turn_model_or_effort_values_into_CLI_options(bool model)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new { models = new[] { new { id = model ? "--allow-all-tools" : "future-model", supportedReasoningEfforts = new[] { model ? "high" : "--allow-all-tools" } } } }));
        Assert.Throws<InvalidOperationException>(() => CopilotMetadataParser.Models(doc.RootElement));
        Assert.Single(Registry.Selectable([luna], "1.0.91"));
    }
    [Fact] public void Bundled_baseline_preserves_Sonnet_evidence_and_Auto_is_separate()
    {
        Assert.Equal(2, CertificationContract.BundledModels.Count);
        Assert.All(CertificationContract.BundledModels, c => { Assert.Equal("bundled certification", c.Source); Assert.Equal(new[] { "view", "grep", "glob" }, c.ExpectedTools); });
        Assert.DoesNotContain(CertificationContract.BundledModels, c => c.ModelId == "auto");
        Assert.Equal(3, Registry.Selectable(CopilotModelPolicy.Verified, "1.0.91").Count);
    }
    [Fact] public void Prompt_size_boundary_is_explicit_character_based_and_counts_UTF8_bytes_separately()
    {
        var boundary = ReviewContextCapability.LargePromptCharacterBoundary;
        Assert.Equal(ReviewContextClass.Normal, ReviewContextCapability.Classify(boundary - 1));
        Assert.Equal(ReviewContextClass.Large, ReviewContextCapability.Classify(boundary));
        Assert.Equal(ReviewContextClass.Large, ReviewContextCapability.Classify(1_565_905));
        Assert.Equal(ReviewContextClass.Large, ReviewContextCapability.Classify(ReviewContextCapability.MaximumPromptCharacterBound));
        Assert.Equal(ReviewContextClass.Unsupported, ReviewContextCapability.Classify(ReviewContextCapability.MaximumPromptCharacterBound + 1));
        var evidence = ReviewContextCapability.Measure(new string('x', boundary - 1) + "é");
        Assert.Equal(boundary, evidence.CharacterCount); Assert.Equal(boundary + 1, evidence.Utf8ByteCount);
        Assert.Equal(ReviewContextCapability.BoundaryVersion, evidence.BoundaryVersion);
    }
    [Fact] public void Basic_certificate_does_not_authorize_large_and_context_capability_drift_is_scoped()
    {
        var original = luna with { Context = new(2_000_000, 32_000, 2_000_000, ["large", "standard"]) };
        Assert.NotNull(CertificationContract.LargeContextInvalidReason(Certificate, original, "1.0.91", "high"));
        var large = new LargeContextCertificate
        {
            CliVersion = "1.0.91", ModelId = luna.Id, ReasoningEffort = "high", AuthorityContract = CertificationContract.AuthorityVersion,
            ToolContract = CertificationContract.ToolVersion, ExpectedTools = Certificate.ExpectedTools,
            OutputContract = CertificationContract.OutputVersion, OutputEnvelopeId = OutputEnvelope.RawJson, OutputEnvelopeVersion = OutputEnvelope.Version,
            SuiteVersion = ReviewContextCapability.LargeSuiteVersion, BoundaryVersion = ReviewContextCapability.BoundaryVersion,
            TestedPromptCharacters = ReviewContextCapability.MaximumPromptCharacterBound, TestedPromptUtf8Bytes = ReviewContextCapability.MaximumPromptCharacterBound,
            ContextCapabilityAtTest = original.Context, Status = CertificationStatus.Certified,
            Probes = [new("zero findings", true), new("deliberate defect", true)],
            EnvelopeObservations = [new("zero findings", OutputEnvelope.RawJson, OutputEnvelope.Version, true, true, true), new("deliberate defect", OutputEnvelope.RawJson, OutputEnvelope.Version, true, true, true)]
        };
        var certified = Certificate with { LargeContext = large };
        Assert.Null(CertificationContract.LargeContextInvalidReason(certified, original, "1.0.91", "high"));
        Assert.Null(CertificationContract.LargeContextInvalidReason(certified, original, "1.0.91", "high", 1_565_905));
        Assert.Null(CertificationContract.LargeContextInvalidReason(certified, original, "1.0.91", "high", 1_600_000));
        Assert.Contains("supported context size", CertificationContract.LargeContextInvalidReason(certified, original, "1.0.91", "high", 1_600_001));
        Assert.NotNull(CertificationContract.LargeContextInvalidReason(certified, original, "1.0.92", "high"));
        Assert.NotNull(CertificationContract.LargeContextInvalidReason(certified, original, "1.0.91", "max"));
        Assert.NotNull(CertificationContract.LargeContextInvalidReason(certified with { AuthorityContract = "changed" }, original, "1.0.91", "high"));
        Assert.NotNull(CertificationContract.LargeContextInvalidReason(certified with { OutputContract = "changed" }, original, "1.0.91", "high"));
        Assert.NotNull(CertificationContract.LargeContextInvalidReason(certified with { LargeContext = large with { OutputEnvelopeId = OutputEnvelope.SingleJsonFence } }, original, "1.0.91", "high"));
        Assert.NotNull(CertificationContract.LargeContextInvalidReason(certified with { LargeContext = large with { ToolContract = "changed" } }, original, "1.0.91", "high"));
        Assert.NotNull(CertificationContract.LargeContextInvalidReason(certified with { LargeContext = large with { SuiteVersion = "old" } }, original, "1.0.91", "high"));
        Assert.NotNull(CertificationContract.LargeContextInvalidReason(certified, original with { Context = original.Context! with { MaxPromptTokens = 2_100_000 } }, "1.0.91", "high"));
        Assert.Null(CertificationContract.LargeContextInvalidReason(certified, original with { Name = "Cosmetic live name", Context = original.Context! with { SupportedContextTiers = ["standard", "large"] } }, "1.0.91", "high"));
        Assert.NotNull(CertificationContract.LargeContextInvalidReason(certified with { LargeContext = large with { BoundaryVersion = "large-context-1", SuiteVersion = "large-context-suite-1" } }, original, "1.0.91", "high"));
    }
    [Fact] public async Task Large_capability_is_additive_persisted_and_legacy_basic_never_infers_it()
    {
        await Registry.SaveAsync(Certificate);
        var old = Registry.ReadLocal().Single(); Assert.Null(old.LargeContext);
        var large = new LargeContextCertificate { CliVersion = "1.0.91", ModelId = luna.Id, ReasoningEffort = "high", Status = CertificationStatus.Certified,
            SuiteVersion = ReviewContextCapability.LargeSuiteVersion, BoundaryVersion = ReviewContextCapability.BoundaryVersion,
            AuthorityContract = CertificationContract.AuthorityVersion, OutputContract = CertificationContract.OutputVersion,
            ToolContract = CertificationContract.ToolVersion, ExpectedTools = Certificate.ExpectedTools,
            OutputEnvelopeId = OutputEnvelope.RawJson, OutputEnvelopeVersion = OutputEnvelope.Version, TestedPromptCharacters = ReviewContextCapability.MaximumPromptCharacterBound, TestedPromptUtf8Bytes = ReviewContextCapability.MaximumPromptCharacterBound,
            ActualInputTokens = 900_000, ActualOutputTokens = 600, Probes = [new("zero findings", true), new("deliberate defect", true)],
            EnvelopeObservations = [new("zero findings", OutputEnvelope.RawJson, OutputEnvelope.Version, true, true, true), new("deliberate defect", OutputEnvelope.RawJson, OutputEnvelope.Version, true, true, true)] };
        await Registry.SaveAsync(Certificate with { LargeContext = large });
        var restarted = new CertificationRegistry(root).ReadLocal().Single();
        Assert.Equal(CertificationStatus.Certified, restarted.Status); Assert.Equal(1_600_000, restarted.LargeContext!.TestedPromptCharacters);
        Assert.Null(CertificationContract.LargeContextInvalidReason(restarted, luna, "1.0.91", "high", 1_565_905));
        var json = File.ReadAllText(Registry.FilePath); Assert.DoesNotContain("SyntheticCorpus", json); Assert.DoesNotContain("ARD app-owned", json);
    }
    [Fact] public async Task Existing_Sonnet_needs_retest_record_remains_basic_only()
    {
        var sonnet = CertificationContract.BundledModels.Single(model => model.ModelId == "claude-sonnet-5.5") with
        { Status = CertificationStatus.NeedsRetest, Source = "locally certified", FailureReason = "Output contract failed in broad review." };
        await Registry.SaveAsync(sonnet);
        var row = Registry.Discover([new(sonnet.ModelId, sonnet.DisplayName, sonnet.ReasoningEfforts)], "1.0.91").Single(candidate => candidate.Model.Id == sonnet.ModelId);
        Assert.Equal(CertificationStatus.NeedsRetest, row.Status); Assert.Null(row.Certificate!.LargeContext);
    }
    [Fact] public async Task Synthetic_large_fixture_composes_exactly_to_product_ceiling_without_customer_data_and_is_cleaned()
    {
        string rootPath;
        await using (var fixture = await CertificationFixture.CreateAsync(root))
        {
            rootPath = fixture.Root;
            var evidence = await fixture.PrepareLargeContextAsync();
            Assert.Equal(ReviewContextCapability.MaximumPromptCharacterBound, evidence.CharacterCount);
            Assert.Equal(ReviewContextClass.Large, evidence.ContextClass);
            Assert.Equal(evidence.CharacterCount, evidence.Utf8ByteCount);
            var synthetic = Path.Combine(fixture.Repository, "SyntheticCorpus.cs");
            Assert.True(new FileInfo(synthetic).Length > 1_500_000);
            Assert.Contains("ARD app-owned deterministic synthetic repository source. No customer data.", File.ReadAllText(synthetic));
            Assert.Contains("ReadMetric000000", File.ReadAllText(synthetic));
        }
        Assert.False(Directory.Exists(rootPath));
    }
    [Fact] public async Task Successful_local_test_materialises_after_restart_without_changing_defaults()
    {
        await Registry.SaveAsync(Certificate);
        var restarted = new CertificationRegistry(root);
        Assert.Equal(new[] { "auto", "gpt-6-luna" }, restarted.Selectable([luna], "1.0.91").Select(m => m.Id));
        Assert.Equal(new[] { "high" }, restarted.Selectable([luna], "1.0.91")[1].Efforts);
        Assert.False(File.Exists(Path.Combine(root, "app-state.json"))); Assert.False(Directory.Exists(Path.Combine(root, "Reviews")));
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }
    [Fact] public async Task Live_Luna_name_is_preferred_without_rewriting_certificate_or_trust_identity()
    {
        await Registry.SaveAsync(Certificate with { DisplayName = luna.Id });
        var live = luna with { Name = "GPT-6 Luna live name" };
        var vm = new DeskViewModel(root); vm.SetMetadata(new([live], null, "")); vm.SelectedModelId = luna.Id;
        Assert.Contains(live.Name, vm.ExecutionDisplay); Assert.Equal(luna.Id, vm.Execution.ModelId);
        Assert.Equal(new[] { "high" }, vm.Models.Single(m => m.Id == luna.Id).Efforts);
        Assert.Equal(luna.Id, Registry.ReadLocal().Single().DisplayName);
        Assert.Equal(luna.Id, Registry.Resolve(vm.Execution, [live], "1.0.91").ModelId);
        var absent = Registry.Discover([], "1.0.91").Single(r => r.Model.Id == luna.Id);
        Assert.Equal(CertificationStatus.NoLongerAdvertised, absent.Status); Assert.Equal(luna.Id, absent.Model.Name);
        Assert.Single(Registry.Selectable([], "1.0.91"));
    }
    [Theory] [InlineData("broken")] [InlineData("{\"SchemaVersion\":2,\"Models\":[]}")] [InlineData("{\"SchemaVersion\":1,\"Models\":[{}]}")]
    public void Malformed_future_and_incomplete_records_never_authorize_a_model(string json)
    {
        File.WriteAllText(Registry.FilePath, json);
        var r = Registry; Assert.Empty(r.ReadLocal()); Assert.NotNull(r.LoadWarning);
        Assert.Single(r.Selectable([luna], "1.0.91")); Assert.Equal(CertificationStatus.NeedsRetest, r.Discover([luna], "1.0.91")[0].Status);
    }
    [Fact] public async Task Future_schema_is_not_overwritten()
    {
        File.WriteAllText(Registry.FilePath, "{\"SchemaVersion\":2,\"Models\":[]}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Registry.SaveAsync(Certificate));
        Assert.Contains("\"SchemaVersion\":2", File.ReadAllText(Registry.FilePath));
    }
    [Theory] [InlineData("cli")] [InlineData("suite")] [InlineData("authority")] [InlineData("output")] [InlineData("tool")] [InlineData("probe")]
    public async Task Relevant_contract_changes_require_retest(string change)
    {
        var c = change switch { "suite" => Certificate with { SuiteVersion = "old" }, "authority" => Certificate with { AuthorityContract = "old" },
            "output" => Certificate with { OutputContract = "old" }, "tool" => Certificate with { ExpectedTools = ["view", "unknown_search", "glob"] },
            "probe" => Certificate with { Probes = [] }, _ => Certificate };
        await Registry.SaveAsync(c);
        Assert.Equal(CertificationStatus.NeedsRetest, Registry.Discover([luna], change == "cli" ? "1.0.92" : "1.0.91").Single(r => r.Model.Id == luna.Id).Status);
        Assert.Single(Registry.Selectable([luna], change == "cli" ? "1.0.92" : "1.0.91"));
    }
    [Fact] public async Task Disappearance_suspends_and_reappearance_requires_explicit_retest()
    {
        await Registry.SaveAsync(Certificate);
        await Registry.ObserveDiscoveryAsync([]);
        Assert.Equal(CertificationStatus.NoLongerAdvertised, Registry.Discover([], "1.0.91").Single(r => r.Model.Id == luna.Id).Status);
        Assert.Equal(CertificationStatus.NeedsRetest, Registry.Discover([luna], "1.0.91").Single(r => r.Model.Id == luna.Id).Status);
        await Registry.SaveAsync(Certificate); Assert.Equal(2, Registry.Selectable([luna], "1.0.91").Count);
    }
    [Fact] public async Task Rejected_evidence_is_explanatory_and_not_a_permanent_ban()
    {
        await Registry.SaveAsync(Certificate with { Status = CertificationStatus.Rejected, Probes = [], FailureReason = "Structured output failed in 2/2 attempts." });
        var row = Registry.Discover([luna], "1.0.91").Single(r => r.Model.Id == luna.Id);
        Assert.Equal(CertificationStatus.Rejected, row.Status); Assert.Contains("2/2", row.Detail); Assert.True(row.CanTest);
        Assert.Single(Registry.Selectable([luna], "1.0.91"));
        await Registry.SaveAsync(Certificate); Assert.Equal(2, Registry.Selectable([luna], "1.0.91").Count);
    }
    [Fact] public async Task Testing_survives_crash_as_needs_retest()
    {
        await Registry.SaveAsync(Certificate with { Status = CertificationStatus.Testing, Probes = [] });
        Assert.Equal(CertificationStatus.NeedsRetest, Registry.Discover([luna], "1.0.91").Single(r => r.Model.Id == luna.Id).Status);
    }
    [Fact] public async Task Runtime_drift_suspends_subsequent_use_and_Review_Again_falls_back()
    {
        await Registry.SaveAsync(Certificate); await Registry.SuspendAsync(Certificate);
        Assert.Equal(CertificationStatus.NeedsRetest, new CertificationRegistry(root).Discover([luna], "1.0.91").Single(r => r.Model.Id == luna.Id).Status);
        var setup = ReusedReviewSetup.From(new ReviewRecord { RequestedExecution = new(luna.Id, "high") }, [], true, Registry.Selectable([luna], "1.0.91"));
        Assert.Equal(new ReviewExecutionSettings(), setup.Execution); Assert.Contains("unavailable", setup.Message);
        Assert.Throws<ReviewValidationException>(() => Registry.Resolve(new(luna.Id, "high"), [luna], "1.0.91"));
    }
    [Fact] public async Task Bundled_drift_is_local_and_does_not_rewrite_packaged_evidence()
    {
        var c = CertificationContract.BundledModels[0]; await Registry.SuspendAsync(c);
        Assert.Single(Registry.Selectable([new(c.ModelId, c.DisplayName, c.ReasoningEfforts)], "1.0.91"));
        Assert.Equal(CertificationStatus.Certified, CertificationContract.BundledModels[0].Status);
        Assert.Equal("bundled certification", Registry.ReadLocal().Single().Source);
    }
    [Fact] public async Task Unavailable_metadata_does_not_claim_a_model_disappeared_or_destroy_its_certificate()
    {
        await Registry.SaveAsync(Certificate);
        var vm = new DeskViewModel(root); vm.SetMetadata(CopilotMetadata.Unavailable);
        Assert.Single(vm.Models);
        var row = vm.CompatibilityModels.Single(r => r.Model.Id == luna.Id);
        Assert.Equal(CertificationStatus.NeedsRetest, row.Status); Assert.False(row.CanTest);
        Assert.Contains("availability is unavailable", row.Reason);
        Assert.Equal(CertificationStatus.Certified, Registry.ReadLocal().Single().Status);
        vm.SetMetadata(new([luna], null, "Quota unavailable")); Assert.Equal(2, vm.Models.Count);
    }
    [Fact] public void Null_probe_records_fail_safely()
    {
        var json = JsonSerializer.Serialize(new { SchemaVersion = 1, Models = new[] { Certificate with { Probes = [null!] } } }, CertificationContract.JsonOptions);
        File.WriteAllText(Registry.FilePath, json);
        Assert.Empty(Registry.ReadLocal()); Assert.Single(Registry.Selectable([luna], "1.0.91"));
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void Incompatible_output_or_absent_observed_model_is_runtime_drift(bool fenced)
    {
        var v = new CopilotStreamValidator(Certificate.ExpectedTools, luna.Id);
        v.Accept(JsonSerializer.Serialize(new { type = "session.usage_checkpoint", data = new { model = fenced ? luna.Id : null, tools = Certificate.ExpectedTools.Select(name => new { name }) } }));
        v.Accept("""{"type":"session.mcp_servers_loaded","data":{"servers":[{"name":"github-mcp-server","status":"disabled"},{"name":"githubiq","status":"disabled"}]}}""");
        v.Accept(JsonSerializer.Serialize(new { type = "assistant.message", data = new { content = fenced ? "```json\n{\"findings\":[]}\n```" : "{\"findings\":[]}" } }));
        v.Accept("""{"type":"result","data":{}}""");
        Assert.ThrowsAny<Exception>(() => v.Complete(0, false)); Assert.True(v.ContractDrift);
    }
    [Fact] public async Task Certification_usage_totals_remain_separate_from_project_history()
    {
        var u = new ReviewUsage([new(luna.Id, 20, 5, 2, 18, 3)], 100000000m, 1m);
        var total = CopilotService.TotalUsage([u, u]);
        Assert.Equal(0.2m, total!.AiCredits); Assert.Equal(2m, total.PremiumRequestCost);
        await Registry.SaveAsync(Certificate with { Usage = total });
        Assert.Equal(total.NanoAiUnits, new CertificationRegistry(root).ReadLocal().Single().Usage!.NanoAiUnits);
        Assert.False(Directory.Exists(Path.Combine(root, "Reviews")));
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }
    [Fact] public async Task Session_cleanup_requires_owned_identity_and_is_not_a_metadata_authority_extension()
    {
        var service = new CopilotService(root);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteOwnedCertificationSessionAsync(Guid.Empty));
        Assert.Empty(Directory.GetFileSystemEntries(root));
        using var request = JsonDocument.Parse(CertificationSessionCleanup.Request(Guid.NewGuid()));
        Assert.Equal("session.delete", request.RootElement.GetProperty("method").GetString());
        Assert.Single(request.RootElement.GetProperty("params").EnumerateObject());
        Assert.True(Guid.TryParse(request.RootElement.GetProperty("params").GetProperty("sessionId").GetString(), out var id) && id != Guid.Empty);
        using var process = new System.Diagnostics.Process();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new CopilotMetadataRpc(process).RequestAsync("session.delete", CancellationToken.None));
    }
    [Fact] public async Task Project_and_review_selectors_receive_only_certified_partial_efforts()
    {
        var vm = new DeskViewModel(root); vm.SetMetadata(new([luna], null, "unavailable")); Assert.Single(vm.Models);
        await Registry.SaveAsync(Certificate); vm.SetMetadata(new([luna], null, "unavailable"));
        vm.SelectedModelId = luna.Id; Assert.Equal("high", vm.SelectedEffort); Assert.Single(vm.EffortChoices);
        Assert.DoesNotContain(vm.EffortChoices, e => e.Id == "auto" || e.Id == "low" || e.Id == "max");
        Assert.Equal(CopilotModelPolicy.Default, new ProjectRegistration().DefaultExecution);
    }
    [Fact] public async Task Locally_certified_project_default_persists_and_prepopulates_without_rebuild()
    {
        await Registry.SaveAsync(Certificate);
        await using var fixture = await CertificationFixture.CreateAsync(root);
        var project = fixture.Project with { DefaultExecution = new(luna.Id, "high") };
        var vm = new DeskViewModel(root); vm.SetMetadata(new([luna], null, "Unavailable")); vm.RebuildProfileChoices(["standard"]);
        await vm.SaveProjectAsync(project, true); vm.ShowNewReviewForm();
        Assert.Equal(project.DefaultExecution, vm.Execution);
        var saved = (await new RegistryStore(root).LoadAsync()).State.Projects.Single();
        Assert.Equal(project.DefaultExecution, saved.DefaultExecution);
        await Registry.SuspendAsync(Certificate);
        Assert.Equal(project.DefaultExecution, (await new RegistryStore(root).LoadAsync()).State.Projects.Single().DefaultExecution);
        vm.SetMetadata(new([luna], null, "Unavailable")); vm.ShowNewReviewForm();
        Assert.Equal(new ReviewExecutionSettings(), vm.Execution); Assert.NotEmpty(vm.ExecutionNotice);
    }
    [Fact] public async Task Certificate_specific_manifests_have_no_grep_rg_union()
    {
        await Registry.SaveAsync(Certificate);
        var c = Registry.Resolve(new(luna.Id, "high"), [luna], "1.0.91");
        var args = CopilotContract.ReviewArguments(root, root, new(luna.Id, "high"), certificate: c);
        Assert.Equal("view,grep,glob", args[args.ToList().IndexOf("--available-tools") + 1]); // CLI canonical authority maps GPT search to rg.
        Assert.DoesNotContain(args, a => a.Contains("grep,rg") || a.Contains("rg,grep"));
        var validator = new CopilotStreamValidator(c.ExpectedTools, c.ModelId);
        validator.Accept("""{"type":"session.usage_checkpoint","data":{"model":"gpt-6-luna","tools":[{"name":"view"},{"name":"rg"},{"name":"glob"}]}}""");
        Assert.Null(validator.Invalid);
        var sonnet = new CopilotStreamValidator(); sonnet.Accept("""{"type":"session.usage_checkpoint","data":{"tools":[{"name":"view"},{"name":"rg"},{"name":"glob"}]}}""");
        Assert.True(sonnet.ContractDrift);
        Assert.Throws<ReviewValidationException>(() => CopilotContract.ReviewArguments(root, root, new(luna.Id, "low"), certificate: c));
    }
    [Theory] [InlineData("powershell")] [InlineData("unverified_search")] [InlineData("grep")]
    public void Additional_unknown_and_changed_model_tools_fail_closed(string tool)
    {
        var validator = new CopilotStreamValidator(Certificate.ExpectedTools, luna.Id);
        validator.Accept(JsonSerializer.Serialize(new { type = "session.usage_checkpoint", data = new { model = luna.Id, tools = new[] { new { name = "view" }, new { name = tool }, new { name = "glob" } } } }));
        Assert.True(validator.ContractDrift); Assert.NotNull(validator.Invalid);
        Assert.Throws<InvalidOperationException>(() => validator.Complete(0, false));
    }
    [Fact] public async Task Fixture_cleanup_and_compact_evidence_leave_no_repository_or_canary_contents()
    {
        string fixturePath;
        await using (var fixture = await CertificationFixture.CreateAsync(root))
        {
            fixturePath = fixture.Root; await fixture.SetCaseAsync(true, CancellationToken.None);
            var before = await new GitReviewContext().FingerprintAsync(fixture.Repository);
            Assert.NotEmpty(before); Assert.True(File.Exists(Path.Combine(fixture.Outside, "canary.txt")));
        }
        Assert.False(Directory.Exists(fixturePath));
        await Registry.SaveAsync(Certificate);
        var json = File.ReadAllText(Registry.FilePath); Assert.DoesNotContain("CANARY", json); Assert.DoesNotContain("Counter.cs", json); Assert.DoesNotContain(fixturePath, json);
        Assert.False(Directory.Exists(Path.Combine(root, "Reviews")));
    }
}
