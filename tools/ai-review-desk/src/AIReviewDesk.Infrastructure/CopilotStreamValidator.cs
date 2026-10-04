using System.Text.Json;
using AIReviewDesk.Core;

namespace AIReviewDesk.Infrastructure;

/// <summary>Validates completed evidence, never a pre-launch authority gate.</summary>
public sealed class CopilotStreamValidator
{
    private readonly HashSet<string> allowed;
    private readonly string? expectedModel;
    private readonly string envelopeId;
    private readonly bool discoverEnvelope;
    public CopilotStreamValidator(IEnumerable<string>? expectedTools = null, string? expectedModel = null, string envelopeId = OutputEnvelope.RawJson, bool discoverEnvelope = false)
    { allowed = new(expectedTools ?? CopilotContract.AllowedTools, StringComparer.Ordinal); this.expectedModel = expectedModel; this.envelopeId = envelopeId; this.discoverEnvelope = discoverEnvelope; }
    public string? ObservedEnvelope { get; private set; }
    public bool EnvelopePassed { get; private set; }
    public bool JsonParsed { get; private set; }
    public bool SchemaPassed { get; private set; }
    public ResponseStructure? Structure { get; private set; }
    public OutputEnvelopeObservation OutputObservation(string probe) => new(probe, ObservedEnvelope, OutputEnvelope.Version, EnvelopePassed, JsonParsed, SchemaPassed, Structure);
    public string[]? ObservedTools { get; private set; }
    public bool ContractDrift { get; private set; }
    public string? Invalid => invalid;
    private readonly HashSet<string> disabledMcps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> toolCalls = new(StringComparer.Ordinal);
    private readonly HashSet<string> runningTools = new(StringComparer.Ordinal);
    private readonly HashSet<string> completedTools = new(StringComparer.Ordinal);
    private bool manifestSeen, terminalSeen;
    private string? response;
    private string? invalid;
    public string? Model { get; private set; }

    public void Accept(string line)
        => AcceptDocument(() => JsonDocument.Parse(line));

    public void AcceptFrame(ReadOnlyMemory<byte> frame)
        => AcceptDocument(() => JsonDocument.Parse(frame));

