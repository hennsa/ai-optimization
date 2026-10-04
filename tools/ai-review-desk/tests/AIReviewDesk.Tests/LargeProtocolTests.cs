using System.Text;
using System.Text.Json;
using AIReviewDesk.Core;
using AIReviewDesk.Infrastructure;
using Repo = AIReviewDesk.Tests.GitReviewContextTests.TemporaryGitRepository;

namespace AIReviewDesk.Tests;

public sealed class LargeProtocolTests
{
    private static byte[] Frame(string type, object data) => JsonSerializer.SerializeToUtf8Bytes(new { type, data }).Concat([(byte)'\n']).ToArray();
    private static byte[] PaddedFrame(int bytes) => Encoding.UTF8.GetBytes("{\"type\":\"user.message\",\"data\":{\"content\":\"" + new string('a', bytes - 46) + "\"}}\n");

    [Fact]
    public async Task Thousands_of_files_produce_bounded_production_context_and_large_echo_without_persistence()
    {
        using var repo = new Repo();
        repo.Write("base.txt", "base"); repo.Commit("initial");
        for (var i = 0; i < 3000; i++) repo.Write($"generated/file-{i:D4}.txt", new string('"', 600));
        var input = await new GitReviewContext().PrepareAsync(repo.Project, ReviewScope.WorkingChanges);
        Assert.Equal(3000, input.Snapshot.UntrackedCount);
        Assert.InRange(input.Context.Length, 1_400_000, GitReviewContext.MaxContextCharacters);
        var prompt = PromptComposer.Compose(input, ["standard"]);
        var echo = Frame("user.message", new { content = prompt });
        Assert.True(echo.Length > 2 * 1024 * 1024);
        Assert.True(echo.Length < CopilotProcess.MaxFrameBytes);
        var validator = new CopilotStreamValidator();
        using (var stream = new MemoryStream(echo)) await CopilotProcess.ConsumeFramesAsync(stream, validator.AcceptFrame);
        validator.Accept("{\"type\":\"tool.execution_start\",\"data\":{\"toolCallId\":\"read\",\"toolName\":\"view\"}}");
        using (var stream = new MemoryStream(Frame("tool.execution_complete", new { toolCallId = "read", success = true, result = new { content = "display", detailedContent = new string('x', 3 * 1024 * 1024) } })))
            await CopilotProcess.ConsumeFramesAsync(stream, validator.AcceptFrame);
        validator.Accept("{\"type\":\"session.usage_checkpoint\",\"data\":{\"tools\":[{\"name\":\"view\"},{\"name\":\"grep\"},{\"name\":\"glob\"}]}}");
        validator.Accept("{\"type\":\"session.mcp_servers_loaded\",\"data\":{\"servers\":[{\"name\":\"github-mcp-server\",\"status\":\"disabled\"},{\"name\":\"githubiq\",\"status\":\"disabled\"}]}}");
        validator.Accept("{\"type\":\"assistant.message\",\"data\":{\"content\":\"{\\\"findings\\\":[],\\\"summary\\\":\\\"Accepted\\\"}\"}}");
        validator.Accept("{\"type\":\"result\"}");
        var result = validator.Complete(0, false);
        var data = Path.Combine(Path.GetTempPath(), "AIReviewDesk.LargeTests", Guid.NewGuid().ToString("N"));
        try
        {
            var record = new ReviewRecord { ProjectId = repo.Project.Id, Status = ReviewStatus.Completed, Result = result };
            await new ReviewHistoryStore(data).SaveAsync(record);
            var saved = await File.ReadAllTextAsync(Directory.GetFiles(data, "*.json", SearchOption.AllDirectories).Single());
            Assert.DoesNotContain("generated/file", saved); Assert.DoesNotContain("detailedContent", saved);
            Assert.True(saved.Length < 5000);
        }
        finally { if (Directory.Exists(data)) Directory.Delete(data, true); }
    }

    [Fact]
    public async Task Frame_at_new_ceiling_is_valid_and_total_supported_stream_is_consumed()
    {
        var frame = PaddedFrame(CopilotProcess.MaxFrameBytes + 1);
        Assert.Equal(CopilotProcess.MaxFrameBytes + 1, frame.Length); // Final LF is transport, not frame data.
        var tail = Frame("session.info", new { content = new string('b', 3 * 1024 * 1024) });
        using var stream = new MemoryStream(frame.Concat(tail).ToArray());
        var events = 0;
        var validator = new CopilotStreamValidator();
        await CopilotProcess.ConsumeFramesAsync(stream, bytes => { events++; validator.AcceptFrame(bytes); });
        Assert.Equal(2, events); Assert.Null(validator.Invalid);
    }

