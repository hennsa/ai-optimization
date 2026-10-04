using AIReviewDesk.App;
using AIReviewDesk.Core;
using AIReviewDesk.Infrastructure;
using System.Globalization;
using Repo = AIReviewDesk.Tests.GitReviewContextTests.TemporaryGitRepository;

namespace AIReviewDesk.Tests;

public sealed class OperationProgressTests
{
    [Fact]
    public void Shared_modal_state_reports_actual_stages_and_cancels_once()
    {
        var cancelled = 0;
        var state = new OperationProgress("Review", "Fixture", "Scope", () => cancelled++);
        state.UpdateContext("Scope\nPrompt size: 1,500,000 characters"); Assert.Contains("1,500,000", state.Context);
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
        var refreshes = 0;
        vm.MetadataRefresher = () => { refreshes++; return Task.CompletedTask; };
        var reviewing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var validating = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var modal = new OperationProgress("Review", "Fixture", "Model / scope", vm.CancelReview, transitionOnSuccess: true);
        modal.PropertyChanged += (_, _) => { if (modal.Stage == "Reviewing") reviewing.TrySetResult(); if (modal.Stage == "Validating result") validating.TrySetResult(); };
        vm.ReviewRunner = async (input, _, progress, _, _, started) =>
        {
            started?.Invoke();
            progress?.Report("Reviewing");
            await reviewing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            progress?.Report("Validating result");
            await validating.Task.WaitAsync(TimeSpan.FromSeconds(5));
            return new ReviewRecord { ProjectId = input.Project.Id, Status = status, Diagnostic = status == ReviewStatus.Completed ? null : "Safe failure explanation", Result = new ReviewResult { Summary = "Synthetic accepted result" } };
        };
        await vm.StartReviewAsync(ReviewScope.WorkingChanges, [], modal);
        Assert.Equal(1, refreshes);
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
        var refreshes = 0; vm.MetadataRefresher = () => { refreshes++; return Task.CompletedTask; };
        vm.ReviewRunner = async (_, _, _, ct, _, modelStarted) => { modelStarted?.Invoke(); started.SetResult(); await Task.Delay(Timeout.Infinite, ct); throw new InvalidOperationException(); };
        var modal = new OperationProgress("Review", "Fixture", "Scope", vm.CancelReview, transitionOnSuccess: true);
        var task = vm.StartReviewAsync(ReviewScope.WorkingChanges, [], modal);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10)); modal.Cancel(); await task;
        Assert.Equal("Cancelled", modal.Outcome); Assert.False(modal.ShouldTransition); Assert.False(vm.IsReviewRunning);
        Assert.Equal(ReviewStatus.Cancelled, vm.LatestReview!.Status);
        Assert.Equal(1, refreshes);
    }

    [Fact]
    public async Task Prelaunch_validation_block_does_not_refresh_allowance()
    {
        using var fixture = new OperationFixture(); var vm = await fixture.ViewModelAsync();
        var refreshes = 0; vm.MetadataRefresher = () => { refreshes++; return Task.CompletedTask; };
        vm.ProfileChoices.Clear();
        await vm.StartReviewAsync(ReviewScope.WorkingChanges, []);
        Assert.Contains("at least one review profile", vm.Error);
        Assert.Equal(0, refreshes);
    }

    [Fact]
    public async Task Allowance_refresh_failure_cannot_change_a_completed_review()
    {
        using var fixture = new OperationFixture(); var vm = await fixture.ViewModelAsync();
        vm.MetadataRefresher = () => throw new IOException("Synthetic metadata failure");
        vm.ReviewRunner = (input, _, _, _, _, started) =>
        {
            started?.Invoke();
            return Task.FromResult(new ReviewRecord { ProjectId = input.Project.Id, Status = ReviewStatus.Completed, Result = new ReviewResult { Summary = "Valid result" } });
        };
        await vm.StartReviewAsync(ReviewScope.WorkingChanges, []);
        Assert.Equal(ReviewStatus.Completed, vm.LatestReview!.Status);
        Assert.Equal("Valid result", vm.LatestReview.Result.Summary);
        Assert.Equal(ReviewStatus.Completed, Assert.Single(await new ReviewHistoryStore(fixture.Data).LoadAsync(fixture.Project.Id)).Status);
        Assert.Empty(vm.Error);
        Assert.Equal("Copilot · Usage unavailable", vm.CopilotAllowanceCompact);
    }

    [Fact]
    public async Task Sign_in_superseding_post_review_refresh_loads_new_metadata_and_discards_old_result()
    {
        using var fixture = new OperationFixture(); var vm = await fixture.ViewModelAsync();
        var currentQuota = new CopilotQuota("AI credits", 100, 20, 80, false, DateTimeOffset.UtcNow);
        vm.SetMetadata(new(CopilotModelPolicy.Verified, currentQuota, ""));
        vm.MetadataRefresher = vm.RefreshAccountAsync;
        var oldMetadata = NewSource<CopilotMetadata>();
        var accountCalls = 0;
        var metadataCalls = 0;
        var oldMetadataStarted = NewSource<bool>();
        vm.AccountStatusLoader = () => Interlocked.Increment(ref accountCalls) == 1
            ? Task.FromResult(new CopilotAccountState(true, true, false, "old identity", "1.0.91", "ok", SavedAccountConfigured: true))
            : Task.FromResult(new CopilotAccountState(true, true, false, "signed-in", "1.0.91", "ok", SavedAccountConfigured: true));
        var newQuota = new CopilotQuota("AI credits", 200, 25, 87.5m, false, DateTimeOffset.UtcNow);
        vm.AccountMetadataLoader = () => Interlocked.Increment(ref metadataCalls) == 1
            ? Start(oldMetadataStarted, oldMetadata)
            : Task.FromResult(new CopilotMetadata(CopilotModelPolicy.Verified, newQuota, "new identity metadata"));
        vm.SignInOperation = () => Task.CompletedTask;
        var propertyChanges = new List<string?>();
        vm.PropertyChanged += (_, args) => propertyChanges.Add(args.PropertyName);

        var postReviewRefresh = vm.MetadataRefresher();
        Assert.Equal("Copilot · Checking…", vm.CopilotAllowanceCompact);
        await oldMetadataStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await vm.SignInAsync();
        Assert.Equal("Copilot · 175 / 200 AI credits remaining", vm.CopilotAllowanceCompact);
        Assert.Equal("Sign-in completed", vm.AccountStatus);

        oldMetadata.SetResult(new(CopilotModelPolicy.Verified, currentQuota with { Used = 95 }, "stale metadata"));
        await postReviewRefresh;

        Assert.Equal("Copilot · 175 / 200 AI credits remaining", vm.CopilotAllowanceCompact);
        Assert.Equal("Sign-in completed", vm.AccountStatus);
        Assert.Contains(nameof(vm.CopilotAllowanceCompact), propertyChanges);
        Assert.DoesNotContain("Checking", vm.CopilotAllowanceCompact);

        static Task<CopilotMetadata> Start(TaskCompletionSource<bool> started, TaskCompletionSource<CopilotMetadata> result)
        {
            started.TrySetResult(true);
            return result.Task;
        }
    }

    [Fact]
    public async Task Stale_refresh_cleanup_cannot_clear_a_newer_refresh_loading_state()
    {
        using var fixture = new OperationFixture(); var vm = await fixture.ViewModelAsync();
        var newAccount = NewSource<CopilotAccountState>();
        var oldMetadata = NewSource<CopilotMetadata>();
        var newMetadata = NewSource<CopilotMetadata>();
        var oldMetadataStarted = NewSource<bool>();
        var newMetadataStarted = NewSource<bool>();
        var accountCalls = 0;
        var metadataCalls = 0;
        vm.AccountStatusLoader = () => Interlocked.Increment(ref accountCalls) switch
        {
            1 => Task.FromResult(new CopilotAccountState(true, true, false, "old", "1.0.91", "ok", SavedAccountConfigured: true)),
            2 => Task.FromResult(new CopilotAccountState(true, true, false, "signed-in", "1.0.91", "ok", SavedAccountConfigured: true)),
            _ => newAccount.Task
        };
        vm.AccountMetadataLoader = () => Interlocked.Increment(ref metadataCalls) switch
        {
            1 => Start(oldMetadataStarted, oldMetadata),
            2 => Task.FromResult(new CopilotMetadata(CopilotModelPolicy.Verified, new CopilotQuota("AI credits", 100, 10, 90, false, DateTimeOffset.UtcNow), "sign-in")),
            _ => Start(newMetadataStarted, newMetadata)
        };
        vm.SignInOperation = () => Task.CompletedTask;

        var obsoleteRefresh = vm.RefreshAccountAsync();
        await oldMetadataStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await vm.SignInAsync(); // Invalidates the first refresh and clears its loading state.
        Assert.DoesNotContain("Checking", vm.CopilotAllowanceCompact);

        var newerRefresh = vm.RefreshAccountAsync();
        Assert.Equal("Copilot · Checking…", vm.CopilotAllowanceCompact);
        oldMetadata.SetResult(new(CopilotModelPolicy.Verified, new CopilotQuota("AI credits", 100, 99, 1, false, DateTimeOffset.UtcNow), "stale"));
        await obsoleteRefresh;
        Assert.Equal("Copilot · Checking…", vm.CopilotAllowanceCompact);

        newAccount.SetResult(new CopilotAccountState(true, true, false, "new", "1.0.91", "ok", SavedAccountConfigured: true));
        await newMetadataStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var newQuota = new CopilotQuota("AI credits", 1500, 693, 53.8m, false, DateTimeOffset.UtcNow);
        newMetadata.SetResult(new(CopilotModelPolicy.Verified, newQuota, ""));
        await newerRefresh;

        Assert.Equal($"Copilot · {807.ToString("N0", CultureInfo.CurrentCulture)} / {1500.ToString("N0", CultureInfo.CurrentCulture)} AI credits remaining", vm.CopilotAllowanceCompact);
        Assert.DoesNotContain("Checking", vm.CopilotAllowanceCompact);

        static Task<CopilotMetadata> Start(TaskCompletionSource<bool> started, TaskCompletionSource<CopilotMetadata> result)
        {
            started.TrySetResult(true);
            return result.Task;
        }
    }

    [Fact]
    public async Task Sign_out_clears_previous_metadata_and_does_not_fetch_metadata()
    {
        using var fixture = new OperationFixture(); var vm = await fixture.ViewModelAsync();
        var oldAccount = NewSource<CopilotAccountState>();
        var accountCalls = 0;
        vm.AccountStatusLoader = () => Interlocked.Increment(ref accountCalls) == 1
            ? oldAccount.Task
            : Task.FromResult(new CopilotAccountState(true, true, false, null, "1.0.91", "signed out", SavedAccountConfigured: false));
        var metadataCalls = 0;
        vm.AccountMetadataLoader = () => { metadataCalls++; return Task.FromResult(CopilotMetadata.Unavailable); };
        vm.SignOutOperation = () => Task.CompletedTask;
        vm.SetMetadata(new(CopilotModelPolicy.Verified, new CopilotQuota("AI credits", 50, 10, 80, false, DateTimeOffset.UtcNow), "old identity"));

        var supersededRefresh = vm.RefreshAccountAsync();
        Assert.Equal("Copilot · Checking…", vm.CopilotAllowanceCompact);
        await vm.SignOutAsync();

        Assert.Equal("Copilot · Usage unavailable", vm.CopilotAllowanceCompact);
        Assert.Equal("Live sign-in status unavailable", vm.AccountStatus);
        Assert.DoesNotContain(vm.Models, m => m.Id != "auto");
        Assert.Equal(0, metadataCalls);
        oldAccount.SetResult(new CopilotAccountState(true, true, false, "stale", "1.0.91", "ok", SavedAccountConfigured: true));
        await supersededRefresh;
        Assert.Equal("Copilot · Usage unavailable", vm.CopilotAllowanceCompact);
        Assert.DoesNotContain("Checking", vm.CopilotAllowanceCompact);
    }

    [Fact]
    public async Task Switch_account_clears_old_metadata_and_uses_only_resulting_identity_metadata()
    {
        using var fixture = new OperationFixture(); var vm = await fixture.ViewModelAsync();
        vm.SetMetadata(new(CopilotModelPolicy.Verified, new CopilotQuota("AI credits", 50, 10, 80, false, DateTimeOffset.UtcNow), "old identity"));
        var signInGate = NewSource<bool>();
        vm.SignOutOperation = () => Task.CompletedTask;
        vm.SignInOperation = async () => { await signInGate.Task; };
        var accountCalls = 0;
        vm.AccountStatusLoader = () => Task.FromResult(Interlocked.Increment(ref accountCalls) == 1
            ? new CopilotAccountState(true, true, false, null, "1.0.91", "signed out")
            : new CopilotAccountState(true, true, false, "new identity", "1.0.91", "ok", SavedAccountConfigured: true));
        var newQuota = new CopilotQuota("Premium requests", 150, 27, 82, false, DateTimeOffset.UtcNow);
        vm.AccountMetadataLoader = () => Task.FromResult(new CopilotMetadata(CopilotModelPolicy.Verified, newQuota, "new identity metadata"));

        var switching = vm.SwitchAccountAsync();
        Assert.Equal("Copilot · Usage unavailable", vm.CopilotAllowanceCompact);
        Assert.Equal("Live sign-in status unavailable", vm.AccountStatus);
        signInGate.SetResult(true);
        await switching;

        Assert.Equal("Sign-in completed", vm.AccountStatus);
        Assert.Equal("Copilot · 123 / 150 requests remaining", vm.CopilotAllowanceCompact);
    }

    [Fact]
    public async Task Sign_in_metadata_failure_keeps_authenticated_account_and_clears_stale_allowance()
    {
        using var fixture = new OperationFixture(); var vm = await fixture.ViewModelAsync();
        vm.SetMetadata(new(CopilotModelPolicy.Verified, new CopilotQuota("AI credits", 50, 10, 80, false, DateTimeOffset.UtcNow), "old identity"));
        vm.SignInOperation = () => Task.CompletedTask;
        vm.AccountStatusLoader = () => Task.FromResult(new CopilotAccountState(true, true, false, "signed-in identity", "1.0.91", "ok", SavedAccountConfigured: true));
        vm.AccountMetadataLoader = () => throw new IOException("Synthetic metadata failure");

        await vm.SignInAsync();

        Assert.Equal("Sign-in completed", vm.AccountStatus);
        Assert.Equal("Copilot · Usage unavailable", vm.CopilotAllowanceCompact);
        Assert.Contains("unavailable", vm.ModelMetadataStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(vm.Error);
        Assert.False(vm.CopilotAllowanceCompact.Contains("Checking", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Certification_modal_uses_real_engine_stages_stays_open_and_refreshes_local_result()
    {
        using var fixture = new OperationFixture(); var vm = await fixture.ViewModelAsync();
        var baseline = CertificationContract.BundledModels.First(c => c.ModelId == "claude-sonnet-5.5") with { ModelId = "synthetic-certification", DisplayName = "Synthetic certification", Probes = CertificationContract.RequiredProbes.Select(p => new CertificationProbe(p, true)).ToArray() };
        var model = new ModelCompatibility(new(baseline.ModelId, baseline.DisplayName, ["high"]), CertificationStatus.Unverified, null, null);
        vm.MetadataRefresher = () =>
        {
            vm.SetMetadata(new([model.Model], new CopilotQuota("AI credits", 10, 2, 80, false, DateTimeOffset.UtcNow), "Synthetic metadata"));
            return Task.CompletedTask;
        };
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
        await vm.RefreshCompatibilityMetadataAsync();
        Assert.Equal(CertificationStatus.Certified, vm.CompatibilityModels.Single(m => m.Model.Id == baseline.ModelId).Status);
        Assert.Equal("Copilot · 8 / 10 AI credits remaining", vm.CopilotAllowanceCompact);
    }

    [Fact]
    public async Task Large_compatibility_refresh_updates_the_header_allowance()
    {
        using var fixture = new OperationFixture(); var vm = await fixture.ViewModelAsync();
        var quota = new CopilotQuota("Premium requests", 300, 42, 86, false, DateTimeOffset.UtcNow);
        vm.MetadataRefresher = () => { vm.SetMetadata(new(CopilotModelPolicy.Verified, quota, "")); return Task.CompletedTask; };
        var certificate = new ModelCertificate { ModelId = "fixture", DisplayName = "Fixture", Status = CertificationStatus.Certified, ReasoningEfforts = ["high"] };
        var model = new ModelCompatibility(new("fixture", "Fixture", ["high"]), CertificationStatus.Certified, certificate, null);
        vm.LargeCertificationRunner = (id, _, _, _) => Task.FromResult(new ModelCertificate { ModelId = id, DisplayName = "Fixture", LargeContext = new LargeContextCertificate { Status = CertificationStatus.Rejected } });
        await vm.TestLargeContextCompatibilityAsync(model);
        Assert.Equal("Copilot · Checking…", vm.CopilotAllowanceCompact);
        await vm.RefreshCompatibilityMetadataAsync();
        Assert.Contains("86% remaining", vm.CopilotAllowanceTooltip);
        Assert.Equal("Copilot · 258 / 300 requests remaining", vm.CopilotAllowanceCompact);
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
            vm.MetadataRefresher = () => Task.CompletedTask;
            return vm;
        }
        public void Dispose() { repo.Dispose(); if (Directory.Exists(Data)) Directory.Delete(Data, true); }
    }

    private static TaskCompletionSource<T> NewSource<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
