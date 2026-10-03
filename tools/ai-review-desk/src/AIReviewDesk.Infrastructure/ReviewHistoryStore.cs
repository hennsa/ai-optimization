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
        foreach (var file in Directory.EnumerateFiles(directory, $"{projectId:N}-*.json").OrderDescending().Take(100))
        {
            if (new FileInfo(file).Length > 512 * 1024) continue;
            try
            {
                var record = JsonSerializer.Deserialize<ReviewRecord>(await File.ReadAllTextAsync(file, cancellationToken), Options);
                if (record?.ProjectId == projectId) records.Add(record);
            }
            catch (JsonException) { /* A damaged record cannot become a completed run. */ }
        }
        return records.OrderByDescending(r => r.TimestampUtc).ToArray();
    }

    public async Task SaveAsync(ReviewRecord record, CancellationToken cancellationToken = default)
    {
        // Never persist partial/stale parsed findings or raw protocol/source diffs.
        if (record.Status != ReviewStatus.Completed) record = record with { Result = new ReviewResult() };
        var json = JsonSerializer.Serialize(record, Options);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 512 * 1024) throw new InvalidOperationException("Review result is too large for compact history.");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{record.ProjectId:N}-{record.TimestampUtc.UtcTicks:D19}-{record.Id:N}.json");
        var temporary = path + ".tmp";
        try { await File.WriteAllTextAsync(temporary, json, cancellationToken); File.Move(temporary, path, overwrite: false); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
