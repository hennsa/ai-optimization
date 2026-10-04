using System.Text.Json;
using AIReviewDesk.Core;
using AIReviewDesk.Infrastructure;

namespace AIReviewDesk.Tests;

public sealed class ResponseStructureTests
{
    private const string Json = "{\"findings\":[],\"summary\":\"PRIVATE_RESPONSE_MARKER\"}";
    public static IEnumerable<object[]> Shapes()
    {
        yield return [Json, ResponseShape.RawJson, 0, true, true];
        yield return ["```json\n" + Json + "\n```", ResponseShape.SingleJsonFence, 2, true, true];
        yield return ["```json\n{bad}\n```", ResponseShape.SingleJsonFence, 2, false, false];
        yield return ["{bad}", ResponseShape.MalformedJson, 0, false, false];
        yield return [Json + "\nprose", ResponseShape.SurroundingText, 0, false, false];
        yield return ["prose\n```json\n" + Json + "\n```", ResponseShape.SurroundingText, 2, false, false];
        yield return ["```json\n" + Json + "\n```\n```json\n{}\n```", ResponseShape.MultipleFenceBlocks, 4, false, false];
        yield return ["```JSON\n" + Json + "\n```", ResponseShape.UnsupportedFence, 2, false, false];
        yield return ["```javascript\n" + Json + "\n```", ResponseShape.UnsupportedFence, 2, false, false];
        yield return ["```json\n" + Json, ResponseShape.IncompleteFence, 1, false, false];
        yield return ["prose " + Json, ResponseShape.OtherPresentation, 0, false, false];
        yield return ["{}", ResponseShape.RawJson, 0, true, false];
    }
    [Theory, MemberData(nameof(Shapes))]
    public void Diagnostics_are_fixed_structural_facts_and_never_return_content(string text, ResponseShape shape, int fences, bool parsed, bool schema)
    {
        var facts = ResponseStructureScanner.Inspect(text);
        Assert.Equal(shape, facts.Shape); Assert.Equal(fences, facts.FenceLikeLines);
        Assert.Equal(parsed, facts.JsonParsed); Assert.Equal(schema, facts.SchemaPassed);
        Assert.Equal(text.Length, facts.Characters); Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(text), facts.Utf8Bytes);
        Assert.True(facts.IsValid);
        Assert.DoesNotContain("PRIVATE_RESPONSE_MARKER", JsonSerializer.Serialize(facts));
        Assert.DoesNotContain("PRIVATE_RESPONSE_MARKER", facts.ToString());
        Assert.All(typeof(ResponseStructure).GetProperties(), p => Assert.NotEqual(typeof(string), p.PropertyType));
    }
    [Fact]
    public void Diagnostic_classification_cannot_authorize_a_runtime_fence_fallback()
    {
        var validator = new CopilotStreamValidator(["view", "grep", "glob"], "future-model");
        validator.Accept("""{"type":"session.usage_checkpoint","data":{"model":"future-model","tools":[{"name":"view"},{"name":"grep"},{"name":"glob"}]}}""");
        validator.Accept("""{"type":"session.mcp_servers_loaded","data":{"servers":[{"name":"github-mcp-server","status":"disabled"},{"name":"githubiq","status":"disabled"}]}}""");
        validator.Accept(JsonSerializer.Serialize(new { type = "assistant.message", data = new { content = "```json\n" + Json + "\n```" } }));
        validator.Accept("""{"type":"result","data":{}}""");
        Assert.Throws<InvalidOperationException>(() => validator.Complete(0, false));
        Assert.True(validator.ContractDrift); Assert.False(validator.SchemaPassed);
        Assert.Equal(ResponseShape.SingleJsonFence, validator.Structure!.Shape);
        Assert.True(validator.Structure.SchemaPassed); // Diagnostic only, never acceptance authority.
        Assert.DoesNotContain("PRIVATE_RESPONSE_MARKER", JsonSerializer.Serialize(validator.OutputObservation("zero findings")));
    }
    [Fact]
    public void Invalid_fixed_facts_are_rejected()
    {
        var facts = ResponseStructureScanner.Inspect(Json);
        Assert.False((facts with { Shape = (ResponseShape)999 }).IsValid);
        Assert.False((facts with { Characters = -1 }).IsValid);
        Assert.False((facts with { JsonParsed = false }).IsValid);
    }
    [Fact]
    public async Task Failed_history_roundtrip_retains_structural_facts_only()
    {
        var root = Path.Combine(Path.GetTempPath(), "ARD-StructureHistory-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ReviewHistoryStore(root);
            var record = new ReviewRecord { ProjectId = Guid.NewGuid(), Status = ReviewStatus.Failed, OutputStructure = ResponseStructureScanner.Inspect(Json + " prose") };
            await store.SaveAsync(record);
            var loaded = (await store.LoadAsync(record.ProjectId)).Single();
            Assert.Equal(record.OutputStructure, loaded.OutputStructure);
            Assert.Equal(ResponseShape.SurroundingText, loaded.OutputStructure!.Shape);
            foreach (var file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories)) Assert.DoesNotContain("PRIVATE_RESPONSE_MARKER", File.ReadAllText(file));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
