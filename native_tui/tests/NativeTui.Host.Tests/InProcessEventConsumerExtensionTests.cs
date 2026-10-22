namespace NativeTui.Host.Tests;

/// <summary>
/// InProcessEventConsumer 新增投影测试 — 与 engine BridgeEventConsumerExtensionTests 逐行为镜像
///（唯一差异：stdout JSON 行 → 内存 BridgeEvent record）：
/// 工具结果原始输出投影（单行零变化 / 多行折叠 / 基准特例 4 行全显 / 超长预截断防 64MB）+
/// OnDone 用量汇总发射（token_usage 紧随 assistant_message；usage null 不发；空文本回合不丢汇总）。
/// </summary>
public sealed class InProcessEventConsumerExtensionTests
{
    /// <summary>多行结果（5 行）— RawOutput 全文携带 + HiddenLines = 2（折叠隐藏行数权威投影）。</summary>
    [Fact]
    public void OnToolEnd_MultiLineResult_CarriesRawOutputAndHiddenLines()
    {
        var consumer = new InProcessEventConsumer();

        consumer.OnToolEnd("Bash", "line1\nline2\nline3\nline4\nline5", "call-1", isError: false, patch: null);

        var result = Assert.IsType<BridgeEvent.ToolResult>(Assert.Single(consumer.DrainEvents()));
        Assert.Equal("line1\nline2\nline3\nline4\nline5", result.Summary);
        Assert.Equal("line1\nline2\nline3\nline4\nline5", result.RawOutput);
        Assert.Equal(2, result.HiddenLines);
    }

    /// <summary>单行结果 — RawOutput 不投影（summary 已表达）、HiddenLines 0（旧事件形态零变化）。</summary>
    [Fact]
    public void OnToolEnd_SingleLineResult_OmitsRawOutput()
    {
        var consumer = new InProcessEventConsumer();

        consumer.OnToolEnd("Read", "41 lines", "call-1", isError: false, patch: null);

        var result = Assert.IsType<BridgeEvent.ToolResult>(Assert.Single(consumer.DrainEvents()));
        Assert.Equal("41 lines", result.Summary);
        Assert.Null(result.RawOutput);
        Assert.Equal(0, result.HiddenLines);
    }

    /// <summary>4 行结果 — 基准特例（折叠外仅多 1 行 → 全显）：RawOutput 全文 + HiddenLines 0。</summary>
    [Fact]
    public void OnToolEnd_FourLineResult_AllShownNoHidden()
    {
        var consumer = new InProcessEventConsumer();

        consumer.OnToolEnd("Bash", "a\nb\nc\nd", "call-1", isError: false, patch: null);

        var result = Assert.IsType<BridgeEvent.ToolResult>(Assert.Single(consumer.DrainEvents()));
        Assert.Equal("a\nb\nc\nd", result.RawOutput);
        Assert.Equal(0, result.HiddenLines);
    }

    /// <summary>超长多行结果（约 122KB）— 预截断防 64MB：RawOutput 截至字符上限 + HiddenLines 大值估算。</summary>
    [Fact]
    public void OnToolEnd_HugeMultiLineResult_PreTruncatedToLimit()
    {
        var consumer = new InProcessEventConsumer();
        var hugeResult = string.Join("\n", Enumerable.Range(0, 2000).Select(static _ => new string('x', 60)));

        consumer.OnToolEnd("Bash", hugeResult, "call-1", isError: false, patch: null);

        var result = Assert.IsType<BridgeEvent.ToolResult>(Assert.Single(consumer.DrainEvents()));
        // 预截断至字符上限（960 = 3 行 × 80 宽 × 4 倍余量 — 对齐基准 maxChars 公式）
        Assert.Equal(960, result.RawOutput!.Length);
        // 折叠隐藏行数按估算（ceil(121999/80) ≈ 1525 行 − 3 预览 → 四位数量级大值）
        Assert.True(result.HiddenLines > 1000, $"隐藏行数应为估算大值，实际 {result.HiddenLines}");
    }

    /// <summary>OnDone 带 usage — assistant_message 之后发 TokenUsage（回合收尾汇总行序，与 bridge 侧镜像）。</summary>
    [Fact]
    public void OnDone_WithUsage_EmitsTokenUsageAfterAssistantMessage()
    {
        var consumer = new InProcessEventConsumer();

        consumer.OnText("hi");
        consumer.OnDone(usage: new TokenUsage(100, 50), modelId: "m");

        var events = consumer.DrainEvents();
        Assert.Equal(3, events.Count);
        Assert.IsType<BridgeEvent.TokenDelta>(events[0]);
        Assert.IsType<BridgeEvent.AssistantMessage>(events[1]);
        var usage = Assert.IsType<BridgeEvent.TokenUsage>(events[2]);
        Assert.Equal(100L, usage.InputTokens);
        Assert.Equal(50L, usage.OutputTokens);
        Assert.Null(usage.ContextTokens);
        Assert.Null(usage.ContextLimitTokens);
    }

    /// <summary>OnDone 无 usage — 不发 TokenUsage（无数据不噪音）。</summary>
    [Fact]
    public void OnDone_WithoutUsage_OmitsTokenUsage()
    {
        var consumer = new InProcessEventConsumer();

        consumer.OnText("hi");
        consumer.OnDone(usage: null, modelId: null);

        var events = consumer.DrainEvents();
        Assert.Equal(2, events.Count);
        Assert.DoesNotContain(events, e => e is BridgeEvent.TokenUsage);
    }

    /// <summary>OnDone 无正文带 usage — 仍发 TokenUsage（纯工具回合的用量汇总不丢）。</summary>
    [Fact]
    public void OnDone_EmptyTextWithUsage_StillEmitsTokenUsage()
    {
        var consumer = new InProcessEventConsumer();

        consumer.OnDone(usage: new TokenUsage(7, 3), modelId: null);

        var usage = Assert.IsType<BridgeEvent.TokenUsage>(Assert.Single(consumer.DrainEvents()));
        Assert.Equal(7L, usage.InputTokens);
        Assert.Equal(3L, usage.OutputTokens);
    }
}
