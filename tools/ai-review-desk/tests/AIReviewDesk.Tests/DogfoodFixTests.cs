using System.Xml.Linq;
using AIReviewDesk.App;
using AIReviewDesk.Core;
using AIReviewDesk.Infrastructure;
using Repo = AIReviewDesk.Tests.GitReviewContextTests.TemporaryGitRepository;

namespace AIReviewDesk.Tests;

public sealed class DogfoodFixTests
{
    [Theory]
    [InlineData(ReviewScope.WorkingChanges, "No working changes")]
    [InlineData(ReviewScope.BranchVsBase, "No branch changes")]
    public async Task Empty_preview_uses_production_composer_and_start_creates_no_history(ReviewScope scope, string context)
    {
        using var repo = new Repo();
        repo.Write("base.txt", "base"); repo.Commit("initial");
        var data = Path.Combine(Path.GetTempPath(), "AIReviewDesk.DogfoodTests", Guid.NewGuid().ToString("N"));
        try
        {
            var project = repo.Project with { DefaultBase = "main" };
            var vm = new DeskViewModel(data);
            await vm.SaveProjectAsync(project, true);
            vm.RebuildProfileChoices(["standard"]);
            var prompt = await vm.PreparePromptAsync(scope, []);
            Assert.Contains(context, prompt);
            Assert.Equal(PromptComposer.Compose(vm.PreparedInput!, vm.SelectedProfileIds()), prompt);
            foreach (var section in new[] { "Shared reviewer policy", "Review profile:", "Repository and project snapshot", "Review scope", "Structured output contract" }) Assert.Contains(section, prompt);
            Assert.False(vm.PreparedInput!.HasReviewableChanges);
            await vm.StartReviewAsync(scope, []);
            Assert.Null(vm.LatestReview);
            Assert.Empty(await new ReviewHistoryStore(data).LoadAsync(project.Id));
            Assert.Contains("no", vm.Error.ToLowerInvariant()); Assert.Contains("changes", vm.Error.ToLowerInvariant());
            await Assert.ThrowsAsync<ReviewValidationException>(() => new CopilotService(data).RunAsync(vm.PreparedInput!, ["standard"]));
        }
        finally { if (Directory.Exists(data)) Directory.Delete(data, true); }
    }

    [Fact]
    public async Task Empty_selected_paths_are_pre_run_validation()
    {
        using var repo = new Repo(); repo.Write("new.txt", "change");
        await Assert.ThrowsAsync<ReviewValidationException>(() => new GitReviewContext().PrepareAsync(repo.Project, ReviewScope.SelectedPaths, []));
    }

    [Fact]
    public async Task Fingerprint_catches_staged_deleted_renamed_refs_and_stat_hidden_content()
    {
        using var repo = new Repo(); repo.Write("base.txt", "aaaa"); repo.Commit("initial");
        var git = new GitReviewContext();
        var before = await git.FingerprintAsync(repo.Root);
        var time = File.GetLastWriteTimeUtc(Path.Combine(repo.Root, "base.txt"));
        repo.Git("update-index", "--assume-unchanged", "base.txt");
        before = await git.FingerprintAsync(repo.Root);
        repo.Write("base.txt", "bbbb"); File.SetLastWriteTimeUtc(Path.Combine(repo.Root, "base.txt"), time);
        Assert.NotEqual(before, await git.FingerprintAsync(repo.Root));
        repo.Git("update-index", "--no-assume-unchanged", "base.txt");
        repo.Write("base.txt", "staged longer content");
        before = await git.FingerprintAsync(repo.Root); repo.Git("add", "base.txt");
        Assert.NotEqual(before, await git.FingerprintAsync(repo.Root));
        before = await git.FingerprintAsync(repo.Root); repo.Git("mv", "base.txt", "renamed.txt");
        Assert.NotEqual(before, await git.FingerprintAsync(repo.Root));
        before = await git.FingerprintAsync(repo.Root); File.Delete(Path.Combine(repo.Root, "renamed.txt"));
        Assert.NotEqual(before, await git.FingerprintAsync(repo.Root));
        before = await git.FingerprintAsync(repo.Root); repo.Git("branch", "another-ref");
        Assert.NotEqual(before, await git.FingerprintAsync(repo.Root));
        before = await git.FingerprintAsync(repo.Root); repo.Write("new.txt", "one");
        Assert.NotEqual(before, await git.FingerprintAsync(repo.Root));
        before = await git.FingerprintAsync(repo.Root); repo.Write("new.txt", "two");
        Assert.NotEqual(before, await git.FingerprintAsync(repo.Root));
    }

