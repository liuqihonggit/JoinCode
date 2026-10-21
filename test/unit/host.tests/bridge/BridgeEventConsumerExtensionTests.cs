namespace Host.Tests.Bridge;

/// <summary>
/// BridgeEventConsumer 新增投影测试 — 工具结果原始输出投影（raw_output/hidden_lines，
/// 对齐基准 terminal.ts renderTruncatedContent 的 PADDING 预截断语义：多行折叠、超长预截断防 64MB、
/// 单行零破坏旧 golden）+ OnDone 用量汇总发射（token_usage 紧随 assistant_message；usage null 不发）。
/// </summary>
public sealed class BridgeEventConsumerExtensionTests
{
    private static (BridgeEventConsumer Consumer, StringWriter Out) Create(Func<long>? wallClock = null)
    {
        var @out = new StringWriter();
        var consumer = new BridgeEventConsumer(@out, wallClock);
        return (consumer, @out);
    }

    /// <summary>多行结果（5 行）— rawOutput 全文携带 + hiddenLines = 5 − 3 = 2（折叠隐藏行数权威投影）。</summary>
    [Fact]
    public void OnToolEnd_MultiLineResult_CarriesRawOutputAndHiddenLines()
    {
        var (consumer, @out) = Create();

        consumer.OnToolEnd("Bash", "line1\nline2\nline3\nline4\nline5", "call-1", isError: false, patch: null);

        Assert.Equal(
            "{\"type\":\"tool_result\",\"summary\":\"line1\\nline2\\nline3\\nline4\\nline5\",\"rawOutput\":\"line1\\nline2\\nline3\\nline4\\nline5\",\"hiddenLines\":2}\n",
            @out.ToString());
    }

    /// <summary>单行结果 — rawOutput 不投影（summary 已完整表达），旧 JSON golden 逐字节零破坏。</summary>
    [Fact]
    public void OnToolEnd_SingleLineResult_OmitsRawOutputLegacyGolden()
    {
        var (consumer, @out) = Create();

        consumer.OnToolEnd("Read", "41 lines", "call-1", isError: false, patch: null);

        Assert.Equal("{\"type\":\"tool_result\",\"summary\":\"41 lines\"}\n", @out.ToString());
    }

    /// <summary>4 行结果 — 基准特例（折叠外仅多 1 行 → 全显）：rawOutput 全文 + hiddenLines 0 不输出。</summary>
    [Fact]
    public void OnToolEnd_FourLineResult_AllShownNoHidden()
    {
        var (consumer, @out) = Create();

        consumer.OnToolEnd("Bash", "a\nb\nc\nd", "call-1", isError: false, patch: null);

        Assert.Equal(
            "{\"type\":\"tool_result\",\"summary\":\"a\\nb\\nc\\nd\",\"rawOutput\":\"a\\nb\\nc\\nd\"}\n",
            @out.ToString());
    }

    /// <summary>超长多行结果（约 122KB）— 预截断防 64MB 语义：rawOutput 截至字符上限、事件行总长有界、hiddenLines 大值估算。</summary>
    [Fact]
    public void OnToolEnd_HugeMultiLineResult_PreTruncatedToLimit()
    {
        var (consumer, @out) = Create();
        var hugeResult = string.Join("\n", Enumerable.Range(0, 2000).Select(static _ => new string('x', 60)));

        consumer.OnToolEnd("Bash", hugeResult, "call-1", isError: false, patch: null);

        var line = @out.ToString().TrimEnd('\n');
        // 事件行总长有界（960 字符上限 + summary 截 200 + JSON 结构开销 — 原始 122KB 不进事件行）
        Assert.True(line.Length < 2000, $"事件行应预截断至有界长度，实际 {line.Length}");
        // 预截断后连续 x 不超过上限（960 = 3 行 × 80 宽 × 4 倍余量）
        Assert.DoesNotContain(new string('x', 961), line);
        // 折叠隐藏行数按估算（ceil(121999/80) ≈ 1525 行 − 3 预览 → 四位数量级大值）
        Assert.Matches("\"hiddenLines\":\\d{4}", line);
    }

    /// <summary>OnDone 带 usage — assistant_message 之后发 token_usage（回合收尾汇总行序）。</summary>
    [Fact]
    public void OnDone_WithUsage_EmitsTokenUsageAfterAssistantMessage()
    {
        var (consumer, @out) = Create();

        consumer.OnText("hi");
        consumer.OnDone(new TokenUsage(100, 50), "m");

        Assert.Equal(
            "{\"type\":\"token_delta\",\"length\":2,\"text\":\"hi\"}\n" +
            "{\"type\":\"assistant_message\",\"text\":\"hi\"}\n" +
            "{\"type\":\"token_usage\",\"inputTokens\":100,\"outputTokens\":50}\n",
            @out.ToString());
    }

    /// <summary>OnDone 无 usage — 不发 token_usage（无数据不噪音）。</summary>
    [Fact]
    public void OnDone_WithoutUsage_OmitsTokenUsage()
    {
        var (consumer, @out) = Create();

        consumer.OnText("hi");
        consumer.OnDone(null, null);

        Assert.Equal(
            "{\"type\":\"token_delta\",\"length\":2,\"text\":\"hi\"}\n" +
            "{\"type\":\"assistant_message\",\"text\":\"hi\"}\n",
            @out.ToString());
    }

    /// <summary>OnDone 无正文带 usage — 仍发 token_usage（纯工具回合的用量汇总不丢）。</summary>
    [Fact]
    public void OnDone_EmptyTextWithUsage_StillEmitsTokenUsage()
    {
        var (consumer, @out) = Create();

        consumer.OnDone(new TokenUsage(7, 3), null);

        Assert.Equal("{\"type\":\"token_usage\",\"inputTokens\":7,\"outputTokens\":3}\n", @out.ToString());
    }

    /// <summary>Reset 后新回合 — usage 逐回合发射无跨回合抑制（每回合 OnDone 独立汇总）。</summary>
    [Fact]
    public void OnDone_AcrossReset_EmitsTokenUsageEachTurn()
    {
        var (consumer, @out) = Create();

        consumer.OnDone(new TokenUsage(1, 1), null);
        consumer.Reset();
        consumer.OnText("t");
        consumer.OnDone(new TokenUsage(2, 2), null);

        Assert.Equal(2, @out.ToString().Split('\n').Count(static line => line.Contains("\"token_usage\"")));
    }
}
