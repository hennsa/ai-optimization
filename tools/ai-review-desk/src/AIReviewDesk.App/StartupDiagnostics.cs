using System.Diagnostics;

namespace AIReviewDesk.App;

internal static class StartupDiagnostics
{
    private const string EnableVariable = "AI_REVIEW_DESK_STARTUP_DIAGNOSTICS";
    private static readonly Stopwatch Clock = new();

    public static bool Enabled => string.Equals(Environment.GetEnvironmentVariable(EnableVariable), "1", StringComparison.Ordinal);

    public static void Begin()
    {
        if (!Enabled) return;
        Trace.AutoFlush = true;
        Clock.Restart();
        Mark("application-startup-entry");
    }

    public static void Mark(string stage)
    {
        if (Enabled) Trace.WriteLine($"[AI Review Desk startup] {stage} at {Clock.Elapsed.TotalMilliseconds:F1} ms");
    }

    public static void Measure(string stage, Stopwatch timer)
    {
        if (Enabled) Trace.WriteLine($"[AI Review Desk startup] {stage} took {timer.Elapsed.TotalMilliseconds:F1} ms (at {Clock.Elapsed.TotalMilliseconds:F1} ms)");
    }
}
