using System.Diagnostics;
using System.Buffers;
using System.Text;

namespace AIReviewDesk.Infrastructure;

public sealed record CopilotProcessResult(int ExitCode, bool Cancelled, bool DiagnosticOutput);

public static class CopilotProcess
{
    // Byte limits, including JSON escaping. A 1.6M-character context can approach 9.6MB
    // when echoed as JSON (six bytes per escaped character); reserve envelope headroom.
    public const int MaxFrameBytes = 12 * 1024 * 1024;
    public const int MaxTransportBytes = 16 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static Task<CopilotProcessResult> RunAsync(ProcessStartInfo info, string? stdin, Action<string> onLine, CancellationToken cancellationToken) =>
        RunCoreAsync(info, stdin, frame => onLine(StrictUtf8.GetString(frame.Span)), cancellationToken, requireTerminator: false);

    // Frames are borrowed only for the synchronous callback. Production validates the
    // UTF-8 memory directly and does not materialize echoed prompt/tool-result strings.
    public static Task<CopilotProcessResult> RunFramesAsync(ProcessStartInfo info, string? stdin, Action<ReadOnlyMemory<byte>> onFrame, CancellationToken cancellationToken, Action? onStarted = null) =>
        RunCoreAsync(info, stdin, onFrame, cancellationToken, requireTerminator: true, onStarted);

    private static async Task<CopilotProcessResult> RunCoreAsync(ProcessStartInfo info, string? stdin, Action<ReadOnlyMemory<byte>> onFrame, CancellationToken cancellationToken, bool requireTerminator, Action? onStarted = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = info };
        if (!process.Start()) throw new InvalidOperationException("Copilot could not start.");
        onStarted?.Invoke();
        var diagnostics = false;
        var stdout = ConsumeFramesAsync(process.StandardOutput.BaseStream, onFrame, cancellationToken, requireTerminator);
        var stderr = ConsumeDiagnosticsAsync(process.StandardError.BaseStream, () => diagnostics = true, cancellationToken);
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

    internal static async Task ConsumeFramesAsync(Stream stream, Action<ReadOnlyMemory<byte>> onFrame, CancellationToken ct = default, bool requireTerminator = true)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        var frame = ArrayPool<byte>.Shared.Rent(64 * 1024);
        var length = 0;
        var total = 0;
        try
        {
            int count;
            while ((count = await stream.ReadAsync(buffer.AsMemory(0, 16 * 1024), ct).ConfigureAwait(false)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                total += count;
                if (total > MaxTransportBytes) throw new InvalidOperationException($"Copilot output exceeded the supported total transport limit ({total} bytes; limit {MaxTransportBytes}).");
                var offset = 0;
                while (offset < count)
                {
                    ct.ThrowIfCancellationRequested();
                    var newline = buffer.AsSpan(offset, count - offset).IndexOf((byte)'\n');
                    var take = newline < 0 ? count - offset : newline;
                    if (length + take > MaxFrameBytes) throw new InvalidOperationException($"Copilot emitted an oversized protocol frame ({length + take} bytes; limit {MaxFrameBytes}).");
                    if (length + take > frame.Length)
                    {
                        var grown = ArrayPool<byte>.Shared.Rent(Math.Min(MaxFrameBytes, Math.Max(length + take, frame.Length * 2)));
                        frame.AsSpan(0, length).CopyTo(grown);
                        ArrayPool<byte>.Shared.Return(frame, clearArray: true);
                        frame = grown;
                    }
                    buffer.AsSpan(offset, take).CopyTo(frame.AsSpan(length));
                    length += take;
                    offset += take;
                    if (newline >= 0)
                    {
                        if (length > 0 && frame[length - 1] == '\r') length--;
                        _ = StrictUtf8.GetCharCount(frame.AsSpan(0, length));
                        onFrame(frame.AsMemory(0, length));
                        length = 0;
                        offset++;
                    }
                }
            }
            if (length != 0)
            {
                if (requireTerminator) throw new InvalidOperationException("Copilot emitted an incomplete JSONL frame.");
                // --version/login are bounded plain-text commands, not JSONL reviews.
                if (frame[length - 1] == '\r') length--;
                _ = StrictUtf8.GetCharCount(frame.AsSpan(0, length));
                onFrame(frame.AsMemory(0, length));
            }
        }
        catch (DecoderFallbackException) { throw new InvalidOperationException("Copilot emitted invalid UTF-8."); }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            ArrayPool<byte>.Shared.Return(frame, clearArray: true);
        }
    }

    private static async Task ConsumeDiagnosticsAsync(Stream stream, Action observed, CancellationToken ct)
    {
        var buffer = new byte[4096];
        var characters = new char[4096];
        var decoder = StrictUtf8.GetDecoder();
        var total = 0;
        try
        {
            int count;
            while ((count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                total += count;
                if (total > MaxTransportBytes) throw new InvalidOperationException("Copilot diagnostics exceeded the supported transport limit.");
                observed();
                _ = decoder.GetChars(buffer.AsSpan(0, count), characters, false);
            }
            _ = decoder.GetChars(ReadOnlySpan<byte>.Empty, characters, true);
        }
        catch (DecoderFallbackException) { throw new InvalidOperationException("Copilot emitted invalid UTF-8."); }
        finally { Array.Clear(buffer); Array.Clear(characters); }
    }
}
