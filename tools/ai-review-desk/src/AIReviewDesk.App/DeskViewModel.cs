using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using AIReviewDesk.Core;
using AIReviewDesk.Infrastructure;

namespace AIReviewDesk.App;

public sealed class DeskViewModel : ObservableObject
{
    private readonly RegistryStore store;
    private readonly GitInspector git = new();
    private readonly CopilotService copilot;
    private readonly ReviewHistoryStore history;
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
    private bool newReview;
    private bool historyList = true;
    private string severityFilter = "All", certaintyFilter = "All", categoryFilter = "All", findingSearch = "";
    private FindingItem? selectedFinding;
    private ReviewExecutionSettings execution = CopilotModelPolicy.Default;
    private readonly ObservableCollection<CopilotModelChoice> models = new(CopilotModelPolicy.Verified);
    private string executionNotice = "";
    private CopilotMetadata metadata = CopilotMetadata.Unavailable;
    private readonly ObservableCollection<EffortChoice> effortChoices = new(CopilotModelPolicy.Efforts(CopilotModelPolicy.Default.ModelId).Select(e => new EffortChoice(e, CopilotModelPolicy.EffortName(e))));

    public DeskViewModel(string? dataDirectory = null)
    {
        store = new RegistryStore(dataDirectory);
        history = new ReviewHistoryStore(dataDirectory);
        copilot = new CopilotService(dataDirectory);
    }

