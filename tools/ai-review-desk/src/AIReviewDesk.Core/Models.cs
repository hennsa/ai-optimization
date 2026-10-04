namespace AIReviewDesk.Core;

public sealed record ProjectRegistration
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string DisplayName { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public string? DefaultBase { get; init; }

    // Kept for compatibility with existing UI and persisted state. New multi-profile
    // settings use DefaultProfileIds; registry migration can populate it from this value.
    public string DefaultProfileId { get; init; } = "standard";
    public List<string>? DefaultProfileIds { get; init; }
    public ReviewExecutionSettings DefaultExecution { get; init; } = CopilotModelPolicy.Default;
}

public sealed record AppState
{
    public int SchemaVersion { get; init; } = 1;
    public List<ProjectRegistration> Projects { get; init; } = [];
    public Guid? SelectedProjectId { get; init; }
    public string Theme { get; init; } = "System";
    public string DefaultProfileId { get; init; } = "standard";
    public List<string>? DefaultProfileIds { get; init; }
    public bool RefreshOnActivate { get; init; } = true;
}

public sealed record ReviewProfile(string Id, string Name, string Description, string Instructions, string Version);

public static class BuiltInProfiles
{
    public static IReadOnlyList<ReviewProfile> All { get; } = Array.AsReadOnly(new[]
    {
        new ReviewProfile("standard", "Standard implementation", "Correctness, edge cases, error handling, and concrete regression risk.",
            "Review correctness of the change, edge cases, error handling, maintainability issues that create concrete risk, and regressions visible in the supplied scope.", "1"),
        new ReviewProfile("bug-fix", "Bug fix", "Whether the change addresses the reported behavior and handles adjacent states.",
            "Review whether the change addresses the reported behavior, handles adjacent inputs and states, and avoids masking symptoms or introducing a related defect. Do not assume unstated reproduction steps were tested.", "1"),
        new ReviewProfile("regression", "Regression", "Behavior changed against the supplied base and likely compatibility regressions.",
            "Review behavior changed relative to the supplied base or context, compatibility and neighboring flows, state transitions, and likely regressions. Avoid restating pre-existing issues as new findings unless the change materially affects them.", "1"),
        new ReviewProfile("security", "Security", "Trust boundaries, validation, authorization, secrets, unsafe flows, and exposure.",
            "Review trust boundaries, input validation, authorization, secret handling, unsafe data flow, and exposure introduced or worsened by the change. Require a concrete path and impact; do not claim exploitability without evidence.", "1"),
        new ReviewProfile("database", "Database / EF", "Schema consistency, migrations, queries, transactions, integrity, and EF usage.",
            "Review schema and model consistency, migrations, query behavior, transaction and concurrency risks, data integrity, nullability, and EF usage. Do not infer runtime database behavior absent evidence.", "1"),
        new ReviewProfile("api", "API contract", "Request and response shape, validation, compatibility, serialization, and errors.",
            "Review request and response shape, validation, status and error behavior, compatibility, serialization, versioning, and consistency between implementation and visible contracts.", "1"),
        new ReviewProfile("frontend", "Frontend", "UI state, interaction, accessibility, and visible client/server contract handling.",
            "Review UI state and interaction correctness, loading, error and empty states, accessibility concerns evident in changed code, and client/server contract handling. Do not claim browser behavior was executed.", "1"),
        new ReviewProfile("architecture", "Architecture", "Boundaries, coupling, responsibility, lifecycle, and concurrency concerns.",
            "Review boundary violations, unintended coupling, responsibility placement, lifecycle and concurrency concerns, and consistency with architecture evidence supplied in context. Avoid taste-only or speculative redesign suggestions.", "1"),
        new ReviewProfile("tests", "Test coverage", "Coverage of changed behavior, edge cases, assertions, and visible test consistency.",
            "Review for missing tests for changed behavior, weak assertions, uncovered edge cases, and tests that appear inconsistent with the implementation. Assess visible source only; do not claim tests were run.", "1")
    });

    public static bool Contains(string? id) => id is not null && All.Any(profile =>
        string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase));
}

public sealed record RepositorySnapshot
{
    public string RootPath { get; init; } = string.Empty;
    public string Branch { get; init; } = string.Empty;
    public string? HeadSha { get; init; }
    public string? BaseRef { get; init; }
    public string? MergeBaseSha { get; init; }
    public string? BaseWarning { get; init; }
    public IReadOnlyList<string> BaseCandidates { get; init; } = Array.Empty<string>();
    public int StagedCount { get; init; }
    public int UnstagedCount { get; init; }
    public int UntrackedCount { get; init; }
    public int ConflictCount { get; init; }
    public int ChangedFileCount { get; init; }
    public int TrackedChangedCount { get; init; }
    public IReadOnlyList<string> ChangedPaths { get; init; } = Array.Empty<string>();
    public string DiffSummary { get; init; } = string.Empty;
    public bool IsClean => StagedCount == 0 && UnstagedCount == 0 && UntrackedCount == 0 && ConflictCount == 0;
}

public enum ReviewScope
{
    WorkingChanges,
    BranchVsBase,
    SelectedPaths
}

public sealed record ReviewInput
{
    public bool IsPreview { get; init; }
    public bool HasReviewableChanges { get; init; }
    public ProjectRegistration Project { get; init; } = new();
    public RepositorySnapshot Snapshot { get; init; } = new();
    public ReviewScope Scope { get; init; } = ReviewScope.WorkingChanges;
    public IReadOnlyList<string> SelectedPaths { get; init; } = Array.Empty<string>();
    public string Context { get; init; } = string.Empty;
    public string Fingerprint { get; init; } = string.Empty;
}

public enum ReviewStatus
{
    Completed,
    Failed,
    Cancelled,
    Stale,
    Unsupported
}

public sealed record ReviewFinding
{
    public string Id { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Certainty { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? File { get; init; }
    public int? Line { get; init; }
    public string Evidence { get; init; } = string.Empty;
    public string Impact { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
}

public sealed record ReviewResult
{
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<ReviewFinding> Findings { get; init; } = Array.Empty<ReviewFinding>();
    public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();
}

/// <summary>Compact run metadata and parsed results. Full repository diffs are intentionally excluded.</summary>
public sealed record ReviewRecord
{
    public ResponseStructure? OutputStructure { get; init; }
    public int SchemaVersion { get; init; } = 1;
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public string Branch { get; init; } = string.Empty;
    public string? HeadSha { get; init; }
    public string? BaseRef { get; init; }
    public string? MergeBaseSha { get; init; }
    public ReviewScope Scope { get; init; }
    public IReadOnlyList<string> SelectedPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ProfileIds { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> ProfileVersions { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> ProfileNames { get; init; } = new Dictionary<string, string>();
    public string SharedPolicyVersion { get; init; } = SharedReviewerPolicy.Version;
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string AppVersion { get; init; } = string.Empty;
    public string CopilotCliVersion { get; init; } = string.Empty;
    public string? CopilotModel { get; init; }
    public ReviewExecutionSettings? RequestedExecution { get; init; }
    public ReviewUsage? Usage { get; init; }
    public PromptSizeEvidence? PromptSize { get; init; }
    public int ChangedFileCount { get; init; }
    public int? TrackedChangedCount { get; init; }
    public int? UntrackedCount { get; init; }
    public string? DiffHash { get; init; }
    public string FingerprintBefore { get; init; } = string.Empty;
    public string FingerprintAfter { get; init; } = string.Empty;
    public ReviewStatus Status { get; init; }
    public ReviewResult Result { get; init; } = new();
    public string? Diagnostic { get; init; }
}