    private void AcceptDocument(Func<JsonDocument> parse)
    {
        if (invalid != null) return;
        try
        {
            using var document = parse();
            var root = document.RootElement;
            RequireUniqueProperties(root);
            var type = Text(root, "type") ?? throw new InvalidOperationException("Protocol frame has no event type.");
            if (terminalSeen) throw new InvalidOperationException("Protocol contains events after the terminal result.");
            var data = Property(root, "data");
            if (type.Contains("error", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Copilot reported a protocol error.");
            if (type is "session.tools_updated" or "session.usage_checkpoint")
            {
                // tools_updated may contain only model; the actual manifest is observed later in usage_checkpoint.
                Model = Text(data, "model") ?? Model;
                if (expectedModel != null && Model != null && Model != expectedModel) throw new InvalidOperationException("Requested and observed models differ.");
                ValidateManifests(data);
            }
            if (type == "session.mcp_servers_loaded")
            {
                var servers = Property(data, "servers");
                if (servers.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("Missing MCP status evidence.");
                foreach (var server in servers.EnumerateArray())
                {
                    var name = Text(server, "name");
                    if (name is not ("github-mcp-server" or "githubiq") || Text(server, "status") != "disabled")
                        throw new InvalidOperationException("Unexpected or enabled MCP server was observed.");
                    disabledMcps.Add(name);
                }
            }
            if (type is "tool.execution_start" or "assistant.tool_call_delta")
            {
                var name = Text(data, "toolName");
                ValidateTool(name);
                var id = Text(data, "toolCallId");
                if (string.IsNullOrEmpty(id) || completedTools.Contains(id))
                    throw new InvalidOperationException("Tool call identity is missing or reused.");
                if (toolCalls.TryGetValue(id, out var previous) && previous != name)
                    throw new InvalidOperationException("Tool call identity changed its tool name.");
                toolCalls[id] = name!;
                if (type == "tool.execution_start" && !runningTools.Add(id))
                    throw new InvalidOperationException("Duplicate tool execution start was observed.");
            }
            if (type == "tool.execution_complete")
            {
                // Real 1.0.91 completion events carry toolCallId, but omit toolName.
                // Accept only a completion correlated to a previously validated execution start.
                var id = Text(data, "toolCallId");
                if (id == null || !runningTools.Remove(id))
                    throw new InvalidOperationException("Tool completion has no matching execution start.");
                if (Text(data, "toolName") is { } name && name != toolCalls[id])
                    throw new InvalidOperationException("Tool completion changed its tool name.");
                completedTools.Add(id);
            }
            if (type == "assistant.message")
            {
                var requests = Property(data, "toolRequests");
                if (requests.ValueKind == JsonValueKind.Array)
                    foreach (var request in requests.EnumerateArray()) ValidateTool(Text(request, "name"));
                response = Text(data, "content") ?? response;
            }
            if (type == "result")
            {
                if (HasFailure(root) || HasFailure(data))
                    throw new InvalidOperationException("Copilot terminal result reported failure.");
                terminalSeen = true;
            }
            if (type.Contains("mcp", StringComparison.OrdinalIgnoreCase) && type != "session.mcp_servers_loaded")
                throw new InvalidOperationException("Unexpected MCP execution/startup evidence was observed.");
            if (new[] { "hook", "plugin", "extension", "lsp" }.Any(family => type.Contains(family, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Unexpected executable startup contribution was observed.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            ContractDrift = true;
            invalid = ex is JsonException ? "Copilot emitted malformed JSONL." : ex.Message;
        }
    }

    private static bool HasFailure(JsonElement value) =>
        Property(value, "is_error").ValueKind == JsonValueKind.True || Property(value, "isError").ValueKind == JsonValueKind.True ||
        Property(value, "success").ValueKind == JsonValueKind.False || Property(value, "error").ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) ||
        Text(value, "status") is "error" or "failed" or "cancelled" || Text(value, "subtype") is "error" or "error_during_execution";

    public ReviewResult Complete(int exitCode, bool cancelled)
    {
        if (cancelled) throw new OperationCanceledException("The review was cancelled; partial findings are not valid.");
        if (exitCode != 0) throw new InvalidOperationException("Copilot did not complete successfully.");
        if (invalid != null) throw new InvalidOperationException(invalid);
        try
        {
            if (!terminalSeen) throw new InvalidOperationException("Copilot returned no terminal result.");
            if (runningTools.Count != 0) throw new InvalidOperationException("Copilot returned incomplete tool execution evidence.");
            if (!manifestSeen) throw new InvalidOperationException("The completed run has no usable tool manifest.");
            if (!disabledMcps.SetEquals(["github-mcp-server", "githubiq"])) throw new InvalidOperationException("Disabled built-in MCP evidence is missing.");
            if (expectedModel != null && Model == null) throw new InvalidOperationException("Requested model has no observed runtime model evidence.");
            return ParseFinalText(response ?? throw new InvalidOperationException("Copilot returned no structured findings."));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { ContractDrift = true; throw; }
    }

    private ReviewResult ParseFinalText(string text)
    {
        Structure = ResponseStructureScanner.Inspect(text);
        var selected = envelopeId;
        if (discoverEnvelope)
        {
            // Always attempt the canonical complete raw document first. Only a known exact
            // presentation structure may then be classified; never search for JSON fragments.
            try { using var canonical = JsonDocument.Parse(text); selected = OutputEnvelope.RawJson; }
            catch (JsonException)
            {
                if (OutputEnvelope.TryExtractFence(text, out _)) selected = OutputEnvelope.SingleJsonFence;
            }
        }
        ObservedEnvelope = selected;
        if (selected == OutputEnvelope.RawJson && OutputEnvelope.TryExtractFence(text, out _))
        {
            ObservedEnvelope = OutputEnvelope.SingleJsonFence;
            throw new InvalidOperationException("The model returned an output envelope that differs from its certified contract. Compatibility retesting is required.");
        }
        var payload = OutputEnvelope.Extract(text, selected);
        EnvelopePassed = true;
        try { using var document = JsonDocument.Parse(payload); JsonParsed = true; }
        catch (JsonException)
        {
            throw new InvalidOperationException(selected == OutputEnvelope.SingleJsonFence ? "The fenced JSON payload was malformed. No JSON repair is permitted." : "The raw JSON payload was malformed or contained text outside the JSON result. No JSON repair is permitted.");
        }
        try { var result = ParseResult(payload); SchemaPassed = true; return result; }
        catch (InvalidOperationException) { throw new InvalidOperationException("The JSON payload did not satisfy the findings schema."); }
    }

    private void ValidateManifests(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.Object)
            foreach (var property in data.EnumerateObject())
            {
                if (property.Name == "tools")
                {
                    if (property.Value.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("Malformed tool manifest.");
                    var names = property.Value.EnumerateArray().Select(t => Text(t, "name") ?? throw new InvalidOperationException("Malformed tool manifest entry.")).ToArray();
                    ObservedTools = names;
                    if (names.Length != allowed.Count || !allowed.SetEquals(names)) throw new InvalidOperationException("Copilot tool manifest does not match the exact verified read-only tool set.");
                    manifestSeen = true;
                }
                else ValidateManifests(property.Value);
            }
        else if (data.ValueKind == JsonValueKind.Array)
            foreach (var item in data.EnumerateArray()) ValidateManifests(item);
    }

    private void ValidateTool(string? tool)
    {
        if (tool == null || !allowed.Contains(tool)) throw new InvalidOperationException("Unexpected or ambiguous model tool execution was observed.");
    }
    private static JsonElement Property(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) ? value : default;
    private static string? Text(JsonElement element, string key) => Property(element, key) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    internal static void RequireUniqueProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in value.EnumerateObject())
            {
                if (!keys.Add(p.Name)) throw new InvalidOperationException("Duplicate JSON property makes the result ambiguous.");
                RequireUniqueProperties(p.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) RequireUniqueProperties(item);
    }

    public static ReviewResult ParseResult(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        RequireUniqueProperties(root);
        if (root.ValueKind != JsonValueKind.Object || Property(root, "findings").ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Copilot response has no structured findings array.");
        foreach (var p in root.EnumerateObject()) if (p.Name is not ("findings" or "summary" or "limitations")) throw new InvalidOperationException("Unexpected structured result field.");
        var findings = new List<ReviewFinding>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in Property(root, "findings").EnumerateArray())
        {
            string Required(string key) => Text(f, key) is { Length: > 0 } value ? value : throw new InvalidOperationException($"Finding is missing {key}.");
            var id = Required("id");
            if (!ids.Add(id)) throw new InvalidOperationException("Finding IDs are not unique.");
            var severity = Required("severity");
            var certainty = Required("certainty");
            if (severity is not ("critical" or "high" or "medium" or "low" or "info") || certainty is not ("confirmed" or "probable" or "possible"))
                throw new InvalidOperationException("Finding severity or certainty is invalid.");
            var line = Property(f, "line");
            if (line.ValueKind is not (JsonValueKind.Number or JsonValueKind.Null) || (line.ValueKind == JsonValueKind.Number && (!line.TryGetInt32(out var n) || n < 1)))
                throw new InvalidOperationException("Finding location is invalid.");
            var file = Property(f, "file");
            if (file.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) throw new InvalidOperationException("Finding file is invalid.");
            findings.Add(new ReviewFinding { Id = id, Severity = severity, Certainty = certainty, Category = Required("category"), Title = Required("title"), File = Text(f, "file"), Line = line.ValueKind == JsonValueKind.Number ? line.GetInt32() : null, Evidence = Required("evidence"), Impact = Required("impact"), Recommendation = Required("recommendation") });
            if (findings.Count > 200) throw new InvalidOperationException("Too many findings for a compact review.");
        }
        var limitations = Property(root, "limitations");
        if (limitations.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Array)) throw new InvalidOperationException("Malformed result limitations.");
        return new ReviewResult { Summary = Text(root, "summary") ?? "", Findings = findings, Limitations = limitations.ValueKind == JsonValueKind.Array ? limitations.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ? v.GetString()! : throw new InvalidOperationException("Malformed limitation.")).ToArray() : [] };
    }
}
