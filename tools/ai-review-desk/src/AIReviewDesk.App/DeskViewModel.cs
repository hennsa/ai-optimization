using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using AIReviewDesk.Core;
using AIReviewDesk.Infrastructure;

namespace AIReviewDesk.App;

public sealed class DeskViewModel : ObservableObject
{
    private readonly RegistryStore store = new();
    private readonly GitInspector git = new();
    private AppState state = new();
    private ProjectRegistration? selected;
    private RepositorySnapshot? snapshot;
    private string area = "Projects", notice = "", error = "", refreshed = "";
    private bool busy, ready;

    public ObservableCollection<ProjectRegistration> Projects { get; } = [];
    public IReadOnlyList<ReviewProfile> Profiles => BuiltInProfiles.All;
    public string DataDirectory => store.DirectoryPath;
    public AppState State => state;
    public ProjectRegistration? Selected { get => selected; private set { SetProperty(ref selected, value); NotifyView(); } }
    public RepositorySnapshot? Snapshot { get => snapshot; private set { SetProperty(ref snapshot, value); NotifyView(); } }
    public string Area { get => area; set { SetProperty(ref area, value); NotifyView(); } }
    public string Notice { get => notice; private set { SetProperty(ref notice, value); OnPropertyChanged(nameof(HasNotice)); } }
    public string Error { get => error; private set { SetProperty(ref error, value); OnPropertyChanged(nameof(HasError)); } }
    public string Refreshed { get => refreshed; private set => SetProperty(ref refreshed, value); }
    public bool Busy { get => busy; private set { SetProperty(ref busy, value); OnPropertyChanged(nameof(Interactive)); } }
    public bool Interactive => !Busy && ready;
    public bool HasNotice => Notice.Length > 0;
    public bool HasError => Error.Length > 0;
    public bool ShowProjects => Area == "Projects";
    public bool ShowReviews => Area == "Reviews";
    public bool ShowProfiles => Area == "Profiles";
    public bool ShowSettings => Area == "Settings";
    public bool HasProject => Selected != null;
    public bool Empty => Selected == null;
    public bool HasSnapshot => Snapshot != null;
    public bool SnapshotUnavailable => Selected != null && Snapshot == null;
    public bool HasBaseWarning => !string.IsNullOrWhiteSpace(Snapshot?.BaseWarning);
    public string WorkingState => Snapshot == null ? "Unavailable" : Snapshot.ConflictCount > 0 ? "Conflicts need attention" : Snapshot.IsClean ? "Working tree clean" : "Working changes";
    public string BaseLabel => Snapshot?.BaseRef ?? Selected?.DefaultBase ?? "No base selected";
    public string ProfileLabel => Profiles.FirstOrDefault(p => p.Id == Selected?.DefaultProfileId)?.Name ?? "Standard implementation";

    private void NotifyView()
    {
        foreach (var property in new[] { nameof(ShowProjects), nameof(ShowReviews), nameof(ShowProfiles), nameof(ShowSettings), nameof(HasProject), nameof(Empty), nameof(HasSnapshot), nameof(SnapshotUnavailable), nameof(HasBaseWarning), nameof(WorkingState), nameof(BaseLabel), nameof(ProfileLabel) })
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
        OnPropertyChanged(nameof(Interactive));
        if (Selected != null) await RefreshAsync();
    }

    public async Task SelectAsync(ProjectRegistration project)
    {
        await PersistAsync(state with { SelectedProjectId = project.Id });
        Selected = project;
        Area = "Projects";
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
        Snapshot = null;
        if (Selected != null) await RefreshAsync();
    }

    public Task SaveSettingsAsync(string theme, string profile, bool refresh) =>
        PersistAsync(state with { Theme = theme, DefaultProfileId = profile, RefreshOnActivate = refresh });

    private async Task PersistAsync(AppState updated)
    {
        await store.SaveAsync(updated);
        state = updated;
        OnPropertyChanged(nameof(State));
    }
}