    [Fact]
    public async Task Thousands_of_untracked_paths_exceed_Windows_command_line_but_prepare_successfully()
    {
        using var repo = new Repo(); repo.Write("tracked.txt", "base"); repo.Write("second.txt", "base"); repo.Commit("initial");
        repo.Write("tracked.txt", "changed"); repo.Write("second.txt", "changed");
        for (var i = 0; i < 2710; i++) repo.Write($"generated/long-directory-name/file-{i:D4}.txt", "small fixture\n");
        var stages = new List<string>();
        using var bounded = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var input = await new GitReviewContext().PrepareAsync(repo.Project, ReviewScope.WorkingChanges, ct: bounded.Token, progress: new ImmediateProgress(stages));
        Assert.True(input.Snapshot.ChangedPaths.Sum(p => p.Length) > 32_767);
        Assert.Equal(2, input.Snapshot.TrackedChangedCount); Assert.Equal(2710, input.Snapshot.UntrackedCount);
        Assert.Contains("Staged", input.Context, StringComparison.OrdinalIgnoreCase); // Unstaged section includes this suffix.
        Assert.Contains("Building review context", stages); Assert.Contains("Fingerprinting repository", stages);
        Assert.Equal(input.Fingerprint, await new GitReviewContext().FingerprintAsync(repo.Root, bounded.Token));
    }

    private sealed class ImmediateProgress(List<string> stages) : IProgress<string> { public void Report(string value) => stages.Add(value); }

