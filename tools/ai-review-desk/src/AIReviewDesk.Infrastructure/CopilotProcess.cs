using System.Diagnostics;

namespace AIReviewDesk.Infrastructure;

public sealed record CopilotProcessResult(int ExitCode, bool Cancelled, bool DiagnosticOutput);

public static class CopilotProcess
{
    public static async Task<CopilotProcessResult> RunAsync(ProcessStartInfo info, string? stdin, Action<string> onLine, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = info };
        if (!process.Start()) throw new InvalidOperationException("Copilot could not start.");
        var diagnostics = false;
        var stdout = ConsumeAsync(process.StandardOutput, onLine);
        var stderr = ConsumeAsync(process.StandardError, _ => diagnostics = true); // Never retain/log arbitrary CLI diagnostics.
        void Terminate()
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        }
        // A failed reader must terminate immediately, rather than waiting behind a blocked stdout pipe.
        _ = stdout.ContinueWith(_ => Terminate(), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        _ = stderr.ContinueWith(_ => Terminate(), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        using var registration = cancellationToken.Register(() =>
        {
            Terminate();
        });
        try
        {
            if (stdin != null) await process.StandardInput.WriteAsync(stdin.AsMemory(), cancellationToken);
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(stdout, stderr);
            return new(process.ExitCode, cancellationToken.IsCancellationRequested, diagnostics);
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None);
            try { await Task.WhenAll(stdout, stderr); } catch { /* Original transport/cancellation failure wins. */ }
            throw;
        }
    }

    private static async Task ConsumeAsync(StreamReader reader, Action<string> onLine)
    {
        try { await ConsumeUtf8Async(reader, onLine); }
        catch (System.Text.DecoderFallbackException) { throw new InvalidOperationException("Copilot emitted invalid UTF-8."); }
    }

    private static async Task ConsumeUtf8Async(StreamReader reader, Action<string> onLine)
    {
        // Bound both individual frames and overall transport; avoid ReadLine allocating unbounded attacker output.
        var buffer = new char[4096];
        var line = new System.Text.StringBuilder();
        var total = 0;
        while (await reader.ReadAsync(buffer) is var count && count > 0)
        {
            total += count;
            if (total > 16 * 1024 * 1024) throw new InvalidOperationException("Copilot output exceeded the supported transport limit.");
            for (var i = 0; i < count; i++)
            {
                if (buffer[i] == '\n') { onLine(line.ToString().TrimEnd('\r')); line.Clear(); }
                else line.Append(buffer[i]);
                if (line.Length > 2 * 1024 * 1024) throw new InvalidOperationException("Copilot emitted an oversized protocol frame.");
            }
        }
        if (line.Length > 0) onLine(line.ToString().TrimEnd('\r'));
    }
}
