namespace AIReviewDesk.Core;

/// <summary>Presentation uses captured data only, never current Git or profile definitions.</summary>
public sealed record ReviewDetails(ReviewRecord Record)
{
    public bool CanHandoff => Record.Status == ReviewStatus.Completed;
    public IReadOnlyList<ReviewFinding> Findings => CanHandoff ? Record.Result.Findings : [];
    public int FindingsCount => Findings.Count;
    public bool HasFindings => FindingsCount > 0;
    public string StatusLabel => Record.Status switch
    {
        ReviewStatus.Completed when FindingsCount == 0 => "Completed · No findings",
        ReviewStatus.Completed => $"Completed · {FindingsCount} finding{(FindingsCount == 1 ? "" : "s")}",
        ReviewStatus.Unsupported => "Unsupported / blocked",
        _ => Record.Status.ToString()
    };
    public string Outcome => Record.Status switch
    {
        ReviewStatus.Completed when FindingsCount == 0 => "No findings reported in this scope. Independent review does not establish that no defects exist.",
        ReviewStatus.Completed => "Independent Copilot findings require assessment and verification.",
        ReviewStatus.Cancelled => "Review cancelled. Partial findings were discarded; no accepted result is available.",
        ReviewStatus.Stale => "Repository state changed during preparation or review. Findings were discarded; no accepted result is available.",
        ReviewStatus.Unsupported => "The CLI or configuration blocked this review. No accepted result is available.",
        _ => "Review failed to produce an accepted result."
    };
    public string ScopeLabel => ScopeName(Record.Scope);
    public string ProfilesLabel => Record.ProfileIds.Count == 0 ? "Profiles not recorded" : string.Join(" + ", Record.ProfileIds.Select(id =>
        Record.ProfileNames.TryGetValue(id, out var name) ? name : $"{id} (name not recorded)"));
    public string ProfileVersionsLabel => string.Join(" · ", Record.ProfileIds.Select(id =>
        Record.ProfileVersions.TryGetValue(id, out var version) ? $"{id} v{version}" : $"{id}: version not recorded"));
    public string TimeLabel => Record.TimestampUtc.LocalDateTime.ToString("g");
    public string ContextSummary => $"{TimeLabel} · {Record.ProjectName} · {ScopeLabel}";
    public string ContextLabel => $"{ScopeLabel} · {Record.Branch} · HEAD {Short(Record.HeadSha)}";
    public string SeveritySummary => string.Join(" / ", new[] { "critical", "high", "medium", "low", "info" }
        .Select(s => (Severity: s, Count: Findings.Count(f => f.Severity == s))).Where(x => x.Count > 0).Select(x => $"{x.Count} {x.Severity}"));
    public string ChangeCounts => Record.TrackedChangedCount is int tracked && Record.UntrackedCount is int untracked
        ? $"Tracked changes: {tracked}\nUntracked files: {untracked}"
        : Record.SchemaVersion >= 3 ? "Tracked/untracked counts unavailable for this preparation."
        : $"Legacy combined changed/untracked files: {Record.ChangedFileCount} (split not recorded)";
    public string SnapshotText => $"Repository: {Record.RepositoryPath}\nBranch: {Record.Branch}\nHEAD: {Record.HeadSha ?? "unavailable"}\nBase: {Record.BaseRef ?? "unavailable"}\nMerge base: {Record.MergeBaseSha ?? "unavailable"}\n{ChangeCounts}\nSelected paths: {(Record.SelectedPaths.Count == 0 ? "none" : string.Join(", ", Record.SelectedPaths))}\nCopilot CLI: {Record.CopilotCliVersion}\nApp: {Record.AppVersion}\nModel: {Record.CopilotModel ?? "unavailable"}\nProfiles: {ProfileVersionsLabel}\nShared policy: v{Record.SharedPolicyVersion}\nFingerprint before: {Record.FingerprintBefore}\nFingerprint after: {Record.FingerprintAfter}\nDiff hash: {Record.DiffHash ?? "unavailable"}\nRun: {Record.Id}";
    public string ResultSummary => CanHandoff ? Record.Result.Summary : "";
    public string Limitations => CanHandoff ? string.Join("\n", Record.Result.Limitations) : "";
    public bool HasLimitations => Limitations.Length > 0;
    public string Diagnostic => Record.Diagnostic ?? "";
    public bool HasDiagnostic => Diagnostic.Length > 0;
    public static string ScopeName(ReviewScope scope) => scope switch
    {
        ReviewScope.WorkingChanges => "Working changes",
        ReviewScope.BranchVsBase => "Branch versus base",
        ReviewScope.SelectedPaths => "Selected changed paths",
        _ => "Scope unavailable"
    };
    private static string Short(string? sha) => string.IsNullOrEmpty(sha) ? "unavailable" : sha[..Math.Min(8, sha.Length)];
}

public sealed record FindingItem(ReviewFinding Finding, string RepositoryPath = "")
{
    public string Title => Finding.Title;
    public string Classification => $"{Finding.Severity} · {Finding.Certainty} · {Finding.Category}";
    public string Location
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Finding.File)) return "Location unavailable";
            var path = Finding.File.Replace('\\', '/');
            var root = RepositoryPath.Replace('\\', '/').TrimEnd('/');
            if (root.Length > 0 && path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)) path = path[(root.Length + 1)..];
            return path + (Finding.Line is int line ? $":{line}" : "");
        }
    }
    public override string ToString() => $"{Classification} · {Title} · {Location}";
}

public static class FindingFilter
{
    public static IReadOnlyList<ReviewFinding> Apply(IEnumerable<ReviewFinding> findings, string? severity, string? certainty, string? category, string? search)
    {
        var query = search?.Trim().Replace('\\', '/') ?? "";
        bool Matches(string value, string? filter) => string.IsNullOrEmpty(filter) || filter == "All" || string.Equals(value, filter, StringComparison.OrdinalIgnoreCase);
        return findings.Where(f => Matches(f.Severity, severity) && Matches(f.Certainty, certainty) && Matches(f.Category, category) &&
            (query.Length == 0 || f.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || (f.File?.Replace('\\', '/').Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))).ToArray();
    }
}

public sealed record ReusedReviewSetup(ReviewScope Scope, IReadOnlyList<string> ProfileIds, IReadOnlyList<string> SelectedPaths, string Message)
{
    public static ReusedReviewSetup From(ReviewRecord record, IReadOnlyList<string> currentChangedPaths, bool hasBase)
    {
        var profiles = BuiltInProfiles.All.Where(p => record.ProfileIds.Contains(p.Id, StringComparer.OrdinalIgnoreCase)).Select(p => p.Id).ToArray();
        var paths = record.Scope == ReviewScope.SelectedPaths
            ? record.SelectedPaths.Where(p => currentChangedPaths.Contains(p, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToArray() : [];
        var scope = record.Scope == ReviewScope.BranchVsBase && !hasBase ? ReviewScope.WorkingChanges : record.Scope;
        var messages = new List<string> { "Setup reused. Preview a fresh repository snapshot before starting the review." };
        if (record.Scope == ReviewScope.BranchVsBase && !hasBase) messages.Add("No current base is available; scope changed to Working changes.");
        if (paths.Length < record.SelectedPaths.Count && record.Scope == ReviewScope.SelectedPaths) messages.Add("Paths outside the current changed set were cleared. Choose changed paths if needed.");
        if (profiles.Length < record.ProfileIds.Count) messages.Add("Unavailable profiles were cleared. Choose at least one current profile.");
        return new(scope, profiles, paths, string.Join(" ", messages));
    }
}
