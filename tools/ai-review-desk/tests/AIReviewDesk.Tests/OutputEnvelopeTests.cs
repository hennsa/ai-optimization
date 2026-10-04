using System.Text.Json;
using AIReviewDesk.Core;
using AIReviewDesk.Infrastructure;
using AIReviewDesk.App;

namespace AIReviewDesk.Tests;

public sealed class OutputEnvelopeTests : IDisposable
{
    private const string Json = "{\"findings\":[],\"summary\":\"ok\",\"limitations\":[]}";
    private readonly string root = Path.Combine(Path.GetTempPath(), "ARD-Envelopes-" + Guid.NewGuid().ToString("N"));
    private static string Fence(string json = Json) => "```json\n" + json + "\n```";
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private static readonly string[] Tools = ["view", "grep", "glob"];
    private static readonly CopilotModelChoice Model = new("claude-haiku-4.5", "Claude Haiku 4.5", ["auto"]);
    private CertificationRegistry Registry => new(root);
    private static ModelCertificate Certificate => new()
    {
        SchemaVersion = 2, ModelId = Model.Id, DisplayName = Model.Name, CliVersion = "1.0.91", SuiteVersion = CertificationContract.SuiteVersion,
        AuthorityContract = CertificationContract.AuthorityVersion, OutputContract = CertificationContract.OutputVersion,
        TestedAt = DateTimeOffset.UtcNow, ReasoningEfforts = ["auto"], ExpectedTools = Tools, TechnicalTools = Tools,
        Probes = [.. CertificationContract.RequiredProbes.Select(p => new CertificationProbe(p, true)), new("stable output envelope", true),
            new("repeat zero findings", true), new("repeat fingerprint integrity", true), new("repeat usage metadata", true)], Status = CertificationStatus.Certified,
        OutputEnvelopeId = OutputEnvelope.SingleJsonFence,
        EnvelopeObservations = new[] { "zero findings", "deliberate defect", "repeat zero findings" }.Select(p => new OutputEnvelopeObservation(p, OutputEnvelope.SingleJsonFence, 1, true, true, true)).ToArray()
    };
    private static CopilotStreamValidator Stream(string text, string envelope = OutputEnvelope.RawJson, bool discovery = false, string tool = "grep", bool terminal = true)
    {
        var v = new CopilotStreamValidator(Tools, Model.Id, envelope, discovery);
        v.Accept(JsonSerializer.Serialize(new { type = "session.usage_checkpoint", data = new { model = Model.Id, tools = new[] { "view", tool, "glob" }.Select(name => new { name }) } }));
        v.Accept("""{"type":"session.mcp_servers_loaded","data":{"servers":[{"name":"github-mcp-server","status":"disabled"},{"name":"githubiq","status":"disabled"}]}}""");
        v.Accept(JsonSerializer.Serialize(new { type = "assistant.message", data = new { content = text } }));
        if (terminal) v.Accept("""{"type":"result","data":{}}""");
        return v;
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void Canonical_raw_and_exact_fence_pass_each_stage(bool fenced)
    {
        var v = Stream(fenced ? Fence() : Json, fenced ? OutputEnvelope.SingleJsonFence : OutputEnvelope.RawJson);
        Assert.Empty(v.Complete(0, false).Findings); Assert.True(v.EnvelopePassed && v.JsonParsed && v.SchemaPassed); Assert.False(v.ContractDrift);
    }
    [Theory] [InlineData("\n")] [InlineData("\r\n")]
    public void Fence_normalizes_only_line_endings_and_outer_JSON_whitespace(string newline)
    {
        var text = " \t\r\n" + Fence("{\n\"findings\":[],\n\"summary\":\"backticks ``` remain data\"\n}").Replace("\n", newline) + "\r\n\t ";
        Assert.Equal("{\n\"findings\":[],\n\"summary\":\"backticks ``` remain data\"\n}", OutputEnvelope.Extract(text, OutputEnvelope.SingleJsonFence));
        Assert.Empty(Stream(text, OutputEnvelope.SingleJsonFence).Complete(0, false).Findings);
        Assert.Equal(" \t" + Json + "\r\n", OutputEnvelope.Extract(" \t" + Json + "\r\n", OutputEnvelope.RawJson));
    }
    public static IEnumerable<object[]> InvalidFences()
    {
        foreach (var text in new[] { "prose\n" + Fence(), Fence() + "\nprose", Fence() + "\n" + Fence(), "```json\n```json\n" + Json + "\n```\n```",
            "```javascript\n" + Json + "\n```", "```\n" + Json + "\n```", "```JSON\n" + Json + "\n```", "```json\n" + Json + "\n``",
            "````json\n" + Json + "\n```", Fence() + "`", "```json \n" + Json + "\n```", "```json\n" + Json + "\n ```",
            "unrelated " + Json + " unrelated", "```json\n" + Json + "\n```\n<script>alert(1)</script>", "\u00a0" + Fence(), "```json\n" + Json + "\n```json\n{}\n```", "```json\n```" }) yield return [text];
    }
    [Theory] [MemberData(nameof(InvalidFences))]
    public void Ambiguous_or_unsupported_presentation_never_extracts(string text)
    {
        Assert.False(OutputEnvelope.TryExtractFence(text, out _));
        var v = Stream(text, OutputEnvelope.SingleJsonFence); Assert.Throws<InvalidOperationException>(() => v.Complete(0, false));
        Assert.True(v.ContractDrift); Assert.False(v.JsonParsed || v.SchemaPassed);
    }
    [Theory] [InlineData("")] [InlineData("{\"findings\":[}")] [InlineData("{\"findings\":[],}")] [InlineData("{'findings':[]}")]
    [InlineData("{/*comment*/\"findings\":[]}")] [InlineData("{\"findings\":[]//comment\n}")] [InlineData("{\"findings\":[]")]
    [InlineData("{\"findings\":[]} {\"findings\":[]}")]
    public void Neither_envelope_repairs_JSON(string json)
    {
        foreach (var id in new[] { OutputEnvelope.RawJson, OutputEnvelope.SingleJsonFence })
        {
            var v = Stream(id == OutputEnvelope.RawJson ? json : Fence(json), id);
            Assert.Throws<InvalidOperationException>(() => v.Complete(0, false)); Assert.False(v.JsonParsed || v.SchemaPassed); Assert.True(v.ContractDrift);
            Assert.Equal(json, OutputEnvelope.Extract(id == OutputEnvelope.RawJson ? json : Fence(json), id));
        }
    }
    [Theory] [InlineData("{}")] [InlineData("{\"findings\":null}")] [InlineData("{\"findings\":[],\"extra\":true}")]
    [InlineData("{\"findings\":[],\"findings\":[]}")] [InlineData("{\"findings\":[{\"id\":\"1\",\"severity\":\"HIGH\"}]}")]
    [InlineData("{\"findings\":[],\"limitations\":[7]}")]
    public void Schema_validation_is_identical_after_either_envelope(string json)
    {
        foreach (var id in new[] { OutputEnvelope.RawJson, OutputEnvelope.SingleJsonFence })
        {
            var v = Stream(id == OutputEnvelope.RawJson ? json : Fence(json), id);
            var error = Assert.Throws<InvalidOperationException>(() => v.Complete(0, false));
            Assert.True(v.JsonParsed); Assert.False(v.SchemaPassed); Assert.Equal("The JSON payload did not satisfy the findings schema.", error.Message);
        }
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void Discovery_classifies_only_during_compatibility_and_requires_authority(bool fenced)
    {
        var text = fenced ? Fence() : Json;
        var v = Stream(text, discovery: true); v.Complete(0, false);
        Assert.Equal(fenced ? OutputEnvelope.SingleJsonFence : OutputEnvelope.RawJson, v.ObservedEnvelope);
        var unsafeTools = Stream(text, discovery: true, tool: "powershell");
        Assert.Throws<InvalidOperationException>(() => unsafeTools.Complete(0, false)); Assert.Null(unsafeTools.ObservedEnvelope);
        var incomplete = Stream(text, discovery: true, terminal: false);
        Assert.Throws<InvalidOperationException>(() => incomplete.Complete(0, false)); Assert.Null(incomplete.ObservedEnvelope);
    }
    [Fact] public void Discovery_does_not_strip_prose_or_hide_malformed_fenced_payloads()
    {
        var prose = Stream("prose\n" + Fence(), discovery: true); Assert.Throws<InvalidOperationException>(() => prose.Complete(0, false)); Assert.False(prose.JsonParsed);
        var broken = Stream(Fence("{bad}"), discovery: true); Assert.Throws<InvalidOperationException>(() => broken.Complete(0, false));
        Assert.Equal(OutputEnvelope.SingleJsonFence, broken.ObservedEnvelope); Assert.True(broken.EnvelopePassed); Assert.False(broken.JsonParsed);
    }
    [Theory] [InlineData("severity")] [InlineData("certainty")] [InlineData("file")] [InlineData("line")] [InlineData("fractional line")] [InlineData("evidence")] [InlineData("required property")]
    public void Envelopes_preserve_finding_enums_locations_evidence_and_required_properties(string violation)
    {
        var finding = new ReviewFinding { Id = "1", Severity = "high", Certainty = "confirmed", Category = "correctness", Title = "Defect", File = "Counter.cs", Line = 2, Evidence = "value - 1", Impact = "Decrements", Recommendation = "Increment" };
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(new { findings = new[] { finding } }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }))!;
        var f = node["findings"]![0]!.AsObject();
        switch (violation)
        {
            case "severity": f["severity"] = "HIGH"; break;
            case "certainty": f["certainty"] = "certain"; break;
            case "file": f["file"] = true; break;
            case "line": f["line"] = 0; break;
            case "fractional line": f["line"] = 1.5; break;
            case "evidence": f["evidence"] = ""; break;
            default: f.Remove("recommendation"); break;
        }
        var json = node.ToJsonString();
        foreach (var id in new[] { OutputEnvelope.RawJson, OutputEnvelope.SingleJsonFence })
        {
            var v = Stream(id == OutputEnvelope.RawJson ? json : Fence(json), id);
            Assert.Throws<InvalidOperationException>(() => v.Complete(0, false)); Assert.True(v.JsonParsed); Assert.False(v.SchemaPassed);
        }
    }
    [Fact] public async Task Newly_discovered_model_can_certify_a_known_fence_without_model_name_rules()
    {
        var future = new CopilotModelChoice("future-model", "Future model", ["auto"]);
        Assert.Single(Registry.Selectable([future], "1.0.91"));
        await Registry.SaveAsync(Certificate with { ModelId = future.Id, DisplayName = future.Name });
        Assert.Equal(OutputEnvelope.SingleJsonFence, Registry.Resolve(new(future.Id, "auto"), [future], "1.0.91").OutputEnvelopeId);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Presentation_drift_fails_closed_suspends_certificate_and_blocks_Review_Again(bool certifiedFence)
    {
        var c = certifiedFence ? Certificate : Certificate with { OutputEnvelopeId = OutputEnvelope.RawJson, EnvelopeObservations = Certificate.EnvelopeObservations.Select(o => o with { EnvelopeId = OutputEnvelope.RawJson }).ToArray() };
        await Registry.SaveAsync(c);
        var v = Stream(certifiedFence ? Json : Fence(), c.OutputEnvelopeId);
        Assert.Throws<InvalidOperationException>(() => v.Complete(0, false)); Assert.True(v.ContractDrift);
        await Registry.SuspendAsync(c);
        Assert.Equal(CertificationStatus.NeedsRetest, new CertificationRegistry(root).Discover([Model], "1.0.91").Single(r => r.Model.Id == Model.Id).Status);
        var setup = ReusedReviewSetup.From(new ReviewRecord { RequestedExecution = new(Model.Id, "auto") }, [], true, Registry.Selectable([Model], "1.0.91"));
        Assert.True(setup.Execution!.IsAutoModel); Assert.Contains("unavailable", setup.Message!);
    }
    [Fact] public async Task Haiku_certificate_materializes_immediately_and_after_restart_without_default_or_history_writes()
    {
        var vm = new DeskViewModel(root); vm.SetMetadata(new([Model], null, "")); Assert.Single(vm.Models);
        await Registry.SaveAsync(Certificate); vm.SetMetadata(new([Model], null, "")); Assert.Equal(2, vm.Models.Count);
        vm.SelectedModelId = Model.Id; Assert.Equal("auto", vm.SelectedEffort);
        var restored = new CertificationRegistry(root).Resolve(new(Model.Id, "auto"), [Model], "1.0.91");
        Assert.Equal(OutputEnvelope.SingleJsonFence, restored.OutputEnvelopeId); Assert.Equal(3, restored.SuccessfulEnvelopeObservations);
        Assert.Contains("Output: JSON code block", Registry.Discover([Model], "1.0.91").Single(r => r.Model.Id == Model.Id).Detail);
        Assert.False(File.Exists(Path.Combine(root, "app-state.json"))); Assert.False(Directory.Exists(Path.Combine(root, "Reviews")));
    }
    [Fact] public async Task Old_local_raw_certificates_migrate_but_cannot_infer_fenced_trust()
    {
        var legacy = Certificate with { SchemaVersion = 1, OutputEnvelopeId = OutputEnvelope.RawJson, EnvelopeObservations = [] };
        var json = JsonSerializer.Serialize(new { SchemaVersion = 1, Models = new[] { legacy } }, CertificationContract.JsonOptions);
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!; var model = node["Models"]![0]!.AsObject();
        model.Remove("OutputEnvelopeId"); model.Remove("OutputEnvelopeVersion"); model.Remove("EnvelopeObservations");
        Directory.CreateDirectory(root); File.WriteAllText(Registry.FilePath, node.ToJsonString());
        Assert.Equal(OutputEnvelope.RawJson, Registry.Resolve(new(Model.Id, "auto"), [Model], "1.0.91").OutputEnvelopeId);
        Assert.Null(CertificationContract.InvalidReason(legacy, "1.0.91", CertificationContract.BundledTools));
        Assert.NotNull(CertificationContract.InvalidReason(legacy with { OutputEnvelopeId = OutputEnvelope.SingleJsonFence }, "1.0.91", CertificationContract.BundledTools));
    }
    [Theory] [InlineData("version")] [InlineData("schema")] [InlineData("single")] [InlineData("mixed")] [InlineData("failed")] [InlineData("repeat probe")]
    public async Task Unsupported_contracts_and_insufficient_or_unstable_evidence_require_retest(string change)
    {
        var c = change switch { "version" => Certificate with { OutputEnvelopeVersion = 2 }, "schema" => Certificate with { OutputContract = "future" },
            "single" => Certificate with { EnvelopeObservations = [Certificate.EnvelopeObservations[0]] },
            "repeat probe" => Certificate with { Probes = Certificate.Probes.Where(p => p.Name != "repeat zero findings").ToArray() },
            "mixed" => Certificate with { EnvelopeObservations = [.. Certificate.EnvelopeObservations, new("zero findings", OutputEnvelope.RawJson, 1, true, true, true)] },
            _ => Certificate with { EnvelopeObservations = Certificate.EnvelopeObservations.Select(o => o with { JsonParsed = false }).ToArray() } };
        await Registry.SaveAsync(c); Assert.Single(Registry.Selectable([Model], "1.0.91"));
        Assert.Equal(CertificationStatus.NeedsRetest, Registry.Discover([Model], "1.0.91").Single(r => r.Model.Id == Model.Id).Status);
    }
    [Fact] public async Task Rejected_evidence_records_stages_and_remains_retryable_without_returned_content()
    {
        var v = Stream(Fence("{\"findings\":[\"SECRET_REPOSITORY_CONTENT\"]}"), discovery: true);
        var error = Assert.Throws<InvalidOperationException>(() => v.Complete(0, false));
        await Registry.SaveAsync(Certificate with { Status = CertificationStatus.Rejected, FailureReason = error.Message, EnvelopeObservations = [v.OutputObservation("zero findings")] });
        var stored = File.ReadAllText(Registry.FilePath); Assert.DoesNotContain("SECRET_REPOSITORY_CONTENT", stored);
        var row = Registry.Discover([Model], "1.0.91").Single(r => r.Model.Id == Model.Id); Assert.True(row.CanTest); Assert.Equal(CertificationStatus.Rejected, row.Status);
        Assert.True(row.Certificate!.EnvelopeObservations[0].JsonParsed); Assert.False(row.Certificate.EnvelopeObservations[0].SchemaPassed);
    }
    [Fact] public void Truncated_and_random_boundaries_are_total_and_do_not_interpret_Markdown()
    {
        var valid = Fence();
        for (var i = 0; i < valid.Length; i++) Assert.False(OutputEnvelope.TryExtractFence(valid[..i], out _));
        var random = new Random(41); const string alphabet = "`json\n\r\t{}[] abc";
        for (var i = 0; i < 1000; i++)
        {
            var text = new string(Enumerable.Range(0, random.Next(0, 100)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
            _ = OutputEnvelope.TryExtractFence(text, out _);
        }
        var data = "{\"findings\":[],\"summary\":\"[link](https://example.invalid) <script>code()</script> ```\"}";
        Assert.Equal(data, OutputEnvelope.Extract(Fence(data), OutputEnvelope.SingleJsonFence));
        Assert.Contains("<script>", Stream(Fence(data), OutputEnvelope.SingleJsonFence).Complete(0, false).Summary);
    }
    [Fact] public async Task Fenced_project_default_persists_and_falls_back_only_after_invalidation()
    {
        await Registry.SaveAsync(Certificate);
        await using var fixture = await CertificationFixture.CreateAsync(root);
        var project = fixture.Project with { DefaultExecution = new(Model.Id, "auto") };
        var vm = new DeskViewModel(root); vm.SetMetadata(new([Model], null, "")); vm.RebuildProfileChoices(["standard"]);
        await vm.SaveProjectAsync(project, true); vm.ShowNewReviewForm(); Assert.Equal(project.DefaultExecution, vm.Execution);
        await Registry.SuspendAsync(Certificate); vm.SetMetadata(new([Model], null, "")); vm.ShowNewReviewForm();
        Assert.True(vm.Execution.IsAutoModel); Assert.NotEmpty(vm.ExecutionNotice);
        Assert.Equal(project.DefaultExecution, (await new RegistryStore(root).LoadAsync()).State.Projects.Single().DefaultExecution);
    }
    [Fact] public async Task Recertified_Sonnet_restores_stored_default_without_rebuild_or_silent_default_change()
    {
        var sonnet = new CopilotModelChoice("claude-sonnet-5.5", "Claude Sonnet 5.5", ["high", "max"]);
        var c = Certificate with { ModelId = sonnet.Id, DisplayName = sonnet.Name, ReasoningEfforts = ["high"],
            OutputEnvelopeId = OutputEnvelope.RawJson,
            EnvelopeObservations = Certificate.EnvelopeObservations.Take(2).Select(o => o with { EnvelopeId = OutputEnvelope.RawJson }).ToArray() };
        await Registry.SuspendAsync(c);
        await using var fixture = await CertificationFixture.CreateAsync(root);
        var project = fixture.Project with { DefaultExecution = new(sonnet.Id, "high") };
        var vm = new DeskViewModel(root); vm.SetMetadata(new([sonnet], null, "")); vm.RebuildProfileChoices(["standard"]);
        await vm.SaveProjectAsync(project, true); vm.ShowNewReviewForm();
        Assert.True(vm.Execution.IsAutoModel); Assert.NotEmpty(vm.ExecutionNotice);
        await Registry.SaveAsync(c with { Status = CertificationStatus.Rejected }); vm.SetMetadata(new([sonnet], null, ""));
        Assert.DoesNotContain(vm.Models, m => m.Id == sonnet.Id);
        await Registry.SaveAsync(c); vm.SetMetadata(new([sonnet], null, "")); vm.ShowNewReviewForm();
        Assert.Equal(project.DefaultExecution, vm.Execution); Assert.Contains(sonnet.Name, vm.ExecutionDisplay);
        Assert.Equal(new[] { "high" }, vm.Models.Single(m => m.Id == sonnet.Id).Efforts);
        Assert.Equal(project.DefaultExecution, (await new RegistryStore(root).LoadAsync()).State.Projects.Single().DefaultExecution);
        Assert.False(Directory.Exists(Path.Combine(root, "Reviews")));
    }
    [Fact] public void Deterministic_boundary_mutations_never_find_a_JSON_fragment()
    {
        foreach (var prefix in new[] { "a", "`", "<html>", "{}", "//", "\u0000" })
        foreach (var text in new[] { prefix + Fence(), Fence() + prefix })
        {
            Assert.False(OutputEnvelope.TryExtractFence(text, out _));
            var v = Stream(text, discovery: true); Assert.Throws<InvalidOperationException>(() => v.Complete(0, false)); Assert.False(v.SchemaPassed);
        }
    }
}
