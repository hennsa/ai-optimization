using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using AIReviewDesk.Core;
using AIReviewDesk.Infrastructure;

namespace AIReviewDesk.App;

public sealed class DeskViewModel : ObservableObject
{
    private readonly RegistryStore store = new();
    private readonly GitInspector git = new();
    private readonly CopilotService copilot = new();
    private readonly ReviewHistoryStore history = new();
    private AppState state = new();
    private ProjectRegistration? selected;
    private RepositorySnapshot? snapshot;
    private string area = "Projects", notice = "", error = "", refreshed = "";
    private bool busy, ready;
    private bool isSelectedPaths;
    private ReviewScope scope = ReviewScope.WorkingChanges;
    private ReviewProfile? selectedProfile;
    private ReviewInput? preparedInput;
    private ReviewRecord? latestReview;
    private CancellationTokenSource? reviewCancellation;
    private string reviewProgress = "", accountStatus = "Checking Copilot…", accountDetail = "";
    private bool activationRefreshRunning;
    private bool canSignIn;

    public ObservableCollection<ProjectRegistration> Projects { get; } = [];
    public IReadOnlyList<ReviewProfile> Profiles => BuiltInProfiles.All;
    public ObservableCollection<ProfileChoice> ProfileChoices { get; } = [];
    public ObservableCollection<ReviewPathChoice> ReviewPaths { get; } = [];
    public ObservableCollection<ReviewHistoryItem> ReviewHistory { get; } = [];
    public ObservableCollection<ReviewFinding> ReviewFindings { get; } = [];
    public string DataDirectory => store.DirectoryPath;
    public AppState State => state;
    public ProjectRegistration? Selected { get => selected; private set { SetProperty(ref selected, value); NotifyView(); OnPropertyChanged(nameof(HasReviewResult)); } }
    public RepositorySnapshot? Snapshot { get => snapshot; private set { SetProperty(ref snapshot, value); NotifyView(); } }
    public string Area { get => area; set { SetProperty(ref area, value); NotifyView(); } }
    public string Notice { get => notice; private set { SetProperty(ref notice, value); OnPropertyChanged(nameof(HasNotice)); } }
    public string Error { get => error; private set { SetProperty(ref error, value); OnPropertyChanged(nameof(HasError)); } }
    public string Refreshed { get => refreshed; private set => SetProperty(ref refreshed, value); }
    public bool Busy { get => busy; private set { SetProperty(ref busy, value); OnPropertyChanged(nameof(Interactive)); } }
    public bool Interactive => !Busy && !IsReviewRunning && ready;
    public bool HasNotice => Notice.Length > 0;
    public bool HasError => Error.Length > 0;
    public bool ShowProjects => Area == "Projects";
    public bool ShowReviews => Area == "Reviews";
    public bool ShowProfiles => Area == "Profiles";
    public bool ShowSettings => Area == "Settings";
    public bool IsSelectedPaths { get => isSelectedPaths; private set { SetProperty(ref isSelectedPaths, value); } }
    public ReviewScope Scope { get => scope; private set { if (SetProperty(ref scope, value)) IsSelectedPaths = value == ReviewScope.SelectedPaths; } }
    public ReviewProfile? SelectedProfile { get => selectedProfile; set { SetProperty(ref selectedProfile, value); OnPropertyChanged(nameof(HasSelectedProfile)); } }
    public bool HasSelectedProfile => SelectedProfile != null;
    public string SharedPolicyText => SharedReviewerPolicy.Instructions;
    public string ReviewProgress { get => reviewProgress; private set => SetProperty(ref reviewProgress, value); }
    public bool HasReviewResult => LatestReview != null && LatestReview.ProjectId == Selected?.Id;
    public bool CanHandoffLatest => LatestReview is { Status: ReviewStatus.Completed } record && record.ProjectId == Selected?.Id;
    public ReviewRecord? LatestReview { get => latestReview; private set { SetProperty(ref latestReview, value); OnPropertyChanged(nameof(HasReviewResult)); OnPropertyChanged(nameof(CanHandoffLatest)); OnPropertyChanged(nameof(ReviewResultTitle)); OnPropertyChanged(nameof(ReviewSummary)); } }
    public string ReviewResultTitle => LatestReview == null ? "" : $"{LatestReview.Status} · {LatestReview.Result.Findings.Count} finding(s)";
    public string ReviewSummary => LatestReview is { } record
        ? string.IsNullOrWhiteSpace(record.Result.Summary) ? record.Diagnostic ?? "" : record.Result.Summary
        : "";
    public string AccountStatus { get => accountStatus; private set => SetProperty(ref accountStatus, value); }
    public string AccountDetail { get => accountDetail; private set => SetProperty(ref accountDetail, value); }
    public bool CanSignOut => false;
    public bool CanSignIn => canSignIn;
    public bool CanStartReview => CopilotContract.ReviewContractVerified;
    public string ReviewAvailabilityMessage => CanStartReview ? "" : "Review execution is blocked until Copilot authentication and executable configuration are verified. You can still prepare and preview the prompt.";
    public bool CanSwitchAccount => false;
    public bool HasProject => Selected != null;
    public bool Empty => Selected == null;
    public bool HasSnapshot => Snapshot != null;
    public bool SnapshotUnavailable => Selected != null && Snapshot == null;
    public bool HasBaseWarning => !string.IsNullOrWhiteSpace(Snapshot?.BaseWarning);
    public string WorkingState => Snapshot == null ? "Unavailable" : Snapshot.ConflictCount > 0 ? "Conflicts need attention" : Snapshot.IsClean ? "Working tree clean" : "Working changes";
    public string BaseLabel => Snapshot?.BaseRef ?? Selected?.DefaultBase ?? "No base selected";
    public string ProfileLabel
    {
        get
        {
            var ids = Selected?.DefaultProfileIds ?? [Selected?.DefaultProfileId ?? "standard"];
            return string.Join(" + ", Profiles.Where(profile => ids.Contains(profile.Id, StringComparer.OrdinalIgnoreCase)).Select(profile => profile.Name));
        }
    }

