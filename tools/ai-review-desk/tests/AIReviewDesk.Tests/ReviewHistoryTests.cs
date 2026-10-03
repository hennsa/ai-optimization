using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIReviewDesk.App;
using AIReviewDesk.Core;
using AIReviewDesk.Infrastructure;

namespace AIReviewDesk.Tests;

public sealed class ReviewHistoryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "AIReviewDesk.HistoryTests", Guid.NewGuid().ToString("N"));
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };
    private readonly Guid projectId = Guid.NewGuid();
    public ReviewHistoryTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public async Task History_is_project_scoped_newest_first_and_survives_store_restart()
    {
        var store = new ReviewHistoryStore(root);
        var old = Record() with { TimestampUtc = DateTimeOffset.UtcNow.AddDays(-2) };
        var recent = Record();
        await store.SaveAsync(recent);
        await store.SaveAsync(old);
        await store.SaveAsync(Record() with { ProjectId = Guid.NewGuid() });
        var reopened = await new ReviewHistoryStore(root).LoadAsync(projectId);
        Assert.Equal(new[] { recent.Id, old.Id }, reopened.Select(r => r.Id));
    }

    [Theory]
    [InlineData(ReviewStatus.Completed, "Completed", true)]
    [InlineData(ReviewStatus.Failed, "Failed", false)]
    [InlineData(ReviewStatus.Cancelled, "Cancelled", false)]
    [InlineData(ReviewStatus.Stale, "Stale", false)]
    [InlineData(ReviewStatus.Unsupported, "Unsupported / blocked", false)]
    public async Task Reopening_respects_status_and_only_accepted_results_have_findings_and_handoffs(ReviewStatus status, string label, bool accepted)
    {
        var record = Record() with { Status = status, Diagnostic = "Safe explanation" };
        var store = new ReviewHistoryStore(root);
        await store.SaveAsync(record);
        var loaded = Assert.Single(await store.LoadAsync(projectId));
        var detail = new ReviewDetails(loaded);
        Assert.StartsWith(label, detail.StatusLabel);
        Assert.Equal(accepted, detail.CanHandoff);
        Assert.Equal(accepted ? 3 : 0, detail.FindingsCount);
        Assert.Equal("Safe explanation", detail.Diagnostic);
        Assert.NotEmpty(detail.Outcome);
        if (!accepted)
        {
            Assert.Empty(loaded.Result.Findings);
            Assert.Throws<InvalidOperationException>(() => HandoffFormatter.FormatResult(loaded));
        }
    }

    [Fact]
    public async Task Zero_findings_is_an_intentional_completed_state_with_handoffs()
    {
        var store = new ReviewHistoryStore(root);
        await store.SaveAsync(Record() with { Result = new ReviewResult { Summary = "No concrete defects found." } });
        var detail = new ReviewDetails(Assert.Single(await store.LoadAsync(projectId)));
        Assert.Equal("Completed · No findings", detail.StatusLabel);
        Assert.False(detail.HasFindings);
        Assert.Contains("does not establish", detail.Outcome);
        Assert.Contains("No findings reported.", HandoffFormatter.FormatResult(detail.Record));
    }

    [Fact]
    public async Task Historical_names_versions_snapshot_and_handoffs_use_persisted_result()
    {
        var store = new ReviewHistoryStore(root);
        var original = Record();
        await store.SaveAsync(original);
        var reopened = Assert.Single(await store.LoadAsync(projectId));
        var detail = new ReviewDetails(reopened);
        Assert.Equal("Original security lens", detail.ProfilesLabel);
        Assert.Equal("security vold", detail.ProfileVersionsLabel);
        Assert.Contains("HEAD: historical-head", detail.SnapshotText);
        Assert.Contains("old-base", detail.SnapshotText);
        Assert.Equal("1 high / 2 medium", detail.SeveritySummary);
        Assert.Equal(HandoffFormatter.FormatResult(original), HandoffFormatter.FormatResult(reopened));
        Assert.Contains("likely false positive", HandoffFormatter.FormatForChatGPT(reopened));
        Assert.Contains("Independently verify", HandoffFormatter.FormatForCodex(reopened));
        Assert.Contains("Do not implement a finding merely", HandoffFormatter.FormatForCodex(reopened));
    }

    [Fact]
    public async Task Old_shape_defaults_names_without_using_current_profile_text()
    {
        var json = JsonSerializer.Serialize(Record(), Json);
        using var doc = JsonDocument.Parse(json);
        var old = doc.RootElement.EnumerateObject().Where(p => p.Name is not ("SchemaVersion" or "ProfileNames")).ToDictionary(p => p.Name, p => p.Value.Clone());
        await WriteRecord(JsonSerializer.Serialize(old));
        var loaded = Assert.Single(await new ReviewHistoryStore(root).LoadAsync(projectId));
        Assert.Equal(1, loaded.SchemaVersion);
        Assert.Contains("security (name not recorded)", new ReviewDetails(loaded).ProfilesLabel);
        Assert.Equal("old", loaded.ProfileVersions["security"]);
        Assert.Equal(3, loaded.Result.Findings.Count);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"Status\":\"Completed\"}")]
    [InlineData("null")]
    public async Task Malformed_history_is_skipped_and_preserved(string json)
    {
        await WriteRecord(json);
        Assert.Empty(await new ReviewHistoryStore(root).LoadAsync(projectId));
        Assert.Single(Directory.GetFiles(Path.Combine(root, "Reviews")));
    }

    [Theory]
    [InlineData("Status")]
    [InlineData("TimestampUtc")]
    [InlineData("Result")]
    [InlineData("Scope")]
    public async Task Missing_acceptance_metadata_cannot_become_a_completed_result(string field)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(Record(), Json));
        var damaged = doc.RootElement.EnumerateObject().Where(p => p.Name != field).ToDictionary(p => p.Name, p => p.Value.Clone());
        await WriteRecord(JsonSerializer.Serialize(damaged));
        Assert.Empty(await new ReviewHistoryStore(root).LoadAsync(projectId));
    }

    [Fact]
    public async Task Explicit_null_optional_collections_default_safely()
    {
        await WriteRecord(JsonSerializer.Serialize(Record() with { ProfileNames = null!, ProfileVersions = null!, SelectedPaths = null!, ProfileIds = null! }, Json));
        var loaded = Assert.Single(await new ReviewHistoryStore(root).LoadAsync(projectId));
        Assert.Equal("Profiles not recorded", new ReviewDetails(loaded).ProfilesLabel);
    }

    [Fact]
    public async Task Malformed_accepted_findings_are_not_reopened_as_valid_history()
    {
        var finding = Findings()[0] with { Severity = "invented" };
        await WriteRecord(JsonSerializer.Serialize(Record() with { Result = new ReviewResult { Findings = [finding] } }, Json));
        Assert.Empty(await new ReviewHistoryStore(root).LoadAsync(projectId));
    }

    [Theory]
    [InlineData(ReviewStatus.Failed)]
    [InlineData(ReviewStatus.Cancelled)]
    [InlineData(ReviewStatus.Stale)]
    [InlineData(ReviewStatus.Unsupported)]
    public async Task Legacy_noncompleted_records_discard_any_partial_findings_on_load(ReviewStatus status)
    {
        await WriteRecord(JsonSerializer.Serialize(Record() with { SchemaVersion = 1, Status = status }, Json));
        var loaded = Assert.Single(await new ReviewHistoryStore(root).LoadAsync(projectId));
        Assert.Empty(loaded.Result.Findings);
        Assert.False(new ReviewDetails(loaded).CanHandoff);
    }

    [Fact]
    public async Task Context_preparation_failure_is_recorded_without_launch_or_partial_findings()
    {
        InitGit();
        var vm = new DeskViewModel(root);
        vm.RebuildProfileChoices(["standard"]);
        await vm.SaveProjectAsync(new ProjectRegistration { Id = projectId, RepositoryPath = root }, true);
        await vm.StartReviewAsync(ReviewScope.SelectedPaths, []);
        Assert.False(vm.IsReviewRunning);
        Assert.True(vm.ShowReviewDetail);
        Assert.Equal(ReviewStatus.Failed, vm.LatestReview?.Status);
        Assert.False(vm.CanHandoffLatest);
        var loaded = Assert.Single(await new ReviewHistoryStore(root).LoadAsync(projectId));
        Assert.Contains("context could not be prepared", loaded.Diagnostic);
        Assert.Empty(loaded.Result.Findings);
    }

    [Fact]
    public async Task Future_schema_is_preserved_but_not_interpreted()
    {
        await WriteRecord(JsonSerializer.Serialize(Record() with { SchemaVersion = 99 }, Json));
        Assert.Empty(await new ReviewHistoryStore(root).LoadAsync(projectId));
        Assert.Single(Directory.GetFiles(Path.Combine(root, "Reviews")));
    }

    [Fact]
    public async Task History_is_bounded_and_registration_removal_preserves_records_without_cross_project_reassociation()
    {
        var store = new ReviewHistoryStore(root);
        for (var i = 0; i < 102; i++) await store.SaveAsync(Record() with { TimestampUtc = DateTimeOffset.UtcNow.AddMinutes(-i) });
        Assert.Equal(100, (await store.LoadAsync(projectId)).Count);
        var registry = new RegistryStore(root);
        await registry.SaveAsync(new AppState { Projects = [new ProjectRegistration { Id = projectId, RepositoryPath = root }] });
        await registry.SaveAsync(new AppState());
        Assert.Empty((await registry.LoadAsync()).State.Projects);
        Assert.Equal(100, (await store.LoadAsync(projectId)).Count);
        Assert.Empty(await store.LoadAsync(Guid.NewGuid()));
        Assert.Equal(102, Directory.GetFiles(Path.Combine(root, "Reviews")).Length);
    }

    [Fact]
    public async Task Persistence_is_compact_and_contains_no_prompt_diff_or_protocol_fields()
    {
        await new ReviewHistoryStore(root).SaveAsync(Record());
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Directory.GetFiles(Path.Combine(root, "Reviews")).Single()));
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.DoesNotContain("Context", keys); Assert.DoesNotContain("Prompt", keys);
        Assert.DoesNotContain("StandardOutput", keys); Assert.DoesNotContain("StandardError", keys);
        Assert.DoesNotContain("Transcript", keys);
        Assert.True(doc.RootElement.GetRawText().Length < 10000);
    }

    [Theory]
    [InlineData("high", "All", "All", "", 1)]
    [InlineData("All", "possible", "All", "", 1)]
    [InlineData("All", "All", "authorization", "", 2)]
    [InlineData("All", "All", "All", "SOURCE/ONE", 2)]
    [InlineData("All", "All", "All", "ownership", 1)]
    [InlineData("medium", "probable", "data", "two", 1)]
    [InlineData("high", "possible", "All", "", 0)]
    public void Filters_are_presentation_only(string severity, string certainty, string category, string search, int count)
    {
        var record = Record();
        Assert.Equal(count, FindingFilter.Apply(record.Result.Findings, severity, certainty, category, search).Count);
        Assert.Equal(3, record.Result.Findings.Count);
    }

    [Fact]
    public void Location_preserves_absent_line_and_normalizes_separators()
    {
        var item = new FindingItem(Findings()[0]);
        Assert.Equal("source/one.cs:12", item.Location);
        Assert.Equal("source/one.cs", new FindingItem(item.Finding with { Line = null }).Location);
        Assert.Equal("Location unavailable", new FindingItem(item.Finding with { File = null }).Location);
        Assert.Equal("source/one.cs:12", new FindingItem(item.Finding with { File = "C:\\captured\\source\\one.cs" }, "C:/captured").Location);
        Assert.Equal("C:/other/one.cs:12", new FindingItem(item.Finding with { File = "C:/other/one.cs" }, "C:/captured").Location);
    }

    [Fact]
    public void Setup_reuses_only_current_profiles_and_valid_changed_paths()
    {
        var record = Record() with { Scope = ReviewScope.SelectedPaths, SelectedPaths = ["valid.cs", "gone.cs"], ProfileIds = ["security", "retired"] };
        var setup = ReusedReviewSetup.From(record, ["valid.cs", "new.cs"], true);
        Assert.Equal(ReviewScope.SelectedPaths, setup.Scope);
        Assert.Equal(new[] { "valid.cs" }, setup.SelectedPaths);
        Assert.Equal(new[] { "security" }, setup.ProfileIds);
        Assert.Contains("cleared", setup.Message);
        Assert.Equal(new[] { "valid.cs", "gone.cs" }, record.SelectedPaths);
        var none = ReusedReviewSetup.From(record, [], true);
        Assert.Empty(none.SelectedPaths);
        Assert.Contains("Choose changed paths", none.Message);
    }

    [Theory]
    [InlineData(true, ReviewScope.BranchVsBase)]
    [InlineData(false, ReviewScope.WorkingChanges)]
    public void Branch_setup_falls_back_when_current_base_is_unavailable(bool hasBase, ReviewScope expected)
    {
        Assert.Equal(expected, ReusedReviewSetup.From(Record() with { Scope = ReviewScope.BranchVsBase }, [], hasBase).Scope);
    }

    [Fact]
    public async Task View_model_reopens_filters_preserves_selection_and_clears_results_on_project_switch()
    {
        InitGit();
        var vm = new DeskViewModel(root);
        vm.RebuildProfileChoices(["standard"]);
        var project = new ProjectRegistration { Id = projectId, RepositoryPath = root };
        await vm.SaveProjectAsync(project, true);
        vm.ShowHistoryRecord(Record());
        Assert.True(vm.ShowHistory); Assert.True(vm.CanHandoffLatest);
        vm.SelectedFinding = vm.ReviewFindings[1];
        vm.SeverityFilter = "medium";
        Assert.Equal("2", vm.SelectedFinding?.Finding.Id);
        vm.FindingSearch = "no-match";
        Assert.True(vm.NoFilterMatches); Assert.Null(vm.SelectedFinding);
        vm.FindingSearch = "";
        Assert.NotNull(vm.SelectedFinding);
        var unrelated = Record() with { ProjectId = Guid.NewGuid() };
        vm.ShowHistoryRecord(unrelated);
        Assert.Equal(projectId, vm.LatestReview?.ProjectId);
        var otherRoot = Path.Combine(root, "other");
        Directory.CreateDirectory(otherRoot); InitGit(otherRoot);
        await vm.SaveProjectAsync(project with { Id = unrelated.ProjectId, RepositoryPath = otherRoot }, true);
        Assert.Null(vm.LatestReview); Assert.Empty(vm.ReviewFindings); Assert.Empty(vm.ReviewHistory);
        Assert.False(vm.CanHandoffLatest); Assert.False(vm.ReviewInputReady);
    }

    [Fact]
    public async Task View_model_reuse_clears_prepared_input_and_builds_fresh_snapshot_without_launch()
    {
        InitGit();
        var vm = new DeskViewModel(root);
        vm.RebuildProfileChoices(["standard"]);
        var project = new ProjectRegistration { Id = projectId, RepositoryPath = root };
        await vm.SaveProjectAsync(project, true);
        File.WriteAllText(Path.Combine(root, "current.cs"), "// current change");
        await vm.PreparePromptAsync(ReviewScope.WorkingChanges, []);
        Assert.True(vm.ReviewInputReady);
        vm.ShowHistoryRecord(Record() with { Scope = ReviewScope.SelectedPaths, SelectedPaths = ["current.cs", "gone.cs"] });
        var setup = await vm.UsePreviousSetupAsync();
        Assert.True(vm.ShowNewReview); Assert.False(vm.IsReviewRunning); Assert.False(vm.ReviewInputReady);
        Assert.Equal(new[] { "current.cs" }, setup!.SelectedPaths);
        var prompt = await vm.PreparePromptAsync(setup.Scope, setup.SelectedPaths);
        Assert.DoesNotContain("historical-head", prompt);
        Assert.Contains("// current change", prompt);
        Assert.Equal(new[] { "security" }, vm.SelectedProfileIds());
    }

    private void InitGit(string? repository = null)
    {
        var info = new ProcessStartInfo("git.exe") { WorkingDirectory = repository ?? root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("init"); info.ArgumentList.Add("-b"); info.ArgumentList.Add("main");
        using var process = Process.Start(info)!;
        process.WaitForExit(); Assert.Equal(0, process.ExitCode);
    }
    private async Task WriteRecord(string json)
    {
        Directory.CreateDirectory(Path.Combine(root, "Reviews"));
        await File.WriteAllTextAsync(Path.Combine(root, "Reviews", $"{projectId:N}-fixture.json"), json);
    }
    private ReviewRecord Record() => new()
    {
        SchemaVersion = 2, ProjectId = projectId, ProjectName = "Captured project", RepositoryPath = "C:/historical",
        Branch = "historical-branch", HeadSha = "historical-head", BaseRef = "old-base", ProfileIds = ["security"],
        ProfileNames = new Dictionary<string, string> { ["security"] = "Original security lens" },
        ProfileVersions = new Dictionary<string, string> { ["security"] = "old" }, Status = ReviewStatus.Completed,
        CopilotCliVersion = "1.0.91", Result = new ReviewResult { Summary = "Captured summary", Findings = Findings(), Limitations = ["Source review only"] }
    };
    private static ReviewFinding[] Findings() =>
    [
        new() { Id = "1", Severity = "high", Certainty = "confirmed", Category = "authorization", Title = "Ownership bypass", File = "source\\one.cs", Line = 12, Evidence = "Captured evidence", Impact = "Exposure", Recommendation = "Check ownership" },
        new() { Id = "2", Severity = "medium", Certainty = "probable", Category = "data", Title = "Data loss", File = "two.cs", Evidence = "Evidence", Impact = "Impact", Recommendation = "Recommendation" },
        new() { Id = "3", Severity = "medium", Certainty = "possible", Category = "authorization", Title = "Missing guard", File = "source/one.cs", Evidence = "Evidence", Impact = "Impact", Recommendation = "Recommendation" }
    ];
}
