namespace Host.Tests.Tui;

/// <summary>
/// ChunkToText 单元测试 — 验证 AgentStreamChunk 到显示文本的完整映射。
/// 覆盖全部 11 种 chunk 类型，确保 P0-1 修复后 TUI 能显示工具结果/进度/完成等。
/// </summary>
public class ChunkToTextTests
{
    [Fact]
    public void Content_ReturnsContent()
    {
        var chunk = Make(AgentStreamChunkType.Content, content: "hello world");
        Assert.Equal("hello world", TuiModeRunner.ChunkToText(chunk));
    }

    [Fact]
    public void ThinkingStart_ReturnsMarker()
    {
        var chunk = Make(AgentStreamChunkType.ThinkingStart);
        Assert.Equal("  [思考开始]", TuiModeRunner.ChunkToText(chunk));
    }

    [Fact]
    public void Thinking_ReturnsThinkingContent()
    {
        var chunk = Make(AgentStreamChunkType.Thinking, thinking: "分析中...");
        Assert.Equal("  [思考] 分析中...", TuiModeRunner.ChunkToText(chunk));
    }

    [Fact]
    public void ThinkingEnd_ReturnsMarker()
    {
        var chunk = Make(AgentStreamChunkType.ThinkingEnd);
        Assert.Equal("  [思考结束]", TuiModeRunner.ChunkToText(chunk));
    }

    [Fact]
    public void ToolCallStart_ReturnsToolName()
    {
        var chunk = Make(AgentStreamChunkType.ToolCallStart, toolName: "Read");
        Assert.Equal("  [工具] Read", TuiModeRunner.ChunkToText(chunk));
    }

    [Fact]
    public void ToolCallEnd_Success_ReturnsCheckmarkAndResult()
    {
        var chunk = Make(AgentStreamChunkType.ToolCallEnd, toolName: "Read", resultText: "文件内容", isError: false);
        Assert.Equal("  [工具] Read ✅ 文件内容", TuiModeRunner.ChunkToText(chunk));
    }

    [Fact]
    public void ToolCallEnd_Error_ReturnsCrossAndResult()
    {
        var chunk = Make(AgentStreamChunkType.ToolCallEnd, toolName: "Write", resultText: "权限不足", isError: true);
        Assert.Equal("  [工具] Write ❌ 权限不足", TuiModeRunner.ChunkToText(chunk));
    }

    [Fact]
    public void ToolCallEnd_LongResult_Truncated()
    {
        var longText = new string('a', 300);
        var chunk = Make(AgentStreamChunkType.ToolCallEnd, toolName: "Read", resultText: longText, isError: false);
        var result = TuiModeRunner.ChunkToText(chunk);
        Assert.Contains("✅", result);
        Assert.Contains("...", result);
    }

    [Fact]
    public void ToolProgress_ReturnsProgressMessage()
    {
        var chunk = Make(AgentStreamChunkType.ToolProgress, progress: "50%");
        Assert.Equal("  [进度] 50%", TuiModeRunner.ChunkToText(chunk));
    }

    [Fact]
    public void LoopDetected_ReturnsWarning()
    {
        var chunk = Make(AgentStreamChunkType.LoopDetected, loopCount: 3);
        Assert.Equal("  ⚠️ [循环检测] 触发 3 次", TuiModeRunner.ChunkToText(chunk));
    }

    [Fact]
    public void TimingSummary_ReturnsContent()
    {
        var chunk = Make(AgentStreamChunkType.TimingSummary, content: "耗时 1.2s");
        Assert.Equal("  ⏱️ 耗时 1.2s", TuiModeRunner.ChunkToText(chunk));
    }

    [Fact]
    public void Complete_WithUsage_ReturnsTokenAndModel()
    {
        var chunk = Make(AgentStreamChunkType.Complete, usage: new TokenUsage(100, 50), modelId: "gpt-4o");
        Assert.Equal("  ✅ 完成 │ Token: 150 │ 模型: gpt-4o", TuiModeRunner.ChunkToText(chunk));
    }

    [Fact]
    public void Complete_WithoutUsage_ReturnsSimpleComplete()
    {
        var chunk = Make(AgentStreamChunkType.Complete);
        Assert.Equal("  ✅ 完成", TuiModeRunner.ChunkToText(chunk));
    }

    [Fact]
    public void Error_ReturnsErrorContent()
    {
        var chunk = Make(AgentStreamChunkType.Error, content: "网络超时");
        Assert.Equal("  [错误] 网络超时", TuiModeRunner.ChunkToText(chunk));
    }

    private static QueryStreamChunk Make(
        AgentStreamChunkType type,
        string? content = null,
        string? thinking = null,
        string? toolName = null,
        string? resultText = null,
        bool isError = false,
        string? progress = null,
        int loopCount = 0,
        TokenUsage? usage = null,
        string? modelId = null)
    {
        return new QueryStreamChunk
        {
            Type = type,
            Content = content,
            ThinkingContent = thinking,
            ToolName = toolName,
            ToolResultText = resultText,
            IsToolError = isError,
            ProgressMessage = progress,
            LoopTriggerCount = loopCount,
            Usage = usage,
            ModelId = modelId,
        };
    }
}