    private void NotifyView()
    {
        foreach (var property in new[] { nameof(ShowProjects), nameof(ShowReviews), nameof(ShowProfiles), nameof(ShowSettings), nameof(HasProject), nameof(Empty), nameof(HasSnapshot), nameof(SnapshotUnavailable), nameof(HasBaseWarning), nameof(WorkingState), nameof(BaseLabel), nameof(ProfileLabel), nameof(IsSelectedPaths), nameof(CanHandoffLatest) })
            OnPropertyChanged(property);
    }

    // Every UI operation passes through this gate: no overlapping selection, saves or inspections.
    public async Task ExecuteAsync(Func<Task> operation)
    {
        if (Busy) return;
        Busy = true;
        Error = "";
        try { await operation(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Error = ex.Message; }
        finally { Busy = false; }
    }

    public async Task InitializeAsync()
    {
        var loaded = await store.LoadAsync();
        state = loaded.State;
        Notice = loaded.Warning ?? "";
        foreach (var project in state.Projects) Projects.Add(project);
        Selected = Projects.FirstOrDefault(p => p.Id == state.SelectedProjectId) ?? Projects.FirstOrDefault();
        ready = true;
        RebuildProfileChoices(state.DefaultProfileIds ?? [state.DefaultProfileId]);
        OnPropertyChanged(nameof(Interactive));
        if (Selected != null) await RefreshAsync();
        await RefreshAccountAsync();
    }

    public async Task SelectAsync(ProjectRegistration project)
    {
        await PersistAsync(state with { SelectedProjectId = project.Id });
        Selected = project;
        Area = "Projects";
        LoadProjectProfiles(project);
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        Snapshot = null;
        Refreshed = "";
        if (Selected == null) return;
        Snapshot = await git.InspectAsync(Selected.RepositoryPath, Selected.DefaultBase);
        Refreshed = $"Checked {DateTime.Now:t} · Local Git state · No remote fetch";
    }

    public async Task RefreshOnActivateAsync()
    {
        if (activationRefreshRunning || Busy || IsReviewRunning || !ready || !state.RefreshOnActivate || Selected == null || HasError)
            return;

        var capturedProject = Selected;
        activationRefreshRunning = true;
        try
        {
            // Activation refresh is deliberately outside ExecuteAsync: it must not disable
            // the workspace while a user is clicking a native modal control such as Add.
            var refreshedSnapshot = await git.InspectAsync(capturedProject.RepositoryPath, capturedProject.DefaultBase);
            if (!ReferenceEquals(Selected, capturedProject) || Busy || IsReviewRunning)
                return;

            Snapshot = refreshedSnapshot;
            Refreshed = $"Checked {DateTime.Now:t} · Local Git state · No remote fetch";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (ReferenceEquals(Selected, capturedProject) && !Busy && !IsReviewRunning && !HasError)
                Error = ex.Message;
        }
        finally
        {
            activationRefreshRunning = false;
        }
    }

    public async Task<RepositorySnapshot> DetectAsync(string path)
    {
        var detected = await git.InspectAsync(path);
        if (Projects.Any(p => string.Equals(System.IO.Path.TrimEndingDirectorySeparator(p.RepositoryPath), System.IO.Path.TrimEndingDirectorySeparator(detected.RootPath), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("This repository is already in your projects. Select it in the sidebar.");
        return detected;
    }

    public async Task SaveProjectAsync(ProjectRegistration project, bool adding)
    {
        var projects = state.Projects.ToList();
        if (adding) projects.Add(project);
        else projects[projects.FindIndex(p => p.Id == project.Id)] = project;
        await PersistAsync(state with { Projects = projects, SelectedProjectId = project.Id });
        if (adding) Projects.Add(project);
        else Projects[Projects.IndexOf(Projects.First(p => p.Id == project.Id))] = project;
        Selected = project;
        Area = "Projects";
        LoadProjectProfiles(project);
        await RefreshAsync();
    }

    public async Task RemoveAsync()
    {
        if (Selected == null) return;
        var removed = Selected;
        var remaining = state.Projects.Where(p => p.Id != removed.Id).ToList();
        await PersistAsync(state with { Projects = remaining, SelectedProjectId = remaining.FirstOrDefault()?.Id });
        Projects.Remove(removed);
        Selected = Projects.FirstOrDefault();
        LoadProjectProfiles(Selected);
        Snapshot = null;
        if (Selected != null) await RefreshAsync();
    }

    public Task SaveSettingsAsync(string theme, bool refresh) =>
        PersistAsync(state with { Theme = theme, DefaultProfileIds = SelectedDefaultProfileIds().ToList(), RefreshOnActivate = refresh });

    public void LoadProjectProfiles(ProjectRegistration? project)
    {
        var ids = project?.DefaultProfileIds ?? [project?.DefaultProfileId ?? "standard"];
        foreach (var choice in ProfileChoices) choice.IsSelected = ids.Contains(choice.Profile.Id, StringComparer.OrdinalIgnoreCase);
        InvalidateReviewInput();
    }

    public void RebuildProfileChoices(IEnumerable<string> defaults)
    {
        foreach (var choice in ProfileChoices) choice.PropertyChanged -= OnProfileChoiceChanged;
        ProfileChoices.Clear();
        var selectedIds = Selected?.DefaultProfileIds ?? [Selected?.DefaultProfileId ?? "standard"];
        var defaultIds = defaults.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in Profiles)
        {
            var choice = new ProfileChoice(profile, defaultIds.Contains(profile.Id), selectedIds.Contains(profile.Id, StringComparer.OrdinalIgnoreCase));
            choice.PropertyChanged += OnProfileChoiceChanged;
            ProfileChoices.Add(choice);
        }
    }

    private void OnProfileChoiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProfileChoice.IsSelected)) InvalidateReviewInput();
    }

