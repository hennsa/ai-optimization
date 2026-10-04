using System.Text.Json;
using AIReviewDesk.Core;

namespace AIReviewDesk.Infrastructure;

/// <summary>Same-user application state, not a signing or cryptographic trust boundary.</summary>
public sealed class CertificationRegistry(string? dataDirectory = null)
{
    public string FilePath { get; } = Path.Combine(dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIReviewDesk"), "certifications.json");
    private sealed record State(int SchemaVersion, ModelCertificate[] Models);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public string? LoadWarning { get; private set; }
    public IReadOnlyList<ModelCertificate> ReadLocal()
    {
        LoadWarning = null;
        if (!File.Exists(FilePath)) return [];
        try
        {
            CopilotPreflight.AssertNoReparseAncestors(FilePath);
            if (new FileInfo(FilePath).Length > 256 * 1024) throw new InvalidOperationException();
            using var document = JsonDocument.Parse(File.ReadAllText(FilePath));
            CopilotStreamValidator.RequireUniqueProperties(document.RootElement);
            var state = document.Deserialize<State>(CertificationContract.JsonOptions)!;
            if (state.SchemaVersion != 1 || state.Models == null || state.Models.Length > 100 ||
                state.Models.Any(c => c == null || c.SchemaVersion != 1 || string.IsNullOrWhiteSpace(c.ModelId) || c.ModelId.Length > 128 || (c.Source != "locally certified" && !(c.Source == "bundled certification" && c.Status == CertificationStatus.NeedsRetest)) ||
                    !Enum.IsDefined(c.Status) || c.ReasoningEfforts == null || c.ExpectedTools == null || c.TechnicalTools == null || c.Probes == null || c.Probes.Any(p => p == null || string.IsNullOrWhiteSpace(p.Name)) ||
                    c.ExpectedTools.Any(t => t == null) || c.TechnicalTools.Any(t => t == null) || c.ReasoningEfforts.Any(e => string.IsNullOrWhiteSpace(e))) ||
                state.Models.Select(c => c.ModelId).Distinct().Count() != state.Models.Length) throw new InvalidOperationException();
            return state.Models;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException or UnauthorizedAccessException or NullReferenceException)
        { LoadWarning = "Local certification records are unreadable or use an unsupported schema. Explicit retesting is required."; return []; }
    }
    public IReadOnlyList<ModelCompatibility> Discover(IReadOnlyList<CopilotModelChoice> live, string? cli, string suite = CertificationContract.SuiteVersion)
    {
        var local = ReadLocal();
        var certificates = CertificationContract.BundledModels.Concat(local).GroupBy(c => c.ModelId).Select(g => g.Last()).ToDictionary(c => c.ModelId);
        var rows = new List<ModelCompatibility>();
        foreach (var model in live.Where(m => m.Id != "auto"))
        {
            certificates.TryGetValue(model.Id, out var c);
            var reason = c == null || c.Status == CertificationStatus.Rejected && c.CliVersion == cli && c.SuiteVersion == suite ? null : CertificationContract.InvalidReason(c, cli, CertificationContract.BundledTools, suite);
            var status = c?.Status ?? CertificationStatus.Unverified;
            if (LoadWarning != null) { status = CertificationStatus.NeedsRetest; reason = LoadWarning; }
            else if (c != null && reason != null) status = CertificationStatus.NeedsRetest;
            else if (status == CertificationStatus.Testing) { status = CertificationStatus.NeedsRetest; reason = "The previous compatibility test did not finish."; }
            else if (status == CertificationStatus.Certified && !c!.ReasoningEfforts.Any(e => e == "auto" || model.Efforts.Contains(e)))
            { status = CertificationStatus.NeedsRetest; reason = "Certified reasoning levels are no longer advertised."; }
            rows.Add(new(model, status, c, reason ?? c?.FailureReason));
        }
        foreach (var c in certificates.Values.Where(c => !live.Any(m => m.Id == c.ModelId)))
            rows.Add(new(new(c.ModelId, c.DisplayName, []), CertificationStatus.NoLongerAdvertised, c, "Model is no longer advertised; certification cannot authorize a review."));
        return rows;
    }
    public IReadOnlyList<CopilotModelChoice> Selectable(IReadOnlyList<CopilotModelChoice> live, string? cli) =>
        [CopilotModelPolicy.Auto, .. Discover(live, cli).Where(r => r.Status == CertificationStatus.Certified)
            .Select(r => r.Model with { Efforts = r.Certificate!.ReasoningEfforts.Where(e => e == "auto" || r.Model.Efforts.Contains(e)).ToArray() })];
    public ModelCertificate Resolve(ReviewExecutionSettings execution, IReadOnlyList<CopilotModelChoice> live, string? cli)
    {
        CopilotModelPolicy.Validate(execution, cli, Selectable(live, cli));
        return Discover(live, cli).Single(r => r.Model.Id == execution.ModelId && r.Status == CertificationStatus.Certified).Certificate!;
    }
    public async Task SaveAsync(ModelCertificate certificate, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        string? temp = null;
        try
        {
            var current = ReadLocal();
            if (LoadWarning != null) throw new InvalidOperationException(LoadWarning + " Preserve or remove the damaged file before saving new evidence.");
            var state = new State(1, [.. current.Where(c => c.ModelId != certificate.ModelId), certificate with { Source = certificate.Source == "bundled certification" && certificate.Status == CertificationStatus.NeedsRetest ? "bundled certification" : "locally certified" }]);
            var directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory); CopilotPreflight.AssertNoReparseAncestors(FilePath);
            temp = Path.Combine(directory, "certifications-" + Guid.NewGuid().ToString("N") + ".tmp");
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { await JsonSerializer.SerializeAsync(stream, state, CertificationContract.JsonOptions, ct); await stream.FlushAsync(ct); }
            ct.ThrowIfCancellationRequested(); File.Move(temp, FilePath, true);
        }
        finally { if (temp != null && File.Exists(temp)) File.Delete(temp); Gate.Release(); }
    }
    public Task SuspendAsync(ModelCertificate c) => SaveAsync(c with { Status = CertificationStatus.NeedsRetest, FailureReason = "Runtime capability or strict output contract changed. Run compatibility testing again." });
    public async Task ObserveDiscoveryAsync(IReadOnlyList<CopilotModelChoice> live)
    {
        var local = ReadLocal();
        if (LoadWarning != null) return;
        foreach (var c in CertificationContract.BundledModels.Concat(local).GroupBy(c => c.ModelId).Select(g => g.Last())
            .Where(c => c.Status == CertificationStatus.Certified && !live.Any(m => m.Id == c.ModelId)))
            await SaveAsync(c with { Status = CertificationStatus.NeedsRetest, FailureReason = "The certified model stopped being advertised. Explicit recertification is required if it returns." });
    }
}
