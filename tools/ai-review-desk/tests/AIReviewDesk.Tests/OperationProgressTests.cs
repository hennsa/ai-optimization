using AIReviewDesk.App;
using AIReviewDesk.Core;
using AIReviewDesk.Infrastructure;
using Repo = AIReviewDesk.Tests.GitReviewContextTests.TemporaryGitRepository;

namespace AIReviewDesk.Tests;

public sealed class OperationProgressTests
{
    [Fact]
    public void Shared_modal_state_reports_actual_stages_and_cancels_once()
    {
        var cancelled = 0;
        var state = new OperationProgress("Review", "Fixture", "Scope", () => cancelled++);
        state.Report("Fingerprinting repository"); Assert.Equal("Fingerprinting repository", state.Stage);
        state.Cancel(); state.Cancel(); state.Report("Reviewing");
        Assert.Equal(1, cancelled); Assert.False(state.CanCancel); Assert.True(state.CancellationRequested);
        Assert.Equal("Cancelling and cleaning up…", state.Stage);
        state.Complete("Cancelled", "Partial findings discarded");
        Assert.True(state.Finished); Assert.False(state.ShouldTransition); Assert.Equal("_Close", state.ActionLabel);
    }

    [Theory]
    [InlineData(ReviewStatus.Completed, true)]
    [InlineData(ReviewStatus.Failed, false)]
    [InlineData(ReviewStatus.Stale, false)]
    [InlineData(ReviewStatus.Unsupported, false)]
    public async Task Review_modal_finishes_with_saved_result_and_only_success_transitions(ReviewStatus status, bool transition)
    {
        using var fixture = new OperationFixture();
        var vm = await fixture.ViewModelAsync();
        var reviewing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var validating = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var modal = new OperationProgress("Review", "Fixture", "Model / scope", vm.CancelReview, transitionOnSuccess: true);
        modal.PropertyChanged += (_, _) => { if (modal.Stage == "Reviewing") reviewing.TrySetResult(); if (modal.Stage == "Validating result") validating.TrySetResult(); };
        vm.ReviewRunner = async (input, _, progress, _, _) =>
        {
            progress?.Report("Reviewing");
            await reviewing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            progress?.Report("Validating result");
            await validating.Task.WaitAsync(TimeSpan.FromSeconds(5));
            return new ReviewRecord { ProjectId = input.Project.Id, Status = status, Diagnostic = status == ReviewStatus.Completed ? null : "Safe failure explanation", Result = new ReviewResult { Summary = "Synthetic accepted result" } };
        };
        await vm.StartReviewAsync(ReviewScope.WorkingChanges, [], modal);
        Assert.True(modal.Finished); Assert.Equal(status.ToString(), modal.Outcome); Assert.Equal(transition, modal.ShouldTransition);
        Assert.True(vm.ShowReviewDetail); Assert.Equal("_View result", modal.ActionLabel);
        Assert.Equal(status, Assert.Single(await new ReviewHistoryStore(fixture.Data).LoadAsync(fixture.Project.Id)).Status);
        if (!transition) Assert.Equal("Safe failure explanation", modal.Explanation);
    }

