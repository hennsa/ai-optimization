using System.Text.Json;
using AIReviewDesk.Core;

namespace AIReviewDesk.Infrastructure;

public sealed partial class CopilotService
{
    public async Task<ModelCertificate> TestLargeContextCompatibilityAsync(string modelId, string effort = "auto", IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var registry = new CertificationRegistry(dataDirectory);
        var metadata = await GetMetadataAsync(ct);
        var account = await GetAccountAsync(ct);
        if (!account.Supported || account.ConfigurationBlocked || account.Version != CopilotContract.SupportedVersion)
            throw new ReviewValidationException("Copilot security preflight is unavailable or blocked.");
        var row = registry.Discover(metadata.Models, account.Version).SingleOrDefault(candidate => candidate.Model.Id == modelId);
        var basic = row?.Certificate;
        var live = metadata.Models.SingleOrDefault(candidate => candidate.Id == modelId);
        if (row?.Status != CertificationStatus.Certified || basic == null || live == null || !basic.ReasoningEfforts.Contains(effort) || !live.Efforts.Contains(effort))
            throw new ReviewValidationException("A valid basic certificate for this model and reasoning level is required before large-context testing.");

        var testing = new LargeContextCertificate
        {
            CliVersion = account.Version!, ModelId = modelId, ReasoningEffort = effort, AuthorityContract = CertificationContract.AuthorityVersion,
            ToolContract = CertificationContract.ToolVersion, ExpectedTools = basic.ExpectedTools,
            OutputContract = CertificationContract.OutputVersion, OutputEnvelopeId = basic.OutputEnvelopeId, OutputEnvelopeVersion = basic.OutputEnvelopeVersion,
            SuiteVersion = ReviewContextCapability.LargeSuiteVersion, BoundaryVersion = ReviewContextCapability.BoundaryVersion,
            ContextCapabilityAtTest = live.Context, Status = CertificationStatus.Testing, TestedAt = DateTimeOffset.UtcNow
        };
        basic = basic with { LargeContext = testing };
        await registry.SaveAsync(basic, ct);
        var probes = new List<CertificationProbe>(); var outputs = new List<OutputEnvelopeObservation>(); var usages = new List<ReviewUsage>();
        try
        {
            progress?.Report("Generating deterministic synthetic large-context fixture");
            await using var fixture = await CertificationFixture.CreateAsync(dataDirectory, ct);
            var size = await fixture.PrepareLargeContextAsync(ct: ct);
            testing = testing with { TestedPromptCharacters = size.CharacterCount, TestedPromptUtf8Bytes = size.Utf8ByteCount };
            if (size.ContextClass != ReviewContextClass.Large || size.CharacterCount != ReviewContextCapability.MaximumPromptCharacterBound)
                throw new ReviewValidationException("Synthetic prompt did not reach the exact Large v2 tier ceiling.");

            progress?.Report("Testing strict zero-findings output at large context");
            var clean = await CompatibilityCallAsync(fixture, new(modelId, effort), basic.ExpectedTools, false, ct, basic.OutputEnvelopeId, timeoutOverride: TimeSpan.FromMinutes(5));
            if (clean.Usage != null) usages.Add(clean.Usage);
            outputs.Add(clean.Output with { Case = "zero findings" });
            probes.Add(new("zero findings", clean.Failure == null && clean.Result?.Findings.Count == 0));
            probes.Add(new("zero findings envelope and schema", clean.Output.EnvelopePassed && clean.Output.JsonParsed && clean.Output.SchemaPassed));

            progress?.Report("Testing deliberate defect at large context");
            await fixture.SetCaseAsync(true, ct);
            var defect = await CompatibilityCallAsync(fixture, new(modelId, effort), basic.ExpectedTools, false, ct, basic.OutputEnvelopeId, timeoutOverride: TimeSpan.FromMinutes(5));
            if (defect.Usage != null) usages.Add(defect.Usage);
            outputs.Add(defect.Output with { Case = "deliberate defect" });
            probes.Add(new("deliberate defect", defect.Failure == null && defect.Result?.Findings.Any(f => f.File?.Replace('\\', '/').EndsWith("Counter.cs", StringComparison.Ordinal) == true && f.Line is >= 1 and <= 2 &&
                (f.Evidence.Contains("value - 1", StringComparison.Ordinal) || f.Evidence.Contains("decrement", StringComparison.OrdinalIgnoreCase) || f.Evidence.Contains("subtract", StringComparison.OrdinalIgnoreCase))) == true));
            probes.Add(new("deliberate defect envelope and schema", defect.Output.EnvelopePassed && defect.Output.JsonParsed && defect.Output.SchemaPassed));
            probes.Add(new("stable manifest", clean.Tools != null && defect.Tools != null && new HashSet<string>(basic.ExpectedTools).SetEquals(clean.Tools) && new HashSet<string>(basic.ExpectedTools).SetEquals(defect.Tools)));
            probes.Add(new("certified tools", basic.ExpectedTools.Length == 3 && basic.ExpectedTools.All(tool => CertificationContract.ToolCertified(account.Version!, tool, CertificationContract.BundledTools))));
            probes.Add(new("disabled MCP and forbidden authority", clean.Failure == null && defect.Failure == null));
            probes.Add(new("fingerprint integrity", clean.Integrity && defect.Integrity));
            probes.Add(new("requested model", clean.Model == modelId && defect.Model == modelId));
            probes.Add(new("owned session cleanup", clean.Cleanup && defect.Cleanup));
            probes.Add(new("large context boundary", size.ContextClass == ReviewContextClass.Large));
            if (probes.Any(probe => !probe.Passed)) throw new ReviewValidationException("A required large-context probe failed: " + string.Join(", ", probes.Where(probe => !probe.Passed).Select(probe => probe.Name)));

            var usage = TotalUsage(usages);
            long? SumTokens(Func<ModelTokenUsage, long?> selector) => usage?.Models.Any(model => selector(model) != null) == true
                ? usage.Models.Sum(model => selector(model) ?? 0) : null;
            testing = testing with
            {
                ActualInputTokens = SumTokens(model => model.InputTokens), ActualOutputTokens = SumTokens(model => model.OutputTokens),
                EnvelopeObservations = outputs.ToArray(), Probes = probes.ToArray(), Usage = usage, Status = CertificationStatus.Certified,
                FailureReason = null
            };
        }
        catch (OperationCanceledException)
        {
            testing = testing with { Status = CertificationStatus.NeedsRetest, FailureReason = "Large-context compatibility testing was cancelled. Partial evidence cannot authorize large reviews.", Probes = probes.ToArray(), EnvelopeObservations = outputs.ToArray(), Usage = TotalUsage(usages) };
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or JsonException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            testing = testing with { Status = CertificationStatus.Rejected, FailureReason = ex is ReviewValidationException ? ex.Message : "Large-context compatibility could not complete under the production security contract.", Probes = probes.ToArray(), EnvelopeObservations = outputs.ToArray(), Usage = TotalUsage(usages) };
        }
        var current = registry.ReadLocal().FirstOrDefault(certificate => certificate.ModelId == modelId) ?? basic;
        var saved = current with { LargeContext = testing };
        await registry.SaveAsync(saved, CancellationToken.None);
        progress?.Report(testing.Status == CertificationStatus.Certified ? "Large-context compatibility certified" : "Large-context compatibility not certified");
        return saved;
    }
}
