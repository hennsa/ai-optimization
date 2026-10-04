using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AIReviewDesk.Core;

namespace AIReviewDesk.Infrastructure;

public sealed record CopilotMetadata(IReadOnlyList<CopilotModelChoice> Models, CopilotQuota? Quota, string Message, bool ModelsAvailable = true, string? CliVersion = CopilotContract.SupportedVersion)
{
    public static CopilotMetadata Unavailable { get; } = new([CopilotModelPolicy.Auto], null,
        "Account quota is not available through the verified CLI 1.0.91 integration. Refresh to check availability.", ModelsAvailable: false, CliVersion: null);
    public string UsageDisplay => Quota?.Display ?? Message;
}

/// <summary>Only the SDK's read-only metadata RPCs; no session, prompt, tool, or credential API.</summary>
internal sealed class CopilotMetadataRpc(Process process)
{
    private int id;
    internal async Task<JsonDocument> RequestAsync(string method, CancellationToken cancellationToken)
    {
        if (method is not ("connect" or "status.get" or "models.list" or "account.getQuota"))
            throw new InvalidOperationException("Unsupported metadata operation.");
        var requestId = ++id;
        // No token parameter, auth callback or credential API. The official process owns saved authentication.
        var body = JsonSerializer.SerializeToUtf8Bytes(new { jsonrpc = "2.0", id = requestId, method, @params = new { } });
        await process.StandardInput.BaseStream.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"), cancellationToken);
        await process.StandardInput.BaseStream.WriteAsync(body, cancellationToken);
        await process.StandardInput.BaseStream.FlushAsync(cancellationToken);
        for (var notifications = 0; notifications < 64; notifications++)
        {
            var document = await ReadFrameAsync(process.StandardOutput.BaseStream, cancellationToken);
            var root = document.RootElement;
            if (root.TryGetProperty("method", out _))
            {
                // Reject server requests rather than granting authority or acquiring credentials.
                var request = root.TryGetProperty("id", out _);
                document.Dispose();
                if (request) throw new InvalidOperationException("Unexpected metadata request.");
                continue;
            }
            if (!root.TryGetProperty("id", out var responseId) || !responseId.TryGetInt32(out var n) || n != requestId ||
                root.TryGetProperty("error", out _) || !root.TryGetProperty("result", out _))
            { document.Dispose(); throw new InvalidOperationException("Metadata response was unavailable or ambiguous."); }
            return document;
        }
        throw new InvalidOperationException("Too many metadata notifications.");
    }

    internal static async Task<JsonDocument> ReadFrameAsync(Stream input, CancellationToken cancellationToken)
    {
        var header = new List<byte>(); var one = new byte[1];
        while (header.Count < 256)
        {
            if (await input.ReadAsync(one, cancellationToken) != 1) throw new IOException("Metadata transport ended.");
            header.Add(one[0]);
            if (header.Count >= 4 && header.TakeLast(4).SequenceEqual(new byte[] { 13, 10, 13, 10 })) break;
        }
        var text = Encoding.ASCII.GetString(header.ToArray());
        var lengths = text.Split("\r\n").Where(s => s.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (!text.EndsWith("\r\n\r\n") || lengths.Length != 1 ||
            !int.TryParse(lengths[0][15..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var length) || length is < 1 or > 512 * 1024)
            throw new InvalidOperationException("Invalid metadata frame size.");
        var bytes = new byte[length]; await input.ReadExactlyAsync(bytes, cancellationToken);
        var document = JsonDocument.Parse(bytes);
        try { CopilotStreamValidator.RequireUniqueProperties(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
    }
}

public static class CopilotMetadataParser
{
    public static void ValidateRuntime(JsonElement result)
    {
        if (result.GetProperty("version").GetString() != CopilotContract.SupportedVersion || result.GetProperty("protocolVersion").GetInt32() != 3)
            throw new InvalidOperationException("Unsupported metadata SDK/CLI protocol combination.");
    }
    public static IReadOnlyList<CopilotModelChoice> Models(JsonElement result)
    {
        var entries = result.GetProperty("models");
        if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > 100) throw new InvalidOperationException("Invalid models metadata.");
        var models = new List<CopilotModelChoice> { CopilotModelPolicy.Auto };
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries.EnumerateArray())
        {
            var id = entry.GetProperty("id").GetString()!;
            if (!ids.Add(id)) throw new InvalidOperationException("Ambiguous models metadata.");
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || id.StartsWith('-') || id.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))) throw new InvalidOperationException("Invalid model identity.");
            if (id == "auto") continue;
            if (entry.TryGetProperty("policy", out var policy) && policy.GetProperty("state").GetString() != "enabled") continue;
            var efforts = entry.TryGetProperty("supportedReasoningEfforts", out var values)
                ? values.EnumerateArray().Select(v => v.GetString() ?? throw new InvalidOperationException("Invalid effort.")).Distinct().ToArray() : [];
            if (efforts.Length > 16 || efforts.Any(e => e.Length is < 1 or > 32 || e.StartsWith('-') || e.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))) throw new InvalidOperationException("Invalid efforts metadata.");
            var name = entry.TryGetProperty("name", out var display) && display.ValueKind == JsonValueKind.String ? display.GetString()! : CopilotModelPolicy.DisplayName(id);
            if (name.Length > 128) throw new InvalidOperationException("Invalid model display name.");
            models.Add(new(id, name, ["auto", .. efforts.Where(e => e != "auto")]));
        }
        return models;
    }
    public static CopilotQuota? Quota(JsonElement result, DateTimeOffset fetchedAt)
    {
        if (!result.GetProperty("quotaSnapshots").TryGetProperty("premium_interactions", out var quota) ||
            !quota.TryGetProperty("hasQuota", out var has) || has.ValueKind != JsonValueKind.True) return null;
        var entitlement = CopilotUsageParser.NonnegativeDecimal(quota, "entitlementRequests") ?? throw new InvalidOperationException("Missing entitlement.");
        var used = CopilotUsageParser.NonnegativeDecimal(quota, "usedRequests") ?? throw new InvalidOperationException("Missing usage.");
        var remaining = CopilotUsageParser.NonnegativeDecimal(quota, "remainingPercentage") ?? throw new InvalidOperationException("Missing remaining allowance.");
        if (remaining > 100) throw new InvalidOperationException("Invalid remaining allowance.");
        var tokenBased = quota.GetProperty("tokenBasedBilling").GetBoolean();
        var unlimited = quota.GetProperty("isUnlimitedEntitlement").GetBoolean();
        // 1.0.91 returns its fetch timestamp as resetDate; deliberately do not retain or display it.
        return new(tokenBased ? "AI credits" : "Premium requests", entitlement, used, remaining, unlimited, fetchedAt);
    }
}
