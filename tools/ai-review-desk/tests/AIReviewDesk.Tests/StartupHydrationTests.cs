using System.Diagnostics;
using AIReviewDesk.App;
using AIReviewDesk.Core;
using AIReviewDesk.Infrastructure;
using Repo = AIReviewDesk.Tests.GitReviewContextTests.TemporaryGitRepository;
using Xunit.Abstractions;

namespace AIReviewDesk.Tests;

public sealed class StartupHydrationTests
{
    private readonly ITestOutputHelper output;
    public StartupHydrationTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public async Task Shell_is_interactive_while_project_and_account_hydration_are_pending_and_stale_results_are_ignored()
    {
        using var directory = new TemporaryDirectory();
        var first = new ProjectRegistration { DisplayName = "First", RepositoryPath = Path.Combine(directory.Path, "first") };
        var second = new ProjectRegistration { DisplayName = "Second", RepositoryPath = Path.Combine(directory.Path, "second") };
        var snapshots = new Dictionary<Guid, TaskCompletionSource<RepositorySnapshot>>
        {
            [first.Id] = NewSource<RepositorySnapshot>(),
            [second.Id] = NewSource<RepositorySnapshot>()
        };
        var histories = new Dictionary<Guid, TaskCompletionSource<IReadOnlyList<ReviewRecord>>>
        {
            [first.Id] = NewSource<IReadOnlyList<ReviewRecord>>(),
            [second.Id] = NewSource<IReadOnlyList<ReviewRecord>>()
        };
        var secondGitStarted = NewSource<bool>();
        var secondHistoryStarted = NewSource<bool>();
        var account = NewSource<bool>();
        var vm = new DeskViewModel(
            directory.Path,
            projectInspector: project =>
            {
                if (project.Id == second.Id) secondGitStarted.TrySetResult(true);
                return snapshots[project.Id].Task;
            },
            historyLoader: projectId =>
            {
                if (projectId == second.Id) secondHistoryStarted.TrySetResult(true);
                return histories[projectId].Task;
            },
            accountRefresher: async () => { await account.Task; });

        await new RegistryStore(directory.Path).SaveAsync(new AppState { Projects = [first, second], SelectedProjectId = first.Id });
        var shellTimer = Stopwatch.StartNew();
        await vm.InitializeAsync();
        output.WriteLine($"Synthetic settings-to-interactive shell: {shellTimer.Elapsed.TotalMilliseconds:F1} ms; Git, history, and account providers remained gated.");

        Assert.True(vm.Interactive);
        Assert.True(vm.RepositoryLoading);
        Assert.True(vm.HistoryLoading);
        Assert.Equal("Checking local repository state…", vm.RepositoryStatus);
        Assert.Equal("Loading review history…", vm.HistoryStatus);
        Assert.Contains("Inspecting", vm.RepositoryDetail);
        Assert.DoesNotContain("try again", vm.RepositoryDetail);
        Assert.DoesNotContain("retry", vm.ModelMetadataStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Loading", vm.ModelMetadataStatus);
        Assert.Contains("Loading", vm.CopilotUsage);

        var selection = vm.SelectAsync(second);
        await Task.WhenAll(secondGitStarted.Task, secondHistoryStarted.Task);
        snapshots[first.Id].SetResult(new RepositorySnapshot { RootPath = first.RepositoryPath, Branch = "stale-first" });
        histories[first.Id].SetResult([new ReviewRecord { ProjectId = first.Id, ProjectName = first.DisplayName, Status = ReviewStatus.Completed }]);
        await Task.Yield();

        Assert.Equal(second.Id, vm.Selected!.Id);
        Assert.Null(vm.Snapshot);
        Assert.Empty(vm.ReviewHistory);
        Assert.True(vm.Interactive);
        Assert.True(vm.RepositoryLoading);
        Assert.True(vm.HistoryLoading);

        snapshots[second.Id].SetResult(new RepositorySnapshot { RootPath = second.RepositoryPath, Branch = "current-second" });
        histories[second.Id].SetResult([]);
        await WaitForHydrationAsync(vm);

        Assert.Equal(second.RepositoryPath, vm.Snapshot!.RootPath);
        Assert.Empty(vm.ReviewHistory);
        Assert.False(vm.RepositoryLoading);
        Assert.False(vm.HistoryLoading);
        await selection;
        account.SetResult(true);
    }

    [Fact]
    public async Task Failed_repository_and_unavailable_metadata_keep_truthful_retry_explanations()
    {
        using var directory = new TemporaryDirectory();
        var project = new ProjectRegistration { RepositoryPath = directory.Path };
        var failed = NewSource<RepositorySnapshot>();
        var vm = new DeskViewModel(directory.Path, projectInspector: _ => failed.Task,
            historyLoader: _ => Task.FromResult<IReadOnlyList<ReviewRecord>>([]), accountRefresher: () => Task.CompletedTask);
        await vm.InitializeAsync(new RegistryLoadResult(new AppState { Projects = [project], SelectedProjectId = project.Id }, null));
        failed.SetException(new IOException("synthetic inspection failure"));
        await WaitForHydrationAsync(vm);
        Assert.Contains("could not be loaded", vm.RepositoryStatus); Assert.Contains("try again", vm.RepositoryDetail); Assert.NotEmpty(vm.Error);
        vm.SetMetadata(CopilotMetadata.Unavailable);
        Assert.Contains("unavailable", vm.ModelMetadataStatus); Assert.Contains("try again", vm.ModelMetadataStatus);
    }

    [Fact]
    public async Task Activation_refresh_does_not_replace_the_view_during_a_new_user_operation()
    {
        using var directory = new TemporaryDirectory();
        var project = new ProjectRegistration { DisplayName = "Synthetic", RepositoryPath = Path.Combine(directory.Path, "repo") };
        var activation = NewSource<RepositorySnapshot>();
        var inspectionCount = 0;
        var vm = new DeskViewModel(
            directory.Path,
            projectInspector: _ => Interlocked.Increment(ref inspectionCount) == 1
                ? Task.FromResult(new RepositorySnapshot { RootPath = project.RepositoryPath, Branch = "initial" })
                : activation.Task,
            historyLoader: _ => Task.FromResult<IReadOnlyList<ReviewRecord>>([]),
            accountRefresher: () => Task.CompletedTask);

        await vm.InitializeAsync(new RegistryLoadResult(new AppState { Projects = [project], SelectedProjectId = project.Id }, null));
        await WaitForHydrationAsync(vm);
        var currentSnapshot = vm.Snapshot;
        var currentRefreshed = vm.Refreshed;
        var activationTask = vm.RefreshOnActivateAsync();
        var operationGate = NewSource<bool>();
        var userOperation = vm.ExecuteAsync(async () => { await operationGate.Task; });

        activation.SetResult(new RepositorySnapshot { RootPath = project.RepositoryPath, Branch = "activation-result" });
        await activationTask;

        Assert.Same(currentSnapshot, vm.Snapshot);
        Assert.Equal(currentRefreshed, vm.Refreshed);
        Assert.False(vm.RepositoryLoading);
        operationGate.SetResult(true);
        await userOperation;
    }

    [Fact]
    public async Task Activation_refresh_is_skipped_while_a_review_is_running()
    {
        using var repo = new Repo();
        repo.Write("changed.txt", "synthetic change");
        using var directory = new TemporaryDirectory();
        var project = new ProjectRegistration { DisplayName = "Synthetic", RepositoryPath = repo.Root };
        var reviewGate = NewSource<bool>();
        var runnerStarted = NewSource<bool>();
        var inspections = 0;
        var vm = new DeskViewModel(
            directory.Path,
            projectInspector: _ =>
            {
                Interlocked.Increment(ref inspections);
                return Task.FromResult(new RepositorySnapshot { RootPath = repo.Root, Branch = "current" });
            },
            historyLoader: _ => Task.FromResult<IReadOnlyList<ReviewRecord>>([]),
            accountRefresher: () => Task.CompletedTask);
        await vm.InitializeAsync(new RegistryLoadResult(
            new AppState { Projects = [project], SelectedProjectId = project.Id, RefreshOnActivate = true }, null));
        await WaitForHydrationAsync(vm);
        vm.ReviewRunner = async (input, _, _, _, _) =>
        {
            runnerStarted.TrySetResult(true);
            await reviewGate.Task;
            return new ReviewRecord { ProjectId = input.Project.Id, ProjectName = project.DisplayName, Status = ReviewStatus.Completed };
        };

        var review = vm.StartReviewAsync(ReviewScope.WorkingChanges, []);
        await runnerStarted.Task;
        var before = vm.Snapshot;
        await vm.RefreshOnActivateAsync();

        Assert.True(vm.IsReviewRunning);
        Assert.Equal(1, inspections);
        Assert.Same(before, vm.Snapshot);
        reviewGate.SetResult(true);
        await review;
    }

    [Fact]
    public async Task Concurrent_settings_and_selection_persistence_keeps_both_latest_values()
    {
        using var directory = new TemporaryDirectory();
        var first = new ProjectRegistration { DisplayName = "First", RepositoryPath = Path.Combine(directory.Path, "first") };
        var second = new ProjectRegistration { DisplayName = "Second", RepositoryPath = Path.Combine(directory.Path, "second") };
        var vm = new DeskViewModel(
            directory.Path,
            projectInspector: project => Task.FromResult(new RepositorySnapshot { RootPath = project.RepositoryPath }),
            historyLoader: _ => Task.FromResult<IReadOnlyList<ReviewRecord>>([]),
            accountRefresher: () => Task.CompletedTask);
        await vm.InitializeAsync(new RegistryLoadResult(new AppState { Projects = [first, second], SelectedProjectId = first.Id }, null));
        await WaitForHydrationAsync(vm);

        var selection = vm.SelectAsync(second);
        var settings = vm.SaveSettingsAsync("Dark", refresh: true);
        await Task.WhenAll(selection, settings);

        var saved = await new RegistryStore(directory.Path).LoadAsync();
        Assert.Equal(second.Id, saved.State.SelectedProjectId);
        Assert.Equal("Dark", saved.State.Theme);
        Assert.True(saved.State.RefreshOnActivate);
    }

    [Fact]
    public async Task Older_history_load_cannot_overwrite_a_later_load_for_the_same_project()
    {
        using var directory = new TemporaryDirectory();
        var project = new ProjectRegistration { DisplayName = "Synthetic", RepositoryPath = Path.Combine(directory.Path, "repo") };
        var historyLoads = Enumerable.Range(0, 2).Select(_ => NewSource<IReadOnlyList<ReviewRecord>>()).ToArray();
        var historyStarted = Enumerable.Range(0, 3).Select(_ => NewSource<bool>()).ToArray();
        var historyLoadIndex = 0;
        var vm = new DeskViewModel(
            directory.Path,
            projectInspector: _ => Task.FromResult(new RepositorySnapshot()),
            historyLoader: _ =>
            {
                var index = Interlocked.Increment(ref historyLoadIndex) - 1;
                historyStarted[index].TrySetResult(true);
                return index == 0 ? Task.FromResult<IReadOnlyList<ReviewRecord>>([]) : historyLoads[index - 1].Task;
            },
            accountRefresher: () => Task.CompletedTask);
        await vm.InitializeAsync(new RegistryLoadResult(new AppState { Projects = [project], SelectedProjectId = project.Id }, null));
        await historyStarted[0].Task;
        var older = vm.LoadHistoryAsync(project.Id);
        await historyStarted[1].Task;
        var newer = vm.LoadHistoryAsync(project.Id);
        await historyStarted[2].Task;
        var newest = new ReviewRecord { ProjectId = project.Id, ProjectName = project.DisplayName, Status = ReviewStatus.Completed };
        historyLoads[1].SetResult([newest]);
        await newer;
        historyLoads[0].SetResult([new ReviewRecord { ProjectId = project.Id, ProjectName = "stale", Status = ReviewStatus.Failed }]);
        await older;

        Assert.Single(vm.ReviewHistory);
        Assert.Same(newest, vm.ReviewHistory[0].Record);
        Assert.False(vm.HistoryLoading);
    }

    [Theory]
    [InlineData("Light", "Light")]
    [InlineData("Dark", "Dark")]
    [InlineData("System", "System")]
    [InlineData("missing", "System")]
    [InlineData("corrupt", "System")]
    public async Task Early_theme_load_uses_shared_registry_normalization_and_safe_fallback(string input, string expected)
    {
        using var directory = new TemporaryDirectory();
        var file = Path.Combine(directory.Path, "app-state.json");
        if (input == "corrupt") await File.WriteAllTextAsync(file, "{ broken");
        else if (input != "missing") await File.WriteAllTextAsync(file, $$"""{"SchemaVersion":1,"Theme":"{{input}}"}""");

        var loaded = await new RegistryStore(directory.Path).LoadAsync();

        Assert.Equal(expected, loaded.State.Theme);
        if (input == "corrupt") Assert.NotNull(loaded.Warning);
        if (input == "missing") Assert.Null(loaded.Warning);
    }

    private static TaskCompletionSource<T> NewSource<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task WaitForHydrationAsync(DeskViewModel vm)
    {
        for (var i = 0; i < 100 && (vm.RepositoryLoading || vm.HistoryLoading); i++)
            await Task.Delay(5);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AIReviewDesk.StartupTests", Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
}
