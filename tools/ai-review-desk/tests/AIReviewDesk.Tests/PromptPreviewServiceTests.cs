using System.Collections.Concurrent;
using AIReviewDesk.App;
using AIReviewDesk.Core;
using AIReviewDesk.Infrastructure;

namespace AIReviewDesk.Tests;

public sealed class PromptPreviewServiceTests
{
    private static TaskCompletionSource<T> Gate<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static ReviewInput Input(ProjectRegistration project) => new() { Project = project, IsPreview = true, HasReviewableChanges = true, Context = "synthetic change", Snapshot = new() { RootPath = project.RepositoryPath } };
    [Fact]
    public async Task Worker_copies_inputs_runs_without_UI_context_and_uses_exact_production_composer()
    {
        await OnUI(async () =>
        {
            var uiThread = Environment.CurrentManagedThreadId;
            var entered = Gate<bool>(); var release = Gate<bool>();
            var paths = new List<string> { "a.txt" }; var profiles = new List<string> { "standard" };
            var defaults = new List<string> { "standard" };
            var project = new ProjectRegistration { DefaultProfileIds = defaults };
            var service = new PromptPreviewService(async (request, progress, ct) =>
            {
                Assert.Null(SynchronizationContext.Current); Assert.NotEqual(uiThread, Environment.CurrentManagedThreadId);
                entered.SetResult(true); await release.Task;
                Assert.Equal(new[] { "a.txt" }, request.Paths); Assert.Equal(new[] { "standard" }, request.Profiles);
                Assert.Equal(new[] { "standard" }, request.Project.DefaultProfileIds);
                return Input(request.Project);
            });
            var preparation = service.PrepareAsync(new(project, ReviewScope.SelectedPaths, paths, profiles));
            await entered.Task; // UI pump continues while worker is blocked.
            Assert.Equal(uiThread, Environment.CurrentManagedThreadId);
            paths.Clear(); profiles.Clear(); defaults.Clear(); release.SetResult(true);
            var prepared = await preparation;
            Assert.Equal(PromptComposer.Compose(prepared.Input, ["standard"]), prepared.Prompt);
            Assert.Equal(prepared.Prompt.Length, prepared.Size!.CharacterCount);
            Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(prepared.Prompt), prepared.Size.Utf8ByteCount);
            Assert.True(prepared.Input.IsPreview); Assert.Empty(prepared.Input.Fingerprint);
        });
    }
    [Theory, InlineData(false), InlineData(true)]
    public async Task Exceptions_and_cancellation_propagate_without_a_result(bool cancel)
    {
        using var cts = new CancellationTokenSource();
        var service = new PromptPreviewService((_, _, ct) =>
        {
            if (!cancel) throw new IOException("synthetic failure");
            cts.Cancel(); ct.ThrowIfCancellationRequested(); return Task.FromResult(Input(new()));
        });
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PrepareAsync(new(new(), ReviewScope.WorkingChanges, [], ["standard"]), ct: cts.Token));
        else await Assert.ThrowsAsync<IOException>(() => service.PrepareAsync(new(new(), ReviewScope.WorkingChanges, [], ["standard"])));
    }
    [Theory, InlineData(false), InlineData(true)]
    public async Task VM_applies_result_and_progress_on_UI_and_discards_changed_selection(bool invalidate)
    {
        var root = Path.Combine(Path.GetTempPath(), "ARD-PreviewTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            await OnUI(async () =>
            {
                var uiThread = Environment.CurrentManagedThreadId;
                var project = new ProjectRegistration { RepositoryPath = root };
                var vm = new DeskViewModel(root, projectInspector: p => Task.FromResult(new RepositorySnapshot()), historyLoader: _ => Task.FromResult<IReadOnlyList<ReviewRecord>>([]), accountRefresher: () => Task.CompletedTask);
                await vm.InitializeAsync(new RegistryLoadResult(new AppState { Projects = [project], SelectedProjectId = project.Id }, null));
                vm.SelectedModelId = "auto";
                vm.RebuildProfileChoices(["standard"]);
                vm.PropertyChanged += (_, _) => Assert.Equal(uiThread, Environment.CurrentManagedThreadId);
                var entered = Gate<bool>(); var release = Gate<bool>();
                vm.PreviewPreparer = new PromptPreviewService(async (request, progress, ct) =>
                {
                    entered.SetResult(true); await release.Task; progress!.Report("late synthetic progress"); return Input(request.Project);
                }).PrepareAsync;
                var preparation = vm.PreparePromptAsync(ReviewScope.WorkingChanges, []);
                await entered.Task;
                if (invalidate) { await vm.SelectAsync(project with { Id = Guid.NewGuid(), DisplayName = "new selection" }); vm.SetReviewProgress("new selection"); }
                release.SetResult(true); var prompt = await preparation;
                await Task.Yield();
                if (invalidate) { Assert.Empty(prompt); Assert.Null(vm.PreparedInput); Assert.Equal("new selection", vm.ReviewProgress); }
                else { Assert.NotEmpty(prompt); Assert.True(vm.PreparedInput!.IsPreview); Assert.Equal(PromptComposer.Compose(vm.PreparedInput, ["standard"]), prompt); Assert.Contains("Prompt prepared", vm.ReviewProgress); }
            });
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Start_review_discards_preview_authority_and_fingerprints_fresh_repository_content()
    {
        using var repo = new GitReviewContextTests.TemporaryGitRepository();
        repo.Write("a.txt", "baseline"); repo.Commit("baseline"); repo.Write("a.txt", "preview version");
        var root = Path.Combine(Path.GetTempPath(), "ARD-PreviewExecution-" + Guid.NewGuid().ToString("N"));
        try
        {
            var vm = new DeskViewModel(root, projectInspector: p => new GitInspector().InspectAsync(p.RepositoryPath),
                historyLoader: _ => Task.FromResult<IReadOnlyList<ReviewRecord>>([]), accountRefresher: () => Task.CompletedTask);
            var project = repo.Project;
            await vm.InitializeAsync(new RegistryLoadResult(new AppState { Projects = [project], SelectedProjectId = project.Id }, null));
            vm.SelectedModelId = "auto"; vm.RebuildProfileChoices(["standard"]);
            await vm.PreparePromptAsync(ReviewScope.WorkingChanges, []);
            Assert.True(vm.PreparedInput!.IsPreview); Assert.Empty(vm.PreparedInput.Fingerprint);
            repo.Write("a.txt", "execution version");
            var fingerprint = await new GitReviewContext().FingerprintAsync(repo.Root);
            var called = false;
            vm.ReviewRunner = (input, _, _, _, _, _) =>
            {
                called = true; Assert.False(input.IsPreview); Assert.Equal(fingerprint, input.Fingerprint);
                Assert.Contains("execution version", input.Context);
                return Task.FromResult(new ReviewRecord { ProjectId = project.Id, Status = ReviewStatus.Cancelled });
            };
            await vm.StartReviewAsync(ReviewScope.WorkingChanges, []);
            Assert.True(called);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Theory, InlineData(false), InlineData(true)]
    public async Task VM_failure_and_cancellation_discard_late_progress_and_never_publish_input(bool cancel)
    {
        await OnUI(async () =>
        {
            var project = new ProjectRegistration();
            var vm = new DeskViewModel(projectInspector: _ => Task.FromResult(new RepositorySnapshot()), historyLoader: _ => Task.FromResult<IReadOnlyList<ReviewRecord>>([]), accountRefresher: () => Task.CompletedTask);
            await vm.InitializeAsync(new RegistryLoadResult(new AppState { Projects = [project], SelectedProjectId = project.Id }, null));
            vm.SelectedModelId = "auto"; vm.RebuildProfileChoices(["standard"]);
            vm.PreviewPreparer = (_, progress, _) =>
            {
                progress!.Report("queued progress");
                return Task.FromException<PreparedPromptPreview>(cancel ? new OperationCanceledException() : new IOException("synthetic failure"));
            };
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => vm.PreparePromptAsync(ReviewScope.WorkingChanges, []));
            else await Assert.ThrowsAsync<IOException>(() => vm.PreparePromptAsync(ReviewScope.WorkingChanges, []));
            vm.SetReviewProgress("operation ended"); await Task.Yield();
            Assert.Equal("operation ended", vm.ReviewProgress); Assert.Null(vm.PreparedInput);
        });
    }
    // A deterministic single-thread dispatcher seam; no sleeps or timing-based correctness assertions.
    private static Task OnUI(Func<Task> action)
    {
        var done = Gate<bool>();
        var thread = new Thread(() =>
        {
            using var context = new Pump(); SynchronizationContext.SetSynchronizationContext(context);
            context.Post(async _ => { try { await action(); done.TrySetResult(true); } catch (Exception ex) { done.TrySetException(ex); } finally { context.Finish(); } }, null);
            context.Run();
        }) { IsBackground = true };
        thread.Start(); return done.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
    private sealed class Pump : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback, object?)> queue = new();
        private readonly object gate = new();
        private bool closed;
        // Like a dispatcher that has shut down, ignore late hydration notifications after the test completes.
        public override void Post(SendOrPostCallback callback, object? state) { lock (gate) { if (!closed) queue.Add((callback, state)); } }
        public void Run() { foreach (var (callback, state) in queue.GetConsumingEnumerable()) callback(state); }
        public void Finish() { lock (gate) { closed = true; queue.CompleteAdding(); } }
        public void Dispose() { lock (gate) { closed = true; queue.Dispose(); } }
    }
}
