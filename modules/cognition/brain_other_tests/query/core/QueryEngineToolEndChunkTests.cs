namespace Core.Tests.Query;

/// <summary>
/// 工具调用结束事件构造门禁（TASK108 Q7）— 真机现象：Edit 改文件成功、文件内容确实变了，
/// 但 TUI 只有 <c>⎿ The file … has been updated successfully.</c>，**没有任何 diff 预览行**。
/// 根因：QueryEngine 内联构造 ToolCallEnd 时漏了 <c>StructuredPatch</c>，
/// FileToolHandlers 填好的 patch 在事件层被丢掉，下游 ProjectPatch/DiffResultItem 全链路拿不到。
/// 门禁锁：patch 透传、无 patch 时为 null、错误文本仍带 `Error: ` 前缀（摘要来源契约）。
/// </summary>
public class QueryEngineToolEndChunkTests
{
    [Fact]
    public void BuildToolCallEndChunk_ShouldCarryStructuredPatch()
    {
        var hunk = new StructuredPatchHunk
        {
            OldStart = 2,
            OldLines = 1,
            NewStart = 2,
            NewLines = 2,
            Header = "@@ -2,1 +2,2 @@",
            Lines =
            [
                new PatchLine { Type = PatchLineType.Removed, Content = "第二行旧内容", OldLineNumber = 2 },
                new PatchLine { Type = PatchLineType.Added, Content = "第二行新内容", NewLineNumber = 2 },
            ],
        };
        var result = new ToolResult
        {
            Content = [new ToolContent { Type = ToolContentType.Text, Text = "updated" }],
            StructuredPatch = [hunk],
        };

        var chunk = QueryEngine.BuildToolCallEndChunk("edit", "call_1", result, 1);

        chunk.Type.Should().Be(AgentStreamChunkType.ToolCallEnd);
        chunk.ToolName.Should().Be("edit");
        chunk.ToolCallId.Should().Be("call_1");
        chunk.ToolCallNumber.Should().Be(1);
        chunk.ToolResultText.Should().Be("updated");
        var patch = chunk.StructuredPatch;
        if (patch is null)
        {
            throw new Xunit.Sdk.XunitException("StructuredPatch 未随 ToolCallEnd 事件下发（diff 预览会全空）");
        }

        patch.Should().ContainSingle().Which.Header.Should().Be("@@ -2,1 +2,2 @@");
        patch[0].Lines.Should().HaveCount(2);
    }

    [Fact]
    public void BuildToolCallEndChunk_NoPatch_ShouldBeNull()
    {
        var result = new ToolResult
        {
            Content = [new ToolContent { Type = ToolContentType.Text, Text = "ok" }],
        };

        var chunk = QueryEngine.BuildToolCallEndChunk("bash", "call_2", result, 2);

        chunk.StructuredPatch.Should().BeNull();
    }

    [Fact]
    public void BuildToolCallEndChunk_ErrorResult_ShouldPrefixError()
    {
        var result = new ToolResult
        {
            Content = [new ToolContent { Type = ToolContentType.Text, Text = "{\"type\":\"permission_denied\",\"message\":\"拒绝\"}" }],
            IsError = true,
        };

        var chunk = QueryEngine.BuildToolCallEndChunk("bash", "call_3", result, 3);

        chunk.IsToolError.Should().BeTrue();
        chunk.ToolResultText.Should().Be("Error: {\"type\":\"permission_denied\",\"message\":\"拒绝\"}");
    }
}
