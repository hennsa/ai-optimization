namespace AIReviewDesk.Core;

/// <summary>Presentation contracts only. No JSON interpretation, repair or execution.</summary>
public static class OutputEnvelope
{
    public const string RawJson = "raw-json-v1";
    public const string SingleJsonFence = "single-json-fence-v1";
    public const int Version = 1;
    public static bool Supported(string? id, int version) => version == Version && id is RawJson or SingleJsonFence;
    public static string Label(string? id) => id switch { RawJson => "Raw JSON", SingleJsonFence => "JSON code block", _ => "Unverified format" };

    public static string Extract(string text, string id)
    {
        if (id == RawJson) return text; // Preserve every existing raw-parser assumption.
        if (id != SingleJsonFence) throw new InvalidOperationException("Unsupported output envelope contract. Compatibility retesting is required.");
        if (TryExtractFence(text, out var payload)) return payload;
        throw new InvalidOperationException("The model returned an output envelope that differs from its certified contract. Compatibility retesting is required.");
    }

    // A small line scanner, not a Markdown interpreter. Only JSON whitespace is normalized.
    public static bool TryExtractFence(string text, out string payload)
    {
        payload = "";
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim(' ', '\t', '\r', '\n');
        if (normalized.Length < 12 || !normalized.StartsWith("```json\n", StringComparison.Ordinal) || !normalized.EndsWith("\n```", StringComparison.Ordinal)) return false;
        var body = normalized[8..^4];
        foreach (var line in body.Split('\n'))
        {
            // A fence line inside the payload is ambiguous. Backticks within JSON strings remain data.
            if (line.TrimStart(' ', '\t').StartsWith("```", StringComparison.Ordinal)) return false;
        }
        payload = body; // No mutation of the JSON payload, except CRLF -> LF above.
        return true;
    }
}

/// <summary>Compact stages observed for one completed certification call; never contains returned text.</summary>
public sealed record OutputEnvelopeObservation(string Case, string? EnvelopeId, int EnvelopeVersion, bool EnvelopePassed, bool JsonParsed, bool SchemaPassed);
