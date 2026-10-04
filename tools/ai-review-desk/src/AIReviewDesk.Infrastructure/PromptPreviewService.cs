using System.Diagnostics;
using AIReviewDesk.Core;

namespace AIReviewDesk.Infrastructure;

public sealed record PromptPreviewRequest(ProjectRegistration Project, ReviewScope Scope, IReadOnlyList<string> Paths, IReadOnlyList<string> Profiles);
public sealed record PreparedPromptPreview(ReviewInput Input, string Prompt, PromptSizeEvidence? Size = null);

/// <summary>One background boundary for advisory preparation and the production composer.
/// Inputs must be captured by the caller; progress is marshalled by its IProgress implementation.</summary>
public sealed class PromptPreviewService
{
    private readonly Func<PromptPreviewRequest, IProgress<string>?, CancellationToken, Task<ReviewInput>> prepare;
    public PromptPreviewService() : this((request, progress, ct) => new GitReviewContext().PrepareAsync(request.Project, request.Scope, request.Paths, ct, preview: true, progress: progress, profileIds: request.Profiles)) { }
    internal PromptPreviewService(Func<PromptPreviewRequest, IProgress<string>?, CancellationToken, Task<ReviewInput>> prepare) => this.prepare = prepare;

    public Task<PreparedPromptPreview> PrepareAsync(PromptPreviewRequest request, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        // Defensive copies happen before scheduling. No WPF objects/collections enter the worker.
        var captured = request with { Project = request.Project with { DefaultProfileIds = request.Project.DefaultProfileIds?.ToList() },
            Paths = request.Paths.ToArray(), Profiles = request.Profiles.ToArray() };
        return Task.Run(async () =>
        {
            var stageTimer = Stopwatch.StartNew();
            var stage = "start";
            var report = new StageProgress(value =>
            {
                Measure(stage, stageTimer); stage = value; stageTimer.Restart(); progress?.Report(value);
            });
            var input = await prepare(captured, report, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            report.Report("Preparing prompt");
            var prompt = PromptComposer.Compose(input, captured.Profiles);
            ct.ThrowIfCancellationRequested();
            Measure(stage, stageTimer);
            return new PreparedPromptPreview(input, prompt, ReviewContextCapability.Measure(prompt));
        }, ct);
    }
    private static void Measure(string stage, Stopwatch timer)
    {
        if (Environment.GetEnvironmentVariable("AI_REVIEW_DESK_PREVIEW_DIAGNOSTICS") == "1")
            Trace.WriteLine($"[AI Review Desk preview] {stage}: {timer.Elapsed.TotalMilliseconds:F1} ms; thread {Environment.CurrentManagedThreadId}; context {SynchronizationContext.Current != null}");
    }
    private sealed class StageProgress(Action<string> report) : IProgress<string> { public void Report(string value) => report(value); }
}
