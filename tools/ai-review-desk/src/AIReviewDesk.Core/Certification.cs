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
}

public sealed record ModelCompatibility(CopilotModelChoice Model, CertificationStatus Status, ModelCertificate? Certificate, string? Reason)
{
    public override string ToString() => Label;
    public string Label => $"{Model.Name} · {Model.Id} — {StatusLabel}";
    public string StatusLabel => Status switch { CertificationStatus.NeedsRetest => "Needs retest", CertificationStatus.NoLongerAdvertised => "No longer advertised", _ => Status.ToString() };
    public bool CanTest => Model.Efforts.Count > 0 && Status is not (CertificationStatus.Testing or CertificationStatus.NoLongerAdvertised);
    public string TestLabel => Certificate == null ? "Test compatibility" : "Retest compatibility";
    public string Detail => $"Copilot reasoning: {string.Join(", ", Model.Efforts)}" + (Certificate == null ? "" :
        $"\n{(Certificate.Source == "locally certified" && Status != CertificationStatus.Certified ? "local certification attempt" : Certificate.Source)} · CLI {Certificate.CliVersion} · {Certificate.TestedAt.LocalDateTime:g}\n{(Status == CertificationStatus.Certified ? "Certified" : "Tested")} reasoning: {string.Join(", ", Certificate.ReasoningEfforts)} · tools: {string.Join(", ", Certificate.ExpectedTools)}\nOutput: {OutputEnvelope.Label(Certificate.OutputEnvelopeId)}") + (Reason == null ? "" : $"\n{Reason}");
    public string Evidence => Certificate == null ? "No certification evidence yet." : (Certificate.EvidenceNote == null ? "" : Certificate.EvidenceNote + "\n") +
        $"Envelope contract: {Certificate.OutputEnvelopeId} · version {Certificate.OutputEnvelopeVersion}\nSchema contract: {Certificate.OutputContract}\nSuccessful envelope observations: {Certificate.SuccessfulEnvelopeObservations}" +
        (Certificate.SchemaVersion == 1 ? (Certificate.Status == CertificationStatus.Certified ? " (legacy strict raw JSON evidence)" : " (legacy attempted raw JSON contract)") : "") + "\n" +
        string.Join("\n", Certificate.EnvelopeObservations.Select(o => $"{o.Case}: {OutputEnvelope.Label(o.EnvelopeId)} · envelope {(o.EnvelopePassed ? "passed" : "failed")} · JSON {(o.JsonParsed ? "passed" : "failed")} · schema {(o.SchemaPassed ? "passed" : "failed")}")) + "\n" +
        string.Join("\n", Certificate.Probes.Select(p => $"{p.Name}: {(p.Passed ? "passed" : "failed")}")) + (Certificate.Usage == null ? "" : "\n" + Certificate.Usage.Display);
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