    public IReadOnlyList<string> SelectedProfileIds() => ProfileChoices.Where(c => c.IsSelected).Select(c => c.Profile.Id).ToArray();
    public IReadOnlyList<string> SelectedDefaultProfileIds() => ProfileChoices.Where(c => c.IsDefault).Select(c => c.Profile.Id).ToArray();

    public void InvalidateReviewInput()
    {
        preparedInput = null;
        OnPropertyChanged(nameof(ReviewInputReady));
    }

    public bool ReviewInputReady => preparedInput != null;

    public async Task LoadChangedPathsAsync(ReviewScope forScope)
    {
        if (Selected == null) return;
        var paths = await new GitReviewContext().GetChangedPathsAsync(Selected, forScope);
        ReviewPaths.Clear();
        foreach (var path in paths) ReviewPaths.Add(new ReviewPathChoice(path));
    }

    public async Task<string> PreparePromptAsync(ReviewScope forScope, IReadOnlyList<string> selectedPaths)
    {
        if (Selected == null) throw new InvalidOperationException("Choose a project before preparing a review.");
        var profileIds = SelectedProfileIds();
        if (profileIds.Count == 0) throw new InvalidOperationException("Choose at least one review profile.");
        Scope = forScope;
        ReviewProgress = "Preparing review context";
        preparedInput = await new GitReviewContext().PrepareAsync(Selected, forScope, selectedPaths);
        OnPropertyChanged(nameof(ReviewInputReady));
        var prompt = PromptComposer.Compose(preparedInput, profileIds);
        ReviewProgress = "Prompt prepared from the current repository snapshot.";
        return prompt;
    }

    public ReviewInput? PreparedInput => preparedInput;

    public void SetReviewScope(ReviewScope value) => Scope = value;
    public void SetReviewProgress(string value) => ReviewProgress = value;
    public void SetError(string value) => Error = value;