    [Fact]
    public async Task Preparation_detects_changes_during_context_build_and_honors_cancellation()
    {
        using var repo = new Repo(); repo.Write("new.txt", "before");
        var context = new GitReviewContext();
        var error = await Assert.ThrowsAsync<PreparationException>(() => context.PrepareAsync(repo.Project, ReviewScope.WorkingChanges,
            progress: new CallbackProgress(stage => { if (stage == "Building review context") repo.Write("new.txt", "after"); })));
        Assert.Equal(PreparationFailure.RepositoryChanged, error.Reason);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.PrepareAsync(repo.Project, ReviewScope.WorkingChanges,
            ct: cancellation.Token, progress: new CallbackProgress(_ => cancellation.Cancel())));
    }

    [Fact]
    public async Task Selected_scope_never_expands_to_other_tracked_changes()
    {
        using var repo = new Repo(); repo.Write("one.txt", "base"); repo.Write("other.txt", "base"); repo.Commit("initial");
        repo.Write("one.txt", "selected change"); repo.Write("other.txt", "MUST_NOT_INCLUDE");
        var input = await new GitReviewContext().PrepareAsync(repo.Project, ReviewScope.SelectedPaths, ["one.txt"]);
        Assert.Contains("selected change", input.Context); Assert.DoesNotContain("MUST_NOT_INCLUDE", input.Context);
    }

    private sealed class CallbackProgress(Action<string> callback) : IProgress<string> { public void Report(string value) => callback(value); }

    [Fact]
    public void Git_locator_rejects_relative_and_repository_local_candidates_and_uses_absolute_installation()
    {
        using var repo = new Repo(); repo.Write("git.exe", "MZmalicious");
        repo.Write("fake/cmd/git.exe", "MZfake"); repo.Write("fake/mingw64/bin/git.exe", "MZfake"); Directory.CreateDirectory(Path.Combine(repo.Root, "fake/etc"));
        var selected = GitExecutableLocator.Resolve(["git.exe", Path.Combine(repo.Root, "git.exe"), Path.Combine(repo.Root, "fake/cmd/git.exe"), GitExecutableLocator.Path]);
        Assert.Equal(GitExecutableLocator.Path, selected); Assert.True(Path.IsPathFullyQualified(selected));
        Assert.Throws<PreparationException>(() => GitExecutableLocator.Resolve(["git.exe", Path.Combine(repo.Root, "fake/cmd/git.exe")]));
    }

    [Fact]
    public async Task Inspector_and_context_use_same_Git_despite_repository_local_executable()
    {
        using var repo = new Repo(); repo.Write("git.exe", "MZinvalid executable"); repo.Commit("initial"); repo.Write("change.txt", "change");
        var overview = await new GitInspector().InspectAsync(repo.Root);
        var preview = await new GitReviewContext().PrepareAsync(repo.Project, ReviewScope.WorkingChanges, preview: true);
        Assert.Equal(overview.HeadSha, preview.Snapshot.HeadSha); Assert.Equal(overview.UntrackedCount, preview.Snapshot.UntrackedCount);
    }

    [Theory]
    [InlineData(PreparationFailure.GitUnavailable, "Git executable unavailable")]
    [InlineData(PreparationFailure.Conflicts, "conflicts")]
    [InlineData(PreparationFailure.ScopeTooLarge, "too large")]
    [InlineData(PreparationFailure.SensitiveFile, "sensitive file")]
    [InlineData(PreparationFailure.Timeout, "time limit")]
    [InlineData(PreparationFailure.RepositoryChanged, "changed during preparation")]
    public void Known_preparation_diagnostics_are_safe_and_useful(PreparationFailure reason, string expected) =>
        Assert.Contains(expected, DeskViewModel.SafePreparationDiagnostic(new PreparationException(reason)));

    [Fact]
    public void Unknown_preparation_errors_never_persist_raw_output() =>
        Assert.DoesNotContain("SYNTHETIC_TOKEN", DeskViewModel.SafePreparationDiagnostic(new InvalidOperationException("SYNTHETIC_TOKEN arbitrary process output")));

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task History_preserves_legacy_counts_without_inventing_split(int schema)
    {
        var data = Path.Combine(Path.GetTempPath(), "AIReviewDesk.DogfoodTests", Guid.NewGuid().ToString("N"));
        var record = new ReviewRecord { SchemaVersion = schema, ProjectId = Guid.NewGuid(), Status = ReviewStatus.Failed, ChangedFileCount = 2712, TrackedChangedCount = 2, UntrackedCount = 2710 };
        try
        {
            var store = new ReviewHistoryStore(data); await store.SaveAsync(record);
            var loaded = Assert.Single(await store.LoadAsync(record.ProjectId));
            var detail = new ReviewDetails(loaded);
            if (schema == 3) { Assert.Equal(2, loaded.TrackedChangedCount); Assert.Contains("Untracked files: 2710", detail.SnapshotText); }
            else { Assert.Null(loaded.TrackedChangedCount); Assert.Null(loaded.UntrackedCount); Assert.Contains("Legacy combined", detail.SnapshotText); }
        }
        finally { Directory.Delete(data, true); }
    }

    [Fact]
    public void Successful_login_and_saved_metadata_never_claim_live_identity()
    {
        var state = new CopilotAccountState(true, true, false, null, "1.0.91", "Live status unavailable", SavedAccountConfigured: true);
        Assert.Equal("Saved Copilot account configured", state.DisplayStatus); Assert.Null(state.Identity); Assert.False(state.Authenticated);
        Assert.Equal("Sign-in completed", (state with { SignInCompleted = true }).DisplayStatus);
        var vm = new DeskViewModel(); vm.SetAccount(state with { SignInCompleted = true });
        Assert.Equal("Sign-in completed", vm.AccountStatus);
        Assert.Contains("dedicated Copilot profile was updated successfully", vm.AccountDetail);
        Assert.Contains("Authentication is verified when a review starts", vm.AccountDetail);
        Assert.Contains("Account identity unavailable", vm.AccountDetail);
        vm.SetAccount(state); Assert.Equal("Saved Copilot account configured", vm.AccountStatus);
        Assert.False(vm.CanSignOut); Assert.False(vm.CanSwitchAccount);
        Assert.Equal("Copilot setup is blocked", (state with { SignInCompleted = true, ConfigurationBlocked = true }).DisplayStatus);
        var metadata = CopilotConfigScanner.Inspect(System.Text.Encoding.UTF8.GetBytes("{\"loggedInUsers\":[]}"));
        Assert.False(metadata.CredentialFieldPresent); Assert.False(metadata.SavedAccountPresent);
        var saved = CopilotConfigScanner.Inspect(System.Text.Encoding.UTF8.GetBytes("{\"loggedInUsers\":[{\"host\":\"https://example.invalid\",\"login\":\"synthetic-user\"}]}"));
        Assert.True(saved.SavedAccountPresent);
    }

    [Fact]
    public async Task Project_tabs_belong_to_selection_and_global_navigation_remains_global()
    {
        using var repo = new Repo();
        var data = Path.Combine(Path.GetTempPath(), "AIReviewDesk.DogfoodTests", Guid.NewGuid().ToString("N"));
        try
        {
            var vm = new DeskViewModel(data); var project = repo.Project;
            await vm.SaveProjectAsync(project, true);
            vm.SelectProjectTab(true); Assert.True(vm.ShowReviews); Assert.True(vm.ShowProjectTabs);
            vm.Area = "Profiles"; Assert.False(vm.ShowProjectTabs); Assert.Equal(project.Id, vm.Selected!.Id);
            vm.ShowSelectedProject(); Assert.True(vm.OverviewSelected); Assert.True(vm.ShowProjectTabs);
            vm.Area = "Settings"; vm.ShowSelectedProject(); Assert.True(vm.OverviewSelected);
            await vm.SelectAsync(project with { Id = Guid.NewGuid(), DisplayName = "Other project" });
            Assert.True(vm.OverviewSelected); Assert.False(vm.ReviewsSelected); Assert.True(vm.ShowProjectTabs);
        }
        finally { Directory.Delete(data, true); }
    }

    [Fact]
    public void Sidebar_and_defaults_have_independent_bounded_scrolling_and_pinned_actions()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "AIReviewDesk.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var window = XDocument.Load(Path.Combine(root.FullName, "src/AIReviewDesk.App/MainWindow.xaml"));
        var projects = window.Descendants(ns + "ListBox").Single(e => (string?)e.Attribute("ItemsSource") == "{Binding Projects}");
        Assert.Equal("Auto", (string?)projects.Attribute("ScrollViewer.VerticalScrollBarVisibility"));
        Assert.Equal("DockPanel", projects.Parent!.Name.LocalName);
        Assert.DoesNotContain(projects.Descendants(), e => (string?)e.Attribute("Text") == "{Binding RepositoryPath}" || e.Name.LocalName == "Button");
        Assert.Contains(window.Descendants(), e => e.Name.LocalName == "GridSplitter");
        var defaults = XDocument.Load(Path.Combine(root.FullName, "src/AIReviewDesk.App/ProjectDialog.xaml"));
        var scroll = defaults.Descendants(ns + "ScrollViewer").Single();
        Assert.Equal("1", (string?)scroll.Attribute("Grid.Row")); Assert.Equal("Auto", (string?)scroll.Attribute("VerticalScrollBarVisibility"));
        Assert.Contains(scroll.Descendants(), e => e.Name.LocalName == "ItemsControl");
        Assert.DoesNotContain(scroll.Descendants(), e => (string?)e.Attribute("Content") == "Save defaults");
        Assert.Equal("CanResize", (string?)defaults.Root!.Attribute("ResizeMode"));
    }
}
