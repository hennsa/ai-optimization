using System.Text;
using System.Text.Json;
using AIReviewDesk.Core;

namespace AIReviewDesk.Infrastructure;

/// <summary>Diagnostics only: never returns payloads or authorizes an envelope/parser fallback.</summary>
public static class ResponseStructureScanner
{
    public static ResponseStructure Inspect(string text)
    {
        var trimmed = text.Trim(' ', '\t', '\r', '\n');
        var lines = trimmed.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var fences = lines.Count(line => line.TrimStart(' ', '\t').StartsWith("```", StringComparison.Ordinal));
        var leading = Boundary(lines.FirstOrDefault() ?? "", leading: true);
        var trailing = Boundary(lines.LastOrDefault() ?? "", leading: false);
        var shape = ResponseShape.OtherPresentation;
        string? payload = null;
        if (Parses(text)) { shape = ResponseShape.RawJson; payload = text; }
        else if (OutputEnvelope.TryExtractFence(text, out var fenced)) { shape = ResponseShape.SingleJsonFence; payload = fenced; }
        else if (fences > 2) shape = ResponseShape.MultipleFenceBlocks;
        else if (fences > 0)
        {
            if (fences == 1) shape = ResponseShape.IncompleteFence;
            else if (leading != ResponseBoundary.Fence || trailing != ResponseBoundary.Fence) shape = ResponseShape.SurroundingText;
            else shape = ResponseShape.UnsupportedFence;
        }
        else if (HasJsonPrefixWithTrailingText(text)) shape = ResponseShape.SurroundingText;
        else if (trimmed.StartsWith('{') || trimmed.StartsWith('[')) shape = ResponseShape.MalformedJson;
        var parsed = payload != null && Parses(payload);
        var schema = false;
        if (parsed)
        {
            try { _ = CopilotStreamValidator.ParseResult(payload!); schema = true; }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
        }
        return new(shape, text.Length, Encoding.UTF8.GetByteCount(text), fences, leading, trailing, parsed, schema);
    }

    private static bool Parses(string text)
    {
        try { using var doc = JsonDocument.Parse(text); return true; }
        catch (JsonException) { return false; }
    }
    // Anchored at the start of the complete response, not a search for JSON fragments.
    // This observation is never passed to the result parser or returned as a payload.
    private static bool HasJsonPrefixWithTrailingText(string text)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            var reader = new Utf8JsonReader(bytes);
            using var doc = JsonDocument.ParseValue(ref reader);
            foreach (var b in bytes.AsSpan((int)reader.BytesConsumed))
                if (b is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')) return true;
            return false;
        }
        catch (JsonException) { return false; }
    }
    private static ResponseBoundary Boundary(string line, bool leading)
    {
        var trimmed = line.Trim(' ', '\t', '\r');
        return trimmed.Length == 0 ? ResponseBoundary.Empty : trimmed.StartsWith("```", StringComparison.Ordinal) ? ResponseBoundary.Fence
            : (leading ? trimmed[0] is '{' or '[' : trimmed[^1] is '}' or ']') ? ResponseBoundary.JsonDelimiter : ResponseBoundary.OtherText;
    }
}