    public ObservableCollection<ProjectRegistration> Projects { get; } = [];
    public IReadOnlyList<CopilotModelChoice> Models => models;
    public IReadOnlyList<EffortChoice> EffortChoices => effortChoices;
    public CopilotModelChoice? SelectedModel { get => models.FirstOrDefault(m => m.Id == execution.ModelId); set { if (value != null) SelectedModelId = value.Id; } }
    public EffortChoice? SelectedReasoningChoice { get => effortChoices.FirstOrDefault(e => e.Id == execution.ReasoningEffort); set { if (value != null) SelectedEffort = value.Id; } }
    public string SelectedModelId
    {
        get => execution.ModelId;
        set
        {
            if (value == null || value == execution.ModelId || !models.Any(m => m.Id == value)) return;
            var requested = execution with { ModelId = value };
            ApplyExecution(CopilotModelPolicy.Adjust(requested, models, out var adjustment));
            ExecutionNotice = adjustment ?? "";
        }
    }
    public string SelectedEffort
    {
        get => execution.ReasoningEffort;
        set
        {
            if (value == null || !EffortChoices.Any(e => e.Id == value) || value == execution.ReasoningEffort) return;
            ApplyExecution(execution with { ReasoningEffort = value }); ExecutionNotice = "";
        }
    }
    public ReviewExecutionSettings Execution => execution;
    public string ExecutionDisplay => execution.Display;
    public string AutoExplanation => CopilotModelPolicy.AutoExplanation;
    public string ExecutionNotice { get => executionNotice; private set => SetProperty(ref executionNotice, value); }
    public string ReasoningHint => execution.IsAutoModel ? "Choose an explicit model to override reasoning. Auto leaves both settings to the CLI." : EffortChoices.Count == 1 ? "This model exposes no reasoning override; Auto is required." : "Only this model's verified reasoning levels are shown. Auto passes no effort override.";
    public string CopilotUsage => metadata.UsageDisplay;
    private void ApplyExecution(ReviewExecutionSettings value)
    {
        execution = value;
        var efforts = models.FirstOrDefault(m => m.Id == execution.ModelId)?.Efforts ?? ["auto"];
        effortChoices.Clear();
        foreach (var effort in efforts) effortChoices.Add(new(effort, CopilotModelPolicy.EffortName(effort)));
        foreach (var name in new[] { nameof(SelectedModelId), nameof(SelectedModel), nameof(EffortChoices), nameof(SelectedEffort), nameof(SelectedReasoningChoice), nameof(ExecutionDisplay), nameof(ReasoningHint) }) OnPropertyChanged(name);
        InvalidateReviewInput();
    }
    internal void SetMetadata(CopilotMetadata value)
    {
        var requested = execution;
        metadata = value; models.Clear();
        foreach (var model in value.Models) models.Add(model);
        OnPropertyChanged(nameof(Models)); OnPropertyChanged(nameof(CopilotUsage));
        ApplyExecution(CopilotModelPolicy.Adjust(requested, models, out var adjustment));
        ExecutionNotice = adjustment ?? "";
    }
    public IReadOnlyList<ReviewProfile> Profiles => BuiltInProfiles.All;
    public ObservableCollection<ProfileChoice> ProfileChoices { get; } = [];
    public ObservableCollection<ReviewPathChoice> ReviewPaths { get; } = [];
    public ObservableCollection<ReviewHistoryItem> ReviewHistory { get; } = [];
    public ObservableCollection<FindingItem> ReviewFindings { get; } = [];
    public IReadOnlyList<string> SeverityOptions { get; } = ["All", "critical", "high", "medium", "low", "info"];
    public IReadOnlyList<string> CertaintyOptions { get; } = ["All", "confirmed", "probable", "possible"];
    public ObservableCollection<string> CategoryOptions { get; } = ["All"];
    public string SeverityFilter { get => severityFilter; set { if (SetProperty(ref severityFilter, value)) FilterFindings(); } }
    public string CertaintyFilter { get => certaintyFilter; set { if (SetProperty(ref certaintyFilter, value)) FilterFindings(); } }
    public string CategoryFilter { get => categoryFilter; set { if (SetProperty(ref categoryFilter, value)) FilterFindings(); } }
    public string FindingSearch { get => findingSearch; set { if (SetProperty(ref findingSearch, value)) FilterFindings(); } }
    public FindingItem? SelectedFinding { get => selectedFinding; set { SetProperty(ref selectedFinding, value); OnPropertyChanged(nameof(HasSelectedFinding)); } }
    public bool HasSelectedFinding => SelectedFinding != null;
    public bool NoFilterMatches => Details?.HasFindings == true && ReviewFindings.Count == 0;
    public bool ShowFindingList => Details?.FindingsCount > 1;
    public string FilterCount => $"Showing {ReviewFindings.Count} of {Details?.FindingsCount ?? 0} findings";
    public bool ShowNewReview => newReview;
    public bool ShowHistory => !newReview;
    public bool ShowHistoryList => !newReview && historyList;
    public bool ShowReviewDetail => !newReview && !historyList && HasReviewResult;
    public bool EmptyHistory => ReviewHistory.Count == 0;
    public ReviewDetails? Details => HasReviewResult ? new(LatestReview!) : null;
    public string DataDirectory => store.DirectoryPath;
    public AppState State => state;
    public ProjectRegistration? Selected { get => selected; private set { if (SetProperty(ref selected, value)) ResetReviewWorkspace(); NotifyView(); } }
    public RepositorySnapshot? Snapshot { get => snapshot; private set { SetProperty(ref snapshot, value); NotifyView(); } }
    public string Area { get => area; set { SetProperty(ref area, value); NotifyView(); } }
    public string Notice { get => notice; private set { SetProperty(ref notice, value); OnPropertyChanged(nameof(HasNotice)); } }
    public string Error { get => error; private set { SetProperty(ref error, value); OnPropertyChanged(nameof(HasError)); } }
    public string Refreshed { get => refreshed; private set => SetProperty(ref refreshed, value); }
    public bool Busy { get => busy; private set { SetProperty(ref busy, value); OnPropertyChanged(nameof(Interactive)); } }
    public bool Interactive => !Busy && !IsReviewRunning && ready;
    public bool HasNotice => Notice.Length > 0;
    public bool HasError => Error.Length > 0;
    public bool ShowProjectTabs => HasProject && (ShowProjects || ShowReviews);
    public bool OverviewSelected => ShowProjects;
    public bool ReviewsSelected => ShowReviews;
    public void SelectProjectTab(bool reviews) { Area = reviews ? "Reviews" : "Projects"; if (reviews) ShowReviewHistory(); }
    public void ShowSelectedProject() { if (HasProject) Area = "Projects"; }
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
    public ReviewRecord? LatestReview { get => latestReview; private set { SetProperty(ref latestReview, value); OnPropertyChanged(nameof(HasReviewResult)); OnPropertyChanged(nameof(CanHandoffLatest)); OnPropertyChanged(nameof(Details)); OnPropertyChanged(nameof(ShowFindingList)); } }
    public string AccountStatus { get => accountStatus; private set => SetProperty(ref accountStatus, value); }
    public string AccountDetail { get => accountDetail; private set => SetProperty(ref accountDetail, value); }
    public bool CanSignOut => false;
    public bool CanSignIn => canSignIn;
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
        foreach (var property in new[] { nameof(ShowProjectTabs), nameof(OverviewSelected), nameof(ReviewsSelected), nameof(ShowProjects), nameof(ShowReviews), nameof(ShowProfiles), nameof(ShowSettings), nameof(HasProject), nameof(Empty), nameof(HasSnapshot), nameof(SnapshotUnavailable), nameof(HasBaseWarning), nameof(WorkingState), nameof(BaseLabel), nameof(ProfileLabel), nameof(IsSelectedPaths), nameof(CanHandoffLatest) })
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
        LoadProjectProfiles(Selected);
        OnPropertyChanged(nameof(Interactive));
        if (Selected != null) { await LoadHistoryAsync(Selected.Id); await RefreshAsync(); }
        await RefreshAccountAsync();
    }

    public async Task SelectAsync(ProjectRegistration project)
    {
        await PersistAsync(state with { SelectedProjectId = project.Id });
        Selected = project;
        Area = "Projects";
        LoadProjectProfiles(project);
        await LoadHistoryAsync(project.Id);
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
        await LoadHistoryAsync(project.Id);
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
        if (Selected != null) { await LoadHistoryAsync(Selected.Id); await RefreshAsync(); }
    }

    public Task SaveSettingsAsync(string theme, bool refresh) =>
        PersistAsync(state with { Theme = theme, DefaultProfileIds = SelectedDefaultProfileIds().ToList(), RefreshOnActivate = refresh });

    public void LoadProjectProfiles(ProjectRegistration? project)
    {
        var ids = project?.DefaultProfileIds ?? [project?.DefaultProfileId ?? "standard"];
        foreach (var choice in ProfileChoices) choice.IsSelected = ids.Contains(choice.Profile.Id, StringComparer.OrdinalIgnoreCase);
        ApplyExecution(CopilotModelPolicy.Adjust(project?.DefaultExecution ?? CopilotModelPolicy.Default, models, out var adjustment));
        ExecutionNotice = adjustment ?? "";
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
        if (profileIds.Count == 0) throw new ReviewValidationException("Choose at least one review profile.");
        CopilotModelPolicy.Validate(execution, CopilotModelPolicy.Version, models);
        Scope = forScope;
        preparedInput = await new GitReviewContext().PrepareAsync(Selected, forScope, selectedPaths, preview: true, progress: new Progress<string>(message => ReviewProgress = message));
        OnPropertyChanged(nameof(ReviewInputReady));
        ReviewProgress = "Preparing prompt";
        var prompt = PromptComposer.Compose(preparedInput, profileIds);
        ReviewProgress = preparedInput.HasReviewableChanges ? "Prompt prepared from the current repository snapshot." : "The selected scope has no changes. Preview is available; Start review is blocked.";
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
        SelectedFinding = null;
        if (Selected == null) { Error = "Choose a project before starting a review."; return; }
        reviewCancellation = new CancellationTokenSource();
        OnPropertyChanged(nameof(IsReviewRunning));
        OnPropertyChanged(nameof(Interactive));
        var cancellationToken = reviewCancellation.Token;
        var selectedExecution = execution;
        try
        {
            var profileIds = SelectedProfileIds();
            if (profileIds.Count == 0) throw new ReviewValidationException("Choose at least one review profile.");
            CopilotModelPolicy.Validate(selectedExecution, CopilotModelPolicy.Version, models);
            // Always prepare afresh for execution; a preview is an audit snapshot, not execution authority.
            var input = await new GitReviewContext().PrepareAsync(Selected, forScope, selectedPaths, cancellationToken, progress: new Progress<string>(message => ReviewProgress = message));
            Snapshot = input.Snapshot;
            preparedInput = input;
            OnPropertyChanged(nameof(ReviewInputReady));
            // Validate through the same production composer used by the preview and runner.
            ReviewProgress = "Preparing prompt";
            _ = PromptComposer.Compose(input, profileIds);
            var progress = new Progress<string>(message => ReviewProgress = message);
            var record = await copilot.RunAsync(input, profileIds, progress, cancellationToken, selectedExecution);
            ShowHistoryRecord(record);
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
            await SavePreparationOutcomeAsync(ReviewStatus.Cancelled, "Review cancelled during context preparation. No accepted result is available.", forScope, selectedPaths);
            ReviewProgress = "Review cancelled.";
        }
        catch (ReviewValidationException ex)
        {
            Error = ex.Message;
            ReviewProgress = "Review has not started. Preview remains available.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Error = SafePreparationDiagnostic(ex);
            if (LatestReview == null)
                await SavePreparationOutcomeAsync(ReviewStatus.Failed, SafePreparationDiagnostic(ex), forScope, selectedPaths);
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
        var records = await history.LoadAsync(projectId);
        if (Selected?.Id != projectId) return;
        ReviewHistory.Clear();
        foreach (var record in records) ReviewHistory.Add(new ReviewHistoryItem(record));
        OnPropertyChanged(nameof(EmptyHistory));
    }

    public static string SafePreparationDiagnostic(Exception error) => error switch
    {
        PreparationException known => known.Message,
        IOException or UnauthorizedAccessException => "Repository files could not be read during preparation. Check access and file availability.",
        _ => "Repository context could not be prepared safely. Check the repository and selected scope."
    };

    private async Task SavePreparationOutcomeAsync(ReviewStatus status, string diagnostic, ReviewScope forScope, IReadOnlyList<string> paths)
    {
        if (Selected == null) return;
        var profiles = Profiles.Where(p => SelectedProfileIds().Contains(p.Id)).ToArray();
        var record = new ReviewRecord
        {
            SchemaVersion = 4, RequestedExecution = execution, ProjectId = Selected.Id, ProjectName = Selected.DisplayName, RepositoryPath = Selected.RepositoryPath,
            Scope = forScope, SelectedPaths = forScope == ReviewScope.SelectedPaths ? paths : [],
            Branch = Snapshot?.Branch ?? "", HeadSha = Snapshot?.HeadSha, BaseRef = Snapshot?.BaseRef, MergeBaseSha = Snapshot?.MergeBaseSha,
            ChangedFileCount = Snapshot?.ChangedFileCount ?? 0, TrackedChangedCount = Snapshot?.TrackedChangedCount, UntrackedCount = Snapshot?.UntrackedCount, ProfileIds = profiles.Select(p => p.Id).ToArray(),
            ProfileNames = profiles.ToDictionary(p => p.Id, p => p.Name), ProfileVersions = profiles.ToDictionary(p => p.Id, p => p.Version),
            AppVersion = "0.1.0", Status = status, Diagnostic = diagnostic
        };
        ShowHistoryRecord(record);
        try { await history.SaveAsync(record); await LoadHistoryAsync(record.ProjectId); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { Error = "Review history could not be saved. " + ex.Message; }
    }

    public void ShowHistoryRecord(ReviewRecord record)
    {
        if (record.ProjectId != Selected?.Id) return;
        LatestReview = record;
        CategoryOptions.Clear(); CategoryOptions.Add("All");
        foreach (var category in new ReviewDetails(record).Findings.Select(f => f.Category).Distinct().Order()) CategoryOptions.Add(category);
        // Replacing the options can clear WPF's SelectedItem binding. Reset after rebuilding.
        severityFilter = certaintyFilter = categoryFilter = "All";
        findingSearch = "";
        foreach (var property in new[] { nameof(SeverityFilter), nameof(CertaintyFilter), nameof(CategoryFilter), nameof(FindingSearch) }) OnPropertyChanged(property);
        SelectedFinding = null;
        FilterFindings();
        newReview = false; historyList = false;
        NotifyReviewNavigation();
    }

    private void FilterFindings()
    {
        var previous = SelectedFinding?.Finding;
        ReviewFindings.Clear();
        foreach (var finding in FindingFilter.Apply(Details?.Findings ?? [], severityFilter, certaintyFilter, categoryFilter, findingSearch))
            ReviewFindings.Add(new FindingItem(finding, LatestReview?.RepositoryPath ?? ""));
        SelectedFinding = ReviewFindings.FirstOrDefault(f => f.Finding == previous) ?? ReviewFindings.FirstOrDefault();
        OnPropertyChanged(nameof(NoFilterMatches)); OnPropertyChanged(nameof(FilterCount));
    }

    public void ShowNewReviewForm(bool useDefaults = true)
    {
        if (useDefaults) LoadProjectProfiles(Selected);
        newReview = true;
        NotifyReviewNavigation();
    }
    public void ShowReviewHistory()
    {
        newReview = false;
        historyList = true;
        NotifyReviewNavigation();
    }
    private void NotifyReviewNavigation()
    {
        foreach (var name in new[] { nameof(ShowNewReview), nameof(ShowHistory), nameof(ShowHistoryList), nameof(ShowReviewDetail) }) OnPropertyChanged(name);
    }
    private void ResetReviewWorkspace()
    {
        LatestReview = null; ReviewHistory.Clear(); ReviewFindings.Clear(); SelectedFinding = null;
        ReviewPaths.Clear(); ReviewProgress = ""; Scope = ReviewScope.WorkingChanges;
        InvalidateReviewInput(); ShowReviewHistory(); OnPropertyChanged(nameof(EmptyHistory));
    }

    public async Task<ReusedReviewSetup?> UsePreviousSetupAsync()
    {
        if (LatestReview == null || LatestReview.ProjectId != Selected?.Id || IsReviewRunning) return null;
        // Only reusable configuration crosses this boundary. Git data and prompt are always fresh.
        InvalidateReviewInput();
        await RefreshAsync();
        var targetScope = LatestReview.Scope == ReviewScope.BranchVsBase && Snapshot?.BaseRef == null ? ReviewScope.WorkingChanges : LatestReview.Scope;
        await LoadChangedPathsAsync(targetScope);
        var setup = ReusedReviewSetup.From(LatestReview, ReviewPaths.Select(p => p.Path).ToArray(), Snapshot?.BaseRef != null, models);
        ApplyExecution(setup.Execution!);
        ExecutionNotice = "";
        Scope = setup.Scope;
        foreach (var choice in ProfileChoices) choice.IsSelected = setup.ProfileIds.Contains(choice.Profile.Id);
        ReviewProgress = setup.Message;
        ShowNewReviewForm(useDefaults: false);
        return setup;
    }

    public async Task RefreshAccountAsync()
    {
        SetAccount(await copilot.GetAccountAsync());
        SetMetadata(await copilot.GetMetadataAsync());
    }
    public async Task SignInAsync()
    {
        try
        {
            await copilot.SignInAsync();
            SetAccount((await copilot.GetAccountAsync()) with { SignInCompleted = true });
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

    internal void SetAccount(CopilotAccountState account)
    {
        canSignIn = account.Available && account.Supported && !account.ConfigurationBlocked;
        OnPropertyChanged(nameof(CanSignIn));
        AccountStatus = account.DisplayStatus;
        var identity = string.IsNullOrWhiteSpace(account.Identity) ? "Account identity unavailable" : account.Identity;
        var version = string.IsNullOrWhiteSpace(account.Version) ? "Version unavailable" : $"GitHub Copilot CLI {account.Version}";
        var completion = account.SignInCompleted ? "The dedicated Copilot profile was updated successfully. Authentication is verified when a review starts. " : "";
        AccountDetail = $"{completion}{identity} · {version}. {account.Message} Sign out and account switching are unavailable because this CLI version has no supported noninteractive sign-out command.";
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
    public ReviewDetails Details => new(Record);
    public override string ToString() => $"{Details.TimeLabel} · {Details.StatusLabel} · {Details.ContextLabel} · {Details.ProfilesLabel}";
}

public sealed record ScopeChoice(ReviewScope Scope, string Name);
public sealed record EffortChoice(string Id, string Name);
