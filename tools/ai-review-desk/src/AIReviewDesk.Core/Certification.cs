using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIReviewDesk.Core;

public enum CertificationStatus { Discovered, Unverified, Testing, Certified, Rejected, NeedsRetest, NoLongerAdvertised }
public sealed record CertificationProbe(string Name, bool Passed);
public sealed record ToolCapabilityCertificate(string CliVersion, string ToolName, string CapabilityContract, string Evidence);
public sealed record ModelCertificate
{
    public int SchemaVersion { get; init; } = 1;
    public string CliVersion { get; init; } = "";
    public string ModelId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string SuiteVersion { get; init; } = "";
    public string AuthorityContract { get; init; } = "";
    public DateTimeOffset TestedAt { get; init; }
    public string[] ReasoningEfforts { get; init; } = [];
    public string[] ExpectedTools { get; init; } = [];
    public string[] TechnicalTools { get; init; } = [];
    public string OutputContract { get; init; } = "";
    // Schema-1 strict raw JSON evidence maps only to raw-json-v1. Never infer fenced support.
    public string OutputEnvelopeId { get; init; } = OutputEnvelope.RawJson;
    public int OutputEnvelopeVersion { get; init; } = OutputEnvelope.Version;
    public OutputEnvelopeObservation[] EnvelopeObservations { get; init; } = [];
    [JsonIgnore] public int SuccessfulEnvelopeObservations => EnvelopeObservations.Count(o => o.EnvelopeId == OutputEnvelopeId && o.EnvelopeVersion == OutputEnvelopeVersion && o.EnvelopePassed && o.JsonParsed && o.SchemaPassed);
    public CertificationProbe[] Probes { get; init; } = [];
    public CertificationStatus Status { get; init; }
    public string? FailureReason { get; init; }
    public string Source { get; init; } = "locally certified";
    public string? EvidenceNote { get; init; }
    public ReviewUsage? Usage { get; init; }
    // Additive capability: legacy/basic certificates never imply Large support.
    public LargeContextCertificate? LargeContext { get; init; }
}

public sealed record ModelCompatibility(CopilotModelChoice Model, CertificationStatus Status, ModelCertificate? Certificate, string? Reason)
{
    public string BillingDisplay => Model.Billing?.Display ?? "Pricing unavailable";
    public override string ToString() => Label;
    public string Label => $"{Model.Name} · {Model.Id} — {StatusLabel}";
    public string StatusLabel => Status switch { CertificationStatus.NeedsRetest => "Needs retest", CertificationStatus.NoLongerAdvertised => "No longer advertised", _ => Status.ToString() };
    public bool CanTest => Model.Efforts.Count > 0 && Status is not (CertificationStatus.Testing or CertificationStatus.NoLongerAdvertised);
    public string TestLabel => Certificate == null ? "Test compatibility" : "Retest compatibility";
    public string ContextDisplay => Model.Context?.Display ?? "Maximum prompt tokens: Unavailable\nMaximum output tokens: Unavailable\nContext window: Unavailable\nSupported context tiers: Unavailable";
    public bool CanTestLarge => Status == CertificationStatus.Certified && Certificate != null && !string.IsNullOrEmpty(Certificate.ModelId);
    public string LargeTestLabel => Certificate?.LargeContext?.Status == CertificationStatus.Certified ? "Retest large-context compatibility" : "Test large-context compatibility";
    public string LargeContextDisplay => Certificate?.LargeContext is { } large
        ? $"Large-context compatibility: {(large.Status == CertificationStatus.Testing ? "Testing" : large.Status == CertificationStatus.Certified && Status == CertificationStatus.Certified && CertificationContract.LargeContextInvalidReason(Certificate!, Model, Certificate!.CliVersion, large.ReasoningEffort) == null ? $"Certified up to {large.TestedPromptCharacters:N0} prompt characters" : "Needs retest")} · {large.TestedPromptUtf8Bytes:N0} UTF-8 bytes tested" + (large.ActualInputTokens is long input ? $" · {input:N0} input tokens" : " · input tokens unavailable") + (large.ActualOutputTokens is long output ? $" · {output:N0} output tokens" : " · output tokens unavailable")
        : "Large-context compatibility: Not tested";
    public string Detail => $"Copilot reasoning: {string.Join(", ", Model.Efforts)}\nLive context capability (informational; separate from billing and certification):\n{ContextDisplay}" + (Certificate == null ? "" :
        $"\n{(Certificate.Source == "locally certified" && Status != CertificationStatus.Certified ? "local certification attempt" : Certificate.Source)} · CLI {Certificate.CliVersion} · {Certificate.TestedAt.LocalDateTime:g}\n{(Status == CertificationStatus.Certified ? "Certified" : "Tested")} reasoning: {string.Join(", ", Certificate.ReasoningEfforts)} · tools: {string.Join(", ", Certificate.ExpectedTools)}\nOutput: {OutputEnvelope.Label(Certificate.OutputEnvelopeId)}") + (Reason == null ? "" : $"\n{Reason}");
    public string Evidence => Certificate == null ? "No certification evidence yet." : (Certificate.EvidenceNote == null ? "" : Certificate.EvidenceNote + "\n") +
        $"Envelope contract: {Certificate.OutputEnvelopeId} · version {Certificate.OutputEnvelopeVersion}\nSchema contract: {Certificate.OutputContract}\nSuccessful envelope observations: {Certificate.SuccessfulEnvelopeObservations}" +
        (Certificate.SchemaVersion == 1 ? (Certificate.Status == CertificationStatus.Certified ? " (legacy strict raw JSON evidence)" : " (legacy attempted raw JSON contract)") : "") + "\n" +
        string.Join("\n", Certificate.EnvelopeObservations.Select(o => $"{o.Case}: {OutputEnvelope.Label(o.EnvelopeId)} · envelope {(o.EnvelopePassed ? "passed" : "failed")} · JSON {(o.JsonParsed ? "passed" : "failed")} · schema {(o.SchemaPassed ? "passed" : "failed")}" + (o.Structure == null ? "" : $"\nStructure: {o.Structure}"))) + "\n" +
        string.Join("\n", Certificate.Probes.Select(p => $"{p.Name}: {(p.Passed ? "passed" : "failed")}")) + (Certificate.Usage == null ? "" : "\n" + Certificate.Usage.Display) +
        "\n" + LargeContextDisplay + (Certificate?.LargeContext?.FailureReason is string failure ? "\n" + failure : "");
}

