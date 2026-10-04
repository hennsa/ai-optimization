using System.Diagnostics;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIReviewDesk.App;

/// <summary>Shared operation state; callers report actual stages, never percentages.</summary>
public sealed class OperationProgress(string title, string identity, string context, Action cancel, bool transitionOnSuccess = false, IReadOnlyList<string>? stages = null) : ObservableObject
{
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private string stage = "Starting", outcome = "", explanation = "";
    private bool finished, cancellationRequested, hasResult;
    public string Title { get; } = title;
    public string Identity { get; } = identity;
    public string Context { get; } = context;
    public ObservableCollection<OperationStage> Stages { get; } = new((stages ?? []).Select(s => new OperationStage(s, "Pending")));
    public string Stage => stage;
    public string Outcome => outcome;
    public string Explanation => explanation;
    public bool Finished => finished;
    public bool CanCancel => !finished && !cancellationRequested;
    public bool CancellationRequested => cancellationRequested;
    public bool ShouldTransition => finished && outcome == "Completed" && transitionOnSuccess;
    public string ActionLabel => hasResult ? "_View result" : "_Close";
    public string Elapsed => $"Elapsed {clock.Elapsed:hh\\:mm\\:ss}";
    public void Tick() => OnPropertyChanged(nameof(Elapsed));

    public void Report(string value)
    {
        if (finished || cancellationRequested) return;
        for (var i = 0; i < Stages.Count; i++)
            if (Stages[i].Name == value) Stages[i] = Stages[i] with { Status = "Current" };
            else if (Stages[i].Status == "Current") Stages[i] = Stages[i] with { Status = "Done" };
        SetProperty(ref stage, value, nameof(Stage));
    }
    public void Cancel()
    {
        if (!CanCancel) return;
        cancellationRequested = true;
        stage = "Cancelling and cleaning up…";
        OnPropertyChanged(nameof(Stage)); OnPropertyChanged(nameof(CanCancel)); OnPropertyChanged(nameof(CancellationRequested));
        cancel();
    }
    public void Complete(string status, string detail, bool resultAvailable = false)
    {
        if (finished) return;
        clock.Stop(); finished = true; outcome = stage = status; explanation = detail; hasResult = resultAvailable;
        for (var i = 0; i < Stages.Count; i++)
            if (Stages[i].Status == "Current") Stages[i] = Stages[i] with { Status = status is "Completed" or "Certified" ? "Done" : status };
        foreach (var name in new[] { nameof(Finished), nameof(Stage), nameof(Outcome), nameof(Explanation), nameof(CanCancel), nameof(ShouldTransition), nameof(ActionLabel), nameof(Elapsed) }) OnPropertyChanged(name);
    }
}

public sealed record OperationStage(string Name, string Status)
{
    public override string ToString() => $"{Name} — {Status}";
}