    [Fact]
    public async Task One_byte_beyond_frame_bound_fails_with_size_only_diagnostics()
    {
        using var stream = new MemoryStream(PaddedFrame(CopilotProcess.MaxFrameBytes + 2));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotProcess.ConsumeFramesAsync(stream, _ => Assert.Fail("Oversized frame delivered")));
        Assert.Contains("oversized protocol frame", error.Message); Assert.Contains("bytes", error.Message);
        Assert.DoesNotContain("aaaa", error.Message); Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task Many_supported_frames_cannot_exceed_total_transport_budget()
    {
        var frame = PaddedFrame(1024 * 1024);
        using var stream = new MemoryStream(Enumerable.Range(0, 17).SelectMany(_ => frame).ToArray());
        var count = 0;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotProcess.ConsumeFramesAsync(stream, _ => count++));
        Assert.Contains("total transport", error.Message); Assert.InRange(count, 15, 16);
    }

    [Fact]
    public async Task Cancellation_while_consuming_large_frame_stops_without_delivering_partial_content()
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new CancellingStream(PaddedFrame(8 * 1024 * 1024), cancellation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CopilotProcess.ConsumeFramesAsync(stream, _ => Assert.Fail("Partial frame delivered"), cancellation.Token));
        Assert.True(stream.Position < stream.Length);
    }

    [Fact]
    public async Task Invalid_utf8_in_large_payload_is_rejected_before_any_frame_is_delivered()
    {
        var bytes = PaddedFrame(3 * 1024 * 1024);
        bytes[bytes.Length - 5] = 0xff;
        using var stream = new MemoryStream(bytes);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotProcess.ConsumeFramesAsync(stream, _ => Assert.Fail("Invalid UTF-8 frame delivered")));
        Assert.Equal("Copilot emitted invalid UTF-8.", error.Message); Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData("{\"type\":\"user.message\"}")]
    [InlineData("{partial")]
    public async Task Unterminated_jsonl_fails_closed(string partial)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(partial));
        await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotProcess.ConsumeFramesAsync(stream, _ => Assert.Fail("Partial frame delivered")));
    }

    [Fact]
    public async Task Plain_version_output_can_end_without_newline_without_weakening_jsonl()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("GitHub Copilot CLI 1.0.91."));
        string? version = null;
        await CopilotProcess.ConsumeFramesAsync(stream, frame => version = CopilotContract.ParseVersion(Encoding.UTF8.GetString(frame.Span)), requireTerminator: false);
        Assert.Equal("1.0.91", version);
    }

    [Fact]
    public async Task Large_final_assistant_output_still_obeys_strict_findings_schema()
    {
        var validator = new CopilotStreamValidator();
        var content = JsonSerializer.Serialize(new { findings = Array.Empty<object>(), summary = new string('s', 3 * 1024 * 1024), unexpected = true });
        using var stream = new MemoryStream(Frame("assistant.message", new { content }));
        await CopilotProcess.ConsumeFramesAsync(stream, validator.AcceptFrame);
        validator.Accept("{\"type\":\"session.usage_checkpoint\",\"data\":{\"tools\":[{\"name\":\"view\"},{\"name\":\"grep\"},{\"name\":\"glob\"}]}}");
        validator.Accept("{\"type\":\"session.mcp_servers_loaded\",\"data\":{\"servers\":[{\"name\":\"github-mcp-server\",\"status\":\"disabled\"},{\"name\":\"githubiq\",\"status\":\"disabled\"}]}}");
        validator.Accept("{\"type\":\"result\"}");
        var error = Assert.Throws<InvalidOperationException>(() => validator.Complete(0, false));
        Assert.Contains("findings schema", error.Message);
        Assert.True(validator.JsonParsed); Assert.False(validator.SchemaPassed);
        Assert.Throws<InvalidOperationException>(() => CopilotStreamValidator.ParseResult(content));
    }

    private sealed class CancellingStream(byte[] bytes, CancellationTokenSource cancellation) : MemoryStream(bytes)
    {
        private int reads;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var result = base.ReadAsync(buffer, cancellationToken);
            if (++reads == 4) cancellation.Cancel();
            return result;
        }
    }
}