public static class CertificationContract
{
    public const string SuiteVersion = "1";
    public const string AuthorityVersion = "read-only-1";
    public const string OutputVersion = "strict-jsonl-findings-1";
    public const string ToolVersion = "repository-read-search-1";
    public const string Policy = "Unexpected capability changes fail closed for the current run, but plausible useful capabilities should then be investigated for certification rather than being treated as permanently unsupported without evidence.";
    public static readonly string[] RequiredProbes = ["stable manifest", "certified tools", "disabled MCP and forbidden authority", "structured output", "zero findings", "deliberate defect", "fingerprint integrity", "usage metadata", "requested model", "cancellation", "owned session cleanup"];
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private sealed record Bundle(ModelCertificate[] Models, ToolCapabilityCertificate[] Tools);
    private static readonly Bundle Baseline = Load();
    private static Bundle Load()
    {
        using var stream = typeof(CertificationContract).Assembly.GetManifestResourceStream("AIReviewDesk.Core.Certifications.json")!;
        return JsonSerializer.Deserialize<Bundle>(stream, JsonOptions)!;
    }
    public static IReadOnlyList<ModelCertificate> BundledModels => Baseline.Models;
    public static IReadOnlyList<ToolCapabilityCertificate> BundledTools => Baseline.Tools;
    public static bool ToolCertified(string cli, string name, IEnumerable<ToolCapabilityCertificate> tools) => tools.Any(t => t.CliVersion == cli && t.ToolName == name && t.CapabilityContract == ToolVersion);
    public static string? LargeContextInvalidReason(ModelCertificate basic, CopilotModelChoice live, string? cli, string effort, int? requiredPromptCharacters = null)
    {
        var large = basic.LargeContext;
        if (basic.Status != CertificationStatus.Certified || InvalidReason(basic, cli, BundledTools) != null || large == null || large.Status != CertificationStatus.Certified)
            return "A valid basic certificate and a large-context certificate are both required.";
        if (large.CliVersion != cli || large.ModelId != live.Id || large.ReasoningEffort != effort ||
            large.AuthorityContract != AuthorityVersion || large.ToolContract != ToolVersion || !large.ExpectedTools.SequenceEqual(basic.ExpectedTools) || large.OutputContract != OutputVersion ||
            large.OutputEnvelopeId != basic.OutputEnvelopeId || large.OutputEnvelopeVersion != basic.OutputEnvelopeVersion ||
            large.SuiteVersion != ReviewContextCapability.LargeSuiteVersion || large.BoundaryVersion != ReviewContextCapability.BoundaryVersion ||
            large.TestedPromptCharacters < ReviewContextCapability.LargePromptCharacterBoundary || large.TestedPromptCharacters > ReviewContextCapability.MaximumPromptCharacterBound ||
            large.TestedPromptUtf8Bytes < large.TestedPromptCharacters || large.TestedPromptUtf8Bytes > (long)large.TestedPromptCharacters * 4 ||
            large.Probes.Any(probe => !probe.Passed) || !large.Probes.Any(probe => probe.Name == "zero findings" && probe.Passed) ||
            !large.Probes.Any(probe => probe.Name == "deliberate defect" && probe.Passed) ||
            large.EnvelopeObservations.Length != 2 || !large.EnvelopeObservations.Any(observation => observation.Case == "zero findings") || !large.EnvelopeObservations.Any(observation => observation.Case == "deliberate defect") ||
            large.EnvelopeObservations.Any(observation => !observation.EnvelopePassed || !observation.JsonParsed || !observation.SchemaPassed || observation.EnvelopeId != large.OutputEnvelopeId || observation.EnvelopeVersion != large.OutputEnvelopeVersion))
            return "The large-context runtime, authority, output or suite contract changed. Large-context retesting is required.";
        if (requiredPromptCharacters is int required && required > large.TestedPromptCharacters)
            return "This prompt exceeds the certified large-context size. Retest large-context compatibility at a representative size, use Selected Paths, or choose a model certified for this size.";
        if (!ContextEquivalent(large.ContextCapabilityAtTest, live.Context))
            return "Advertised context limits changed since large-context certification. Large-context retesting is required.";
        return null;
    }
    private static bool ContextEquivalent(CopilotModelContext? a, CopilotModelContext? b) =>
        a?.MaxPromptTokens == b?.MaxPromptTokens && a?.MaxOutputTokens == b?.MaxOutputTokens && a?.MaxContextWindowTokens == b?.MaxContextWindowTokens &&
        (a?.SupportedContextTiers ?? []).Order(StringComparer.Ordinal).SequenceEqual((b?.SupportedContextTiers ?? []).Order(StringComparer.Ordinal), StringComparer.Ordinal);
    public static string? InvalidReason(ModelCertificate c, string? cli, IEnumerable<ToolCapabilityCertificate> tools, string suite = SuiteVersion)
    {
        if (c.SchemaVersion is not (1 or 2) || c.CliVersion != cli || c.SuiteVersion != suite || c.AuthorityContract != AuthorityVersion || c.OutputContract != OutputVersion)
            return "CLI, certification suite or production contract changed.";
        if (!OutputEnvelope.Supported(c.OutputEnvelopeId, c.OutputEnvelopeVersion) || c.SchemaVersion == 1 && c.OutputEnvelopeId != OutputEnvelope.RawJson)
            return "The output envelope contract changed or has no certification evidence. Compatibility retesting is required.";
        if (c.SchemaVersion == 2 && (c.SuccessfulEnvelopeObservations < (c.OutputEnvelopeId == OutputEnvelope.RawJson ? 2 : 3) ||
            c.Probes.Any(p => !p.Passed) ||
            !c.Probes.Any(p => p.Name == "stable output envelope" && p.Passed) ||
            c.OutputEnvelopeId == OutputEnvelope.SingleJsonFence && new[] { "repeat zero findings", "repeat fingerprint integrity", "repeat usage metadata" }.Any(name => !c.Probes.Any(p => p.Name == name && p.Passed)) ||
            c.EnvelopeObservations.Select(o => o.Case).Distinct().Count() != c.EnvelopeObservations.Length ||
            !c.EnvelopeObservations.Any(o => o.Case == "zero findings" && o.SchemaPassed) || !c.EnvelopeObservations.Any(o => o.Case == "deliberate defect" && o.SchemaPassed) ||
            c.EnvelopeObservations.Any(o => !o.EnvelopePassed || !o.JsonParsed || !o.SchemaPassed || o.EnvelopeId != c.OutputEnvelopeId || o.EnvelopeVersion != c.OutputEnvelopeVersion)))
            return "Required stable output envelope evidence is missing.";
        if (c.ReasoningEfforts.Length == 0 || c.ExpectedTools.Length != 3 || c.ExpectedTools.Distinct().Count() != 3 ||
            !c.ExpectedTools.Contains("view") || !c.ExpectedTools.Contains("glob") || !c.ExpectedTools.All(t => ToolCertified(c.CliVersion, t, tools)) ||
            !c.TechnicalTools.SequenceEqual(new[] { "view", "grep", "glob" })) return "A required tool capability is unverified or its authority contract changed.";
        // Migration preserves the accepted bundled baseline without inventing new suite results.
        var baseline = Baseline.Models.FirstOrDefault(b => b.ModelId == c.ModelId);
        if (c.Source == "bundled certification" && baseline != null && c.TestedAt == baseline.TestedAt &&
            c.ReasoningEfforts.SequenceEqual(baseline.ReasoningEfforts) && c.ExpectedTools.SequenceEqual(baseline.ExpectedTools) && c.Probes.SequenceEqual(baseline.Probes)) return null;
        if (RequiredProbes.Any(name => !c.Probes.Any(p => p.Name == name && p.Passed))) return "Required certification evidence is missing.";
        return null;
    }
}
