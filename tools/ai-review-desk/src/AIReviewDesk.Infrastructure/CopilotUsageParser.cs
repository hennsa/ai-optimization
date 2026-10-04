using System.Text.Json;
using AIReviewDesk.Core;

namespace AIReviewDesk.Infrastructure;

public static class CopilotUsageParser
{
    public static ReviewUsage Parse(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 128 * 1024) throw new InvalidOperationException("Usage metadata is too large.");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        CopilotStreamValidator.RequireUniqueProperties(root);
        var models = new List<ModelTokenUsage>();
        if (root.TryGetProperty("modelMetrics", out var metrics))
        {
            if (metrics.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Malformed model usage.");
            foreach (var model in metrics.EnumerateObject())
            {
                if (models.Count >= 20 || !ValidModelId(model.Name)) throw new InvalidOperationException("Invalid usage model.");
                var usage = model.Value.GetProperty("usage");
                long? Tokens(string name)
                {
                    if (!usage.TryGetProperty(name, out var value)) return null;
                    if (!value.TryGetInt64(out var count) || count < 0) throw new InvalidOperationException("Invalid token count.");
                    return count;
                }
                var counts = new ModelTokenUsage(model.Name, Tokens("inputTokens"), Tokens("outputTokens"), Tokens("cacheReadTokens"), Tokens("cacheWriteTokens"), Tokens("reasoningTokens"));
                if (counts.InputTokens != null || counts.OutputTokens != null || counts.CacheReadTokens != null || counts.CacheWriteTokens != null || counts.ReasoningTokens != null)
                    models.Add(counts);
            }
        }
        var result = new ReviewUsage(models, NonnegativeDecimal(root, "totalNanoAiu"), NonnegativeDecimal(root, "totalPremiumRequestCost"));
        if (models.Count == 0 && result.NanoAiUnits == null && result.PremiumRequestCost == null) throw new InvalidOperationException("No usable usage metadata.");
        return result;
    }
    internal static bool ValidModelId(string? value) => value != null && value.Length is > 0 and <= 100 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
    internal static decimal? NonnegativeDecimal(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return null;
        if (!value.TryGetDecimal(out var number) || number < 0 || number > 1_000_000_000_000_000_000m) throw new InvalidOperationException("Invalid usage value.");
        return number;
    }
}
