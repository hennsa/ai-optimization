using System.Text.Json;
using System.Text.Json.Serialization;
using AIReviewDesk.Core;

namespace AIReviewDesk.Infrastructure;

public sealed class ReviewHistoryStore
{
    private readonly string directory;
    private static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() } };
    public ReviewHistoryStore(string? dataDirectory = null) => directory = Path.Combine(dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIReviewDesk"), "Reviews");

    public async Task<IReadOnlyList<ReviewRecord>> LoadAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory)) return [];
        var records = new List<ReviewRecord>();
        foreach (var file in Directory.EnumerateFiles(directory, $"{projectId:N}-*.json"))
        {
            try
            {
                if (new FileInfo(file).Length > 512 * 1024) continue;
                var json = await File.ReadAllTextAsync(file, cancellationToken);
                using var document = JsonDocument.Parse(json);
                // Missing status must not default to Completed; missing timestamps must not become today.
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("Status", out _) ||
                    !document.RootElement.TryGetProperty("TimestampUtc", out _) ||
                    !document.RootElement.TryGetProperty("Scope", out _) ||
                    !document.RootElement.TryGetProperty("Id", out _)) continue;
                var record = JsonSerializer.Deserialize<ReviewRecord>(json, Options);
                if (record?.Status == ReviewStatus.Completed && !document.RootElement.TryGetProperty("Result", out _)) continue;
                if (record?.ProjectId == projectId) records.Add(Normalize(record));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
            { /* A damaged/unavailable record cannot become a completed run. Preserve its file. */ }
        }
        return records.OrderByDescending(r => r.TimestampUtc).ThenByDescending(r => r.Id).Take(100).ToArray();
    }

    public async Task SaveAsync(ReviewRecord record, CancellationToken cancellationToken = default)
    {
        // Never persist partial/stale parsed findings or raw protocol/source diffs.
        record = Normalize(record);
        var json = JsonSerializer.Serialize(record, Options);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 512 * 1024) throw new InvalidOperationException("Review result is too large for compact history.");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{record.ProjectId:N}-{record.TimestampUtc.UtcTicks:D19}-{record.Id:N}.json");
        var temporary = path + ".tmp";
        try { await File.WriteAllTextAsync(temporary, json, cancellationToken); File.Move(temporary, path, overwrite: false); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static ReviewRecord Normalize(ReviewRecord record)
    {
        if (record.SchemaVersion is < 1 or > 3 || !Enum.IsDefined(record.Status) || !Enum.IsDefined(record.Scope) ||
            record.Id == Guid.Empty || record.ProjectId == Guid.Empty || record.TimestampUtc == default)
            throw new InvalidOperationException("Invalid or unsupported review history record.");
        var result = new ReviewResult();
        if (record.Status == ReviewStatus.Completed)
        {
            if (record.Result == null || record.Result.Findings == null || record.Result.Limitations == null)
                throw new InvalidOperationException("Completed review has no valid result.");
            // The same structured finding validation used for accepted execution applies on reopen.
            result = CopilotStreamValidator.ParseResult(JsonSerializer.Serialize(record.Result,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        }
        return record with
        {
            Result = result,
            TrackedChangedCount = record.SchemaVersion >= 3 ? record.TrackedChangedCount : null,
            UntrackedCount = record.SchemaVersion >= 3 ? record.UntrackedCount : null,
            SelectedPaths = record.SelectedPaths?.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray() ?? [],
            ProfileIds = record.ProfileIds?.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray() ?? [],
            ProfileNames = record.ProfileNames ?? new Dictionary<string, string>(),
            ProfileVersions = record.ProfileVersions ?? new Dictionary<string, string>()
        };
    }
}