    [Fact]
    public async Task Review_modal_cancellation_reaches_runner_token_and_saves_cancelled_history()
    {
        using var fixture = new OperationFixture(); var vm = await fixture.ViewModelAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.ReviewRunner = async (_, _, _, ct, _) => { started.SetResult(); await Task.Delay(Timeout.Infinite, ct); throw new InvalidOperationException(); };
        var modal = new OperationProgress("Review", "Fixture", "Scope", vm.CancelReview, transitionOnSuccess: true);
        var task = vm.StartReviewAsync(ReviewScope.WorkingChanges, [], modal);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10)); modal.Cancel(); await task;
        Assert.Equal("Cancelled", modal.Outcome); Assert.False(modal.ShouldTransition); Assert.False(vm.IsReviewRunning);
        Assert.Equal(ReviewStatus.Cancelled, vm.LatestReview!.Status);
    }

    [Fact]
    public async Task Certification_modal_uses_real_engine_stages_stays_open_and_refreshes_local_result()
    {
        using var fixture = new OperationFixture(); var vm = await fixture.ViewModelAsync();
        var baseline = CertificationContract.BundledModels.First(c => c.ModelId == "claude-sonnet-5.5") with { ModelId = "synthetic-certification", DisplayName = "Synthetic certification", Probes = CertificationContract.RequiredProbes.Select(p => new CertificationProbe(p, true)).ToArray() };
        var model = new ModelCompatibility(new(baseline.ModelId, baseline.DisplayName, ["high"]), CertificationStatus.Unverified, null, null);
        vm.SetMetadata(new([model.Model], null, "Synthetic metadata"));
        var observedStage = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var modal = new OperationProgress("Compatibility", model.Model.Name, "High", vm.CancelCompatibility);
        modal.PropertyChanged += (_, _) => { if (modal.Stage == "Testing deliberate defect and manifest stability") observedStage.TrySetResult(); };
        vm.CertificationRunner = async (_, _, progress, _) =>
        {
            progress?.Report("Testing deliberate defect and manifest stability");
            await observedStage.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await new CertificationRegistry(fixture.Data).SaveAsync(baseline with { Source = "locally certified" });
            return baseline;
        };
        await vm.TestCompatibilityAsync(model, modal);
        Assert.Equal("Certified", modal.Outcome); Assert.True(modal.Finished); Assert.False(modal.ShouldTransition);
        Assert.Contains("tools:", modal.Explanation); Assert.Contains("output:", modal.Explanation);
        Assert.Equal(CertificationStatus.Testing, vm.CompatibilityModels.Single(m => m.Model.Id == baseline.ModelId).Status); // Settings waits for Close.
        vm.SetMetadata(new([model.Model], null, "Synthetic metadata")); // Same post-close local registry refresh used in production.
        Assert.Equal(CertificationStatus.Certified, vm.CompatibilityModels.Single(m => m.Model.Id == baseline.ModelId).Status);
    }

    [Fact]
    public async Task Certification_cancel_reaches_engine_and_does_not_auto_close_final_outcome()
    {
        using var fixture = new OperationFixture(); var vm = await fixture.ViewModelAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.CertificationRunner = async (id, _, _, ct) =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { }
            return new ModelCertificate { ModelId = id, DisplayName = "Fixture", Status = CertificationStatus.NeedsRetest, FailureReason = "Cancelled; partial evidence cannot authorize reviews." };
        };
        var modal = new OperationProgress("Compatibility", "Fixture", "High", vm.CancelCompatibility);
        var task = vm.TestCompatibilityAsync(new(new("fixture", "Fixture", ["high"]), CertificationStatus.Unverified, null, null), modal);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); modal.Cancel(); await task;
        Assert.Equal("Cancelled", modal.Outcome); Assert.False(modal.ShouldTransition); Assert.False(vm.IsCompatibilityTesting);
        Assert.Contains("partial evidence", modal.Explanation);
    }

    [Fact]
    public void Optional_stage_plan_records_observed_stages_and_final_status_without_fake_percentages()
    {
        var state = new OperationProgress("Review", "Fixture", "Scope", () => { }, stages: ["Preparing", "Reviewing", "Saving"]);
        state.Report("Preparing"); state.Report("Reviewing"); state.Complete("Failed", "Safe failure");
        Assert.Equal(new[] { "Done", "Failed", "Pending" }, state.Stages.Select(s => s.Status));
        state.Report("Saving"); Assert.Equal("Failed", state.Stage);
    }

    private sealed class OperationFixture : IDisposable
    {
        private readonly Repo repo = new();
        private ProjectRegistration? project;
        public ProjectRegistration Project => project ??= repo.Project;
        public string Data { get; } = Path.Combine(Path.GetTempPath(), "AIReviewDesk.OperationTests", Guid.NewGuid().ToString("N"));
        public async Task<DeskViewModel> ViewModelAsync()
        {
            repo.Write("change.txt", "synthetic change");
            var vm = new DeskViewModel(Data, projectInspector: _ => Task.FromResult(new RepositorySnapshot()), historyLoader: _ => Task.FromResult<IReadOnlyList<ReviewRecord>>([]), accountRefresher: () => Task.CompletedTask);
            await vm.InitializeAsync(new(new AppState { Projects = [Project], SelectedProjectId = Project.Id }, null));
            return vm;
        }
        public void Dispose() { repo.Dispose(); if (Directory.Exists(Data)) Directory.Delete(Data, true); }
    }
}