    public async Task StartReviewAsync(ReviewScope forScope, IReadOnlyList<string> selectedPaths)
    {
        if (reviewCancellation != null) return;
        Error = "";
        LatestReview = null;
        ReviewFindings.Clear();
        if (Selected == null) { Error = "Choose a project before starting a review."; return; }
        reviewCancellation = new CancellationTokenSource();
        OnPropertyChanged(nameof(IsReviewRunning));
        OnPropertyChanged(nameof(Interactive));
        var cancellationToken = reviewCancellation.Token;
        try
        {
            var profileIds = SelectedProfileIds();
            if (profileIds.Count == 0) throw new InvalidOperationException("Choose at least one review profile.");
            var input = preparedInput;
            if (input == null || input.Project.Id != Selected.Id || input.Scope != forScope)
            {
                ReviewProgress = "Preparing review context";
                input = await new GitReviewContext().PrepareAsync(Selected, forScope, selectedPaths, cancellationToken);
                preparedInput = input;
                OnPropertyChanged(nameof(ReviewInputReady));
            }
            // Validate through the same production composer used by the preview and runner.
            _ = PromptComposer.Compose(input, profileIds);
            var progress = new Progress<string>(message => ReviewProgress = message);
            var record = await copilot.RunAsync(input, profileIds, progress, cancellationToken);
            LatestReview = record;
            if (record.Status == ReviewStatus.Completed)
                foreach (var finding in record.Result.Findings) ReviewFindings.Add(finding);
            await history.SaveAsync(record);
            await LoadHistoryAsync(record.ProjectId);
            ReviewProgress = record.Status switch
            {
                ReviewStatus.Completed => "Review completed and validated.",
                ReviewStatus.Stale => "Repository changed during the review. Findings are not presented as a clean result.",
                ReviewStatus.Cancelled => "Review cancelled.",
                ReviewStatus.Unsupported => record.Diagnostic ?? "This Copilot configuration is not supported.",
                _ => record.Diagnostic ?? "Review failed."
            };
        }
        catch (OperationCanceledException)
        {
            ReviewProgress = "Review cancelled.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Error = ex.Message;
            ReviewProgress = "Review failed.";
        }
        finally
        {
            reviewCancellation?.Dispose();
            reviewCancellation = null;
            OnPropertyChanged(nameof(IsReviewRunning));
            OnPropertyChanged(nameof(Interactive));
        }
    }

    public bool IsReviewRunning => reviewCancellation != null;
    public void CancelReview() => reviewCancellation?.Cancel();

    public async Task LoadHistoryAsync(Guid projectId)
    {
        ReviewHistory.Clear();
        foreach (var record in await history.LoadAsync(projectId)) ReviewHistory.Add(new ReviewHistoryItem(record));
    }

    public void ShowHistoryRecord(ReviewRecord record)
    {
        if (record.ProjectId != Selected?.Id) return;
        LatestReview = record;
        ReviewFindings.Clear();
        if (record.Status == ReviewStatus.Completed)
            foreach (var finding in record.Result.Findings) ReviewFindings.Add(finding);
    }

    public async Task RefreshAccountAsync() => SetAccount(await copilot.GetAccountAsync());
    public async Task SignInAsync()
    {
        try
        {
            await copilot.SignInAsync();
            await RefreshAccountAsync();
            ReviewProgress = "The official GitHub sign-in flow finished. This CLI version does not expose a supported account-status command.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Error = ex.Message; }
    }
    public async Task SignOutAsync()
    {
        try { await copilot.SignOutAsync(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Error = ex.Message; }
        await RefreshAccountAsync();
    }
    public async Task SwitchAccountAsync()
    {
        try
        {
            await copilot.SignOutAsync();
            await copilot.SignInAsync();
            await RefreshAccountAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Error = ex.Message; }
    }

    private void SetAccount(CopilotAccountState account)
    {
        canSignIn = account.Available && account.Supported && !account.ConfigurationBlocked;
        OnPropertyChanged(nameof(CanSignIn));
        AccountStatus = account.DisplayStatus;
        var identity = string.IsNullOrWhiteSpace(account.Identity) ? "Account identity unavailable" : account.Identity;
        var version = string.IsNullOrWhiteSpace(account.Version) ? "Version unavailable" : $"GitHub Copilot CLI {account.Version}";
        AccountDetail = $"{identity} · {version}. {account.Message} Sign out and account switching are unavailable because this CLI version has no supported noninteractive sign-out command.";
    }

    private async Task PersistAsync(AppState updated)
    {
        await store.SaveAsync(updated);
        state = updated;
        OnPropertyChanged(nameof(State));
    }
}

public sealed class ProfileChoice(ReviewProfile profile, bool isDefault, bool isSelected) : ObservableObject
{
    private bool defaultValue = isDefault, selectedValue = isSelected;
    public ReviewProfile Profile { get; } = profile;
    public bool IsDefault { get => defaultValue; set => SetProperty(ref defaultValue, value); }
    public bool IsSelected { get => selectedValue; set => SetProperty(ref selectedValue, value); }
}

public sealed record ReviewPathChoice(string Path)
{
    public string Display => Path;
}

public sealed record ReviewHistoryItem(ReviewRecord Record)
{
    public string Summary => $"{Record.TimestampUtc.LocalDateTime:g} · {Record.Status} · {Record.Result.Findings.Count} finding(s) · {Record.Result.Summary}";
}

public sealed record ScopeChoice(ReviewScope Scope, string Name);
