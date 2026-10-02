namespace AIReviewDesk.Core;

public sealed record ProjectRegistration
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string DisplayName { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public string? DefaultBase { get; init; }
    public string DefaultProfileId { get; init; } = "standard";
}

public sealed record AppState
{
    public int SchemaVersion { get; init; } = 1;
    public List<ProjectRegistration> Projects { get; init; } = [];
    public Guid? SelectedProjectId { get; init; }
    public string Theme { get; init; } = "System";
    public string DefaultProfileId { get; init; } = "standard";
    public bool RefreshOnActivate { get; init; } = true;
}

public sealed record ReviewProfile(string Id, string Name, string Description);

public static class BuiltInProfiles
{
    public static IReadOnlyList<ReviewProfile> All { get; } = Array.AsReadOnly(new[]
    {
        new ReviewProfile("standard", "Standard implementation", "Correctness, edge cases, error handling, and concrete regression risk."),
        new ReviewProfile("bug-fix", "Bug fix", "Whether the change addresses the reported behavior and handles adjacent states."),
        new ReviewProfile("regression", "Regression", "Behavior changed against the supplied base and likely compatibility regressions."),
        new ReviewProfile("security", "Security", "Trust boundaries, validation, authorization, secrets, unsafe flows, and exposure."),
        new ReviewProfile("database", "Database / EF", "Schema consistency, migrations, queries, transactions, integrity, and EF usage."),
        new ReviewProfile("api", "API contract", "Request and response shape, validation, compatibility, serialization, and errors."),
        new ReviewProfile("frontend", "Frontend", "UI state, interaction, accessibility, and visible client/server contract handling."),
        new ReviewProfile("architecture", "Architecture", "Boundaries, coupling, responsibility, lifecycle, and concurrency concerns."),
        new ReviewProfile("tests", "Test coverage", "Coverage of changed behavior, edge cases, assertions, and visible test consistency.")
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
    public string DiffSummary { get; init; } = string.Empty;
    public bool IsClean => StagedCount == 0 && UnstagedCount == 0 && UntrackedCount == 0 && ConflictCount == 0;
}
