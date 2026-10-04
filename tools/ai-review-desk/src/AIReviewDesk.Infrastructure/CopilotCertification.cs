using AIReviewDesk.Core;
using System.Text.Json;

namespace AIReviewDesk.Infrastructure;

public sealed partial class CopilotService
{
    private sealed record CompatibilityRun(ReviewResult? Result, string[]? Tools, string? Model, ReviewUsage? Usage, bool Integrity, bool Cancelled, bool Cleanup, string? Failure, OutputEnvelopeObservation Output);
    public async Task<ModelCertificate> TestCompatibilityAsync(string modelId, string effort = "auto", IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var registry = new CertificationRegistry(dataDirectory);
        var previous = registry.ReadLocal().FirstOrDefault(c => c.ModelId == modelId);
        var certificate = new ModelCertificate { SchemaVersion = 2, ModelId = modelId, DisplayName = modelId, CliVersion = CopilotContract.SupportedVersion,
            SuiteVersion = CertificationContract.SuiteVersion, AuthorityContract = CertificationContract.AuthorityVersion,
            OutputContract = CertificationContract.OutputVersion, TestedAt = DateTimeOffset.UtcNow, Status = CertificationStatus.Testing,
            ReasoningEfforts = [effort], TechnicalTools = CopilotContract.AllowedTools };
        var probes = new List<CertificationProbe>(); var usages = new List<ReviewUsage>(); var outputs = new List<OutputEnvelopeObservation>();
        try
        {
            progress?.Report("Refreshing live discovery (no model call)");
            var account = await GetAccountAsync(ct);
            if (!account.Supported || account.ConfigurationBlocked) throw new ReviewValidationException("Copilot security preflight is unavailable or blocked.");
            var metadata = await GetMetadataAsync(ct);
            var live = metadata.Models.SingleOrDefault(m => m.Id == modelId && m.Id != "auto") ?? throw new ReviewValidationException("The model is no longer advertised by this account.");
            if (!live.Efforts.Contains(effort)) throw new ReviewValidationException("The requested reasoning level is not advertised.");
            certificate = certificate with { DisplayName = live.Name, CliVersion = account.Version! };
            await registry.SaveAsync(certificate, ct);
            await using var fixture = await CertificationFixture.CreateAsync(dataDirectory, ct);
            var expected = previous?.ExpectedTools.Length == 3 && previous.ExpectedTools.All(t => CertificationContract.ToolCertified(certificate.CliVersion, t, CertificationContract.BundledTools))
                ? previous.ExpectedTools : CopilotContract.AllowedTools;
            progress?.Report("Testing exact manifest and zero findings");
            await fixture.SetCaseAsync(false, ct);
            // Bounded suite: at most one manifest rediscovery, three completed presentation
            // observations for fences (two for raw), and one early cancellation. No output retries.
            var clean = await CompatibilityCallAsync(fixture, new(modelId, effort), expected, false, ct, discoverEnvelope: true);
            if (clean.Usage != null) usages.Add(clean.Usage);
            if (clean.Tools != null && !new HashSet<string>(expected).SetEquals(clean.Tools))
            {
                if (!clean.Cleanup || !clean.Integrity) throw new ReviewValidationException("Manifest discovery failed fixture integrity or owned session cleanup.");
                if (clean.Tools.Length > 16 || clean.Tools.Any(t => t.Length is < 1 or > 64 || t.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not ('_' or '-' or '.'))))
                    throw new ReviewValidationException("The observed tool manifest is outside the supported capability schema.");
                certificate = certificate with { ExpectedTools = clean.Tools };
                var unknown = clean.Tools.Where(t => !CertificationContract.ToolCertified(certificate.CliVersion, t, CertificationContract.BundledTools)).ToArray();
                if (unknown.Length != 0) throw new ReviewValidationException($"Unverified tool capability: {string.Join(", ", unknown)}. Read/search capabilities can be investigated separately; execution/write/network capabilities are unsupported.");
                if (clean.Tools.Length != 3 || !clean.Tools.Contains("view") || !clean.Tools.Contains("glob")) throw new ReviewValidationException("The manifest is outside the supported read/search contract.");
                expected = clean.Tools;
                progress?.Report("Certified tool manifest discovered; verifying strict zero findings");
                clean = await CompatibilityCallAsync(fixture, new(modelId, effort), expected, false, ct, discoverEnvelope: true);
                if (clean.Usage != null) usages.Add(clean.Usage);
            }
            certificate = certificate with { ExpectedTools = expected };
            outputs.Add(clean.Output with { Case = "zero findings" });
            if (clean.Output.EnvelopeId != null) certificate = certificate with { OutputEnvelopeId = clean.Output.EnvelopeId };
            if (clean.Failure != null) throw new ReviewValidationException("Zero-findings probe: " + clean.Failure);
            probes.Add(new("zero findings", clean.Result?.Findings.Count == 0));
            progress?.Report("Testing deliberate defect and manifest stability");
            await fixture.SetCaseAsync(true, ct);
            var defect = await CompatibilityCallAsync(fixture, new(modelId, effort), expected, false, ct, certificate.OutputEnvelopeId);
            if (defect.Usage != null) usages.Add(defect.Usage);
            outputs.Add(defect.Output with { Case = "deliberate defect" });
            if (defect.Failure != null) throw new ReviewValidationException("Deliberate-defect probe: " + defect.Failure);
            probes.Add(new("deliberate defect", defect.Result!.Findings.Any(f => f.File?.Replace('\\', '/').EndsWith("Counter.cs", StringComparison.Ordinal) == true && f.Line is >= 1 and <= 2 &&
                (f.Evidence.Contains("value - 1", StringComparison.Ordinal) || f.Evidence.Contains("decrement", StringComparison.OrdinalIgnoreCase) || f.Evidence.Contains("subtract", StringComparison.OrdinalIgnoreCase)))));
            probes.Add(new("stable manifest", clean.Tools != null && defect.Tools != null && new HashSet<string>(expected).SetEquals(clean.Tools) && new HashSet<string>(expected).SetEquals(defect.Tools)));
            probes.Add(new("certified tools", expected.All(t => CertificationContract.ToolCertified(certificate.CliVersion, t, CertificationContract.BundledTools))));
            probes.Add(new("disabled MCP and forbidden authority", true)); // Both calls passed the unchanged production validator.
            probes.Add(new("structured output", true));
            probes.Add(new("fingerprint integrity", clean.Integrity && defect.Integrity));
            probes.Add(new("usage metadata", clean.Usage != null && defect.Usage != null));
            probes.Add(new("requested model", clean.Model == modelId && defect.Model == modelId));
            var repeatCleanup = true;
            if (certificate.OutputEnvelopeId != OutputEnvelope.RawJson)
            {
                progress?.Report("Checking output envelope stability with repeat zero findings");
                await fixture.SetCaseAsync(false, ct);
                var repeat = await CompatibilityCallAsync(fixture, new(modelId, effort), expected, false, ct, certificate.OutputEnvelopeId);
                if (repeat.Usage != null) usages.Add(repeat.Usage);
                outputs.Add(repeat.Output with { Case = "repeat zero findings" });
                if (repeat.Failure != null) throw new ReviewValidationException("Envelope stability probe: " + repeat.Failure);
                probes.Add(new("repeat zero findings", repeat.Result?.Findings.Count == 0));
                probes.Add(new("repeat fingerprint integrity", repeat.Integrity));
                probes.Add(new("repeat usage metadata", repeat.Usage != null));
                repeatCleanup = repeat.Cleanup;
            }
            probes.Add(new("stable output envelope", outputs.All(o => o.EnvelopeId == certificate.OutputEnvelopeId && o.EnvelopePassed && o.JsonParsed && o.SchemaPassed)));
            progress?.Report("Testing process-tree cancellation");
            var cancellation = await CompatibilityCallAsync(fixture, new(modelId, effort), expected, true, ct);
            if (cancellation.Usage != null) usages.Add(cancellation.Usage);
            probes.Add(new("cancellation", cancellation.Cancelled && cancellation.Integrity && cancellation.Result == null));
            probes.Add(new("owned session cleanup", clean.Cleanup && defect.Cleanup && repeatCleanup && cancellation.Cleanup)); // Each call awaited narrow CLI session deletion in finally.
            if (probes.Any(p => !p.Passed)) throw new ReviewValidationException("Required probe failed: " + string.Join(", ", probes.Where(p => !p.Passed).Select(p => p.Name)));
            certificate = certificate with { Status = CertificationStatus.Certified, FailureReason = null };
        }
        catch (OperationCanceledException) { certificate = certificate with { Status = CertificationStatus.NeedsRetest, FailureReason = "Compatibility testing was cancelled. Partial evidence cannot authorize reviews." }; }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or JsonException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // Only fixed application diagnostics are persisted; never arbitrary CLI output, fixture contents or paths.
            certificate = certificate with { Status = CertificationStatus.Rejected, FailureReason = ex is ReviewValidationException ? ex.Message : "Compatibility testing could not complete under the production security contract." };
        }
        certificate = certificate with { Probes = probes.ToArray(), Usage = TotalUsage(usages), EnvelopeObservations = outputs.ToArray() };
        await registry.SaveAsync(certificate, CancellationToken.None);
        progress?.Report(certificate.Status == CertificationStatus.Certified ? "Certified; available for new reviews" : "Not certified; see compatibility evidence");
        return certificate;
    }
    private async Task<CompatibilityRun> CompatibilityCallAsync(CertificationFixture fixture, ReviewExecutionSettings execution, string[] tools, bool cancelProbe, CancellationToken ct, string envelopeId = OutputEnvelope.RawJson, bool discoverEnvelope = false)
    {
        var input = await new GitReviewContext().PrepareAsync(fixture.Project, ReviewScope.WorkingChanges, ct: ct);
        var run = CreateRunDirectory(); var usageFile = Path.Combine(run, "usage.json");
        var validator = new CopilotStreamValidator(tools, execution.ModelId, envelopeId, discoverEnvelope);
        var ownedSession = Guid.NewGuid();
        ReviewUsage? usage = null; ReviewResult? result = null; string? failure = null; var cancelled = false; var launchedOwnedSession = false; var cleanup = true;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            var configuration = CopilotPreflight.Inspect(Profile, run, fixture.Repository);
            var installation = CopilotContract.Detect() ?? throw new InvalidOperationException();
            // Discovery uses the established technical CLI tools. No unknown capability is granted authority.
            var ownedPath = Path.Combine(Profile, "session-state", ownedSession.ToString());
            CopilotPreflight.AssertNoReparseAncestors(ownedPath);
            if (Directory.Exists(ownedPath) || File.Exists(ownedPath)) throw new ReviewValidationException("Generated certification session identity already exists.");
            var args = CopilotContract.ReviewArguments(fixture.Repository, Path.Combine(run, "logs"), usageFile: usageFile).ToList();
            args.AddRange(["--session-id", ownedSession.ToString()]);
            args.AddRange(["--model", execution.ModelId]);
            if (execution.ReasoningEffort != "auto") args.AddRange(["--reasoning-effort", execution.ReasoningEffort]);
            var info = CopilotContract.StartInfo(installation, run, Profile, Path.Combine(run, "cache"), args);
            if (CopilotPreflight.Inspect(Profile, run, fixture.Repository) != configuration) throw new InvalidOperationException();
            CopilotGitHubCliIsolation.Inspect(installation, run);
            launchedOwnedSession = true;
            var process = await CopilotProcess.RunAsync(info, PromptComposer.Compose(input, ["standard"]), line =>
            {
                validator.Accept(line);
                if (cancelProbe && line.Contains("\"session.tools_updated\"", StringComparison.Ordinal)) timeout.Cancel();
            }, timeout.Token);
            result = validator.Complete(process.ExitCode, process.Cancelled);
        }
        catch (OperationCanceledException) { ct.ThrowIfCancellationRequested(); cancelled = cancelProbe; failure = "Interrupted"; }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException) { failure = ex is JsonException ? "Structured output is not strict JSON; no formatting repair is permitted." : validator.Invalid ?? ex.Message; }
        finally
        {
            try { if (File.Exists(usageFile) && new FileInfo(usageFile).Length <= 128 * 1024 && (File.GetAttributes(usageFile) & FileAttributes.ReparsePoint) == 0) usage = CopilotUsageParser.Parse(await File.ReadAllTextAsync(usageFile)); }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException) { }
            try { if (launchedOwnedSession) await DeleteOwnedCertificationSessionAsync(ownedSession); }
            catch (ReviewValidationException) { cleanup = false; failure = "Owned CLI certification session cleanup failed. Compatibility cannot be certified until cleanup succeeds."; }
            finally { DeleteRunDirectory(run); }
        }
        var integrity = await new GitReviewContext().FingerprintAsync(fixture.Repository, ct) == input.Fingerprint;
        return new(result, validator.ObservedTools, validator.Model, usage, integrity, cancelled, cleanup, failure, validator.OutputObservation("zero findings"));
    }
    internal static ReviewUsage? TotalUsage(IReadOnlyList<ReviewUsage> usages) => usages.Count == 0 ? null : new(
        usages.SelectMany(u => u.Models).ToArray(), usages.All(u => u.NanoAiUnits != null) ? usages.Sum(u => u.NanoAiUnits) : null,
        usages.All(u => u.PremiumRequestCost != null) ? usages.Sum(u => u.PremiumRequestCost) : null);
}
