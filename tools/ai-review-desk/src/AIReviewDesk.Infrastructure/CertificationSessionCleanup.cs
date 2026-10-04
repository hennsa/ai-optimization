using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AIReviewDesk.Infrastructure;

/// <summary>Separate lifecycle operation for a GUID created by this certification call.
/// Discovery's metadata RPC allowlist remains unchanged. No enumeration, creation, prompt or auth RPC.</summary>
internal static class CertificationSessionCleanup
{
    internal static byte[] Request(Guid ownedId)
    {
        if (ownedId == Guid.Empty) throw new InvalidOperationException("Missing owned certification session identity.");
        return JsonSerializer.SerializeToUtf8Bytes(new { jsonrpc = "2.0", id = 3, method = "session.delete", @params = new { sessionId = ownedId.ToString() } });
    }

    internal static async Task DeleteAsync(Process process, Guid ownedId, CancellationToken ct)
    {
        var rpc = new CopilotMetadataRpc(process);
        using var connect = await rpc.RequestAsync("connect", ct);
        if (connect.RootElement.GetProperty("result").GetProperty("protocolVersion").GetInt32() != 3) throw new InvalidOperationException();
        using var status = await rpc.RequestAsync("status.get", ct);
        CopilotMetadataParser.ValidateRuntime(status.RootElement.GetProperty("result"));
        var body = Request(ownedId);
        await process.StandardInput.BaseStream.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"), ct);
        await process.StandardInput.BaseStream.WriteAsync(body, ct);
        await process.StandardInput.BaseStream.FlushAsync(ct);
        for (var n = 0; n < 64; n++)
        {
            using var response = await CopilotMetadataRpc.ReadFrameAsync(process.StandardOutput.BaseStream, ct);
            var root = response.RootElement;
            if (root.TryGetProperty("method", out _) && !root.TryGetProperty("id", out _)) continue;
            if (!root.TryGetProperty("id", out var id) || !id.TryGetInt32(out var number) || number != 3 ||
                root.TryGetProperty("error", out _) || !root.TryGetProperty("result", out _)) throw new InvalidOperationException();
            return;
        }
        throw new InvalidOperationException();
    }
}

public sealed partial class CopilotService
{
    internal async Task DeleteOwnedCertificationSessionAsync(Guid ownedId)
    {
        _ = CertificationSessionCleanup.Request(ownedId); // Reject empty identity before any filesystem/process access.
        var sessionPath = Path.Combine(Profile, "session-state", ownedId.ToString());
        CopilotPreflight.AssertNoReparseAncestors(sessionPath);
        if (!Directory.Exists(sessionPath)) return;
        var run = CreateRunDirectory(); Process? process = null; Task? stderr = null;
        try
        {
            var installation = CopilotContract.Detect() ?? throw new InvalidOperationException();
            var before = CopilotPreflight.Inspect(Profile, run);
            CopilotGitHubCliIsolation.Inspect(installation, run);
            var info = CopilotContract.StartInfo(installation, run, Profile, Path.Combine(run, "cache"), CopilotContract.MetadataArguments(Path.Combine(run, "logs")));
            if (before != CopilotPreflight.Inspect(Profile, run)) throw new InvalidOperationException();
            process = Process.Start(info) ?? throw new InvalidOperationException();
            stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await CertificationSessionCleanup.DeleteAsync(process, ownedId, timeout.Token);
            if (Directory.Exists(sessionPath)) throw new InvalidOperationException();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or JsonException or OperationCanceledException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or KeyNotFoundException)
        { throw new AIReviewDesk.Core.ReviewValidationException("Owned CLI certification session cleanup failed. Compatibility cannot be certified until cleanup succeeds."); }
        finally
        {
            if (process != null)
            {
                try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
                await process.WaitForExitAsync(); if (stderr != null) await stderr; process.Dispose();
            }
            DeleteRunDirectory(run);
        }
    }
}
