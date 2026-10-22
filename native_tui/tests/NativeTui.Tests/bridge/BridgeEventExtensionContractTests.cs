namespace NativeTui.Tests.Bridge;

/// <summary>
/// 新增事件 JSON 契约锁定测试 — TokenUsage（token_usage）与 ToolResult 扩展字段
/// （raw_output/hidden_lines）的序列化形态防漂移：
/// 可空字段 null 不输出（WhenWritingNull）、hiddenLines 默认 0 不输出（WhenWritingDefault）—
/// 旧 JSON golden 逐字节零破坏；旧引擎 JSON 反序列化兼容（缺字段 → 默认值）。
/// 注意：native_tui 侧 BridgeJsonContext.Default 不带 UnsafeRelaxedJsonEscaping —
/// 断言字符串统一 ASCII（中文会转义为 \uXXXX）。
/// </summary>
public sealed class BridgeEventExtensionContractTests
{
    /// <summary>TokenUsage 全字段序列化 — input/output 恒输出 + context 双通道可选输出。</summary>
    [Fact]
    public void SerializeEvent_TokenUsage_FullFields()
    {
        var line = BridgeProtocol.SerializeEvent(new BridgeEvent.TokenUsage(
            InputTokens: 120, OutputTokens: 45, ContextTokens: 980, ContextLimitTokens: 131072));

        Assert.Equal(
            "{\"type\":\"token_usage\",\"inputTokens\":120,\"outputTokens\":45,\"contextTokens\":980,\"contextLimitTokens\":131072}",
            line);
    }

    /// <summary>TokenUsage context 通道 null — 字段省略（WhenWritingNull，旧引擎兼容形态）。</summary>
    [Fact]
    public void SerializeEvent_TokenUsage_NullContextOmitted()
    {
        var line = BridgeProtocol.SerializeEvent(new BridgeEvent.TokenUsage(10, 5));

        Assert.Equal("{\"type\":\"token_usage\",\"inputTokens\":10,\"outputTokens\":5}", line);
    }

    /// <summary>ToolResult 带原始输出与折叠隐藏行数 — rawOutput/hiddenLines 字段输出（camelCase）。</summary>
    [Fact]
    public void SerializeEvent_ToolResult_CarriesRawOutputAndHiddenLines()
    {
        var line = BridgeProtocol.SerializeEvent(new BridgeEvent.ToolResult(
            "5 lines", RawOutput: "a\nb\nc\nd\ne", HiddenLines: 2));

        Assert.Equal(
            "{\"type\":\"tool_result\",\"summary\":\"5 lines\",\"rawOutput\":\"a\\nb\\nc\\nd\\ne\",\"hiddenLines\":2}",
            line);
    }

    /// <summary>ToolResult 默认构造 — rawOutput null 省略 + hiddenLines 0 省略，旧 JSON golden 逐字节不变。</summary>
    [Fact]
    public void SerializeEvent_ToolResult_DefaultsOmitted_LegacyGolden()
    {
        var line = BridgeProtocol.SerializeEvent(new BridgeEvent.ToolResult("41 lines"));

        Assert.Equal("{\"type\":\"tool_result\",\"summary\":\"41 lines\"}", line);
    }

    /// <summary>TokenUsage roundtrip — 序列化行反序列化回读四字段全量（context 通道 null 保持 null）。</summary>
    [Fact]
    public void TryReadEvent_TokenUsage_RoundtripSurvives()
    {
        var usage = new BridgeEvent.TokenUsage(120, 45, 980, 131072);

        var reader = new StringReader(BridgeProtocol.SerializeEvent(usage) + "\n");

        var result = BridgeProtocol.TryReadEvent(reader, out var actual, out var diagnostic);
        Assert.Equal(BridgeReadResult.Event, result);
        Assert.Null(diagnostic);
        var parsed = Assert.IsType<BridgeEvent.TokenUsage>(actual);
        Assert.Equal(120L, parsed.InputTokens);
        Assert.Equal(45L, parsed.OutputTokens);
        Assert.Equal(980L, parsed.ContextTokens);
        Assert.Equal(131072L, parsed.ContextLimitTokens);
    }

    /// <summary>旧引擎形态（无 context 字段）反序列化 — ContextTokens/ContextLimitTokens 解析为 null 不报坏行。</summary>
    [Fact]
    public void TryReadEvent_TokenUsage_LegacyWithoutContextParsesNulls()
    {
        var reader = new StringReader("{\"type\":\"token_usage\",\"inputTokens\":100,\"outputTokens\":50}\n");

        var result = BridgeProtocol.TryReadEvent(reader, out var actual, out var diagnostic);

        Assert.Equal(BridgeReadResult.Event, result);
        Assert.Null(diagnostic);
        var parsed = Assert.IsType<BridgeEvent.TokenUsage>(actual);
        Assert.Equal(100L, parsed.InputTokens);
        Assert.Equal(50L, parsed.OutputTokens);
        Assert.Null(parsed.ContextTokens);
        Assert.Null(parsed.ContextLimitTokens);
    }

    /// <summary>旧引擎 ToolResult（无 raw_output/hidden_lines 字段）反序列化 — RawOutput null + HiddenLines 0 不报坏行。</summary>
    [Fact]
    public void TryReadEvent_ToolResult_LegacyJsonParsesDefaults()
    {
        var reader = new StringReader("{\"type\":\"tool_result\",\"summary\":\"41 lines\"}\n");

        var result = BridgeProtocol.TryReadEvent(reader, out var actual, out var diagnostic);

        Assert.Equal(BridgeReadResult.Event, result);
        Assert.Null(diagnostic);
        var parsed = Assert.IsType<BridgeEvent.ToolResult>(actual);
        Assert.Equal("41 lines", parsed.Summary);
        Assert.Null(parsed.RawOutput);
        Assert.Equal(0, parsed.HiddenLines);
    }

    /// <summary>ToolResult 扩展字段 roundtrip — rawOutput 多行文本 + hiddenLines 回读保真。</summary>
    [Fact]
    public void TryReadEvent_ToolResult_RawOutputRoundtripSurvives()
    {
        var toolResult = new BridgeEvent.ToolResult("5 lines", RawOutput: "a\nb\nc\nd\ne", HiddenLines: 2);

        var reader = new StringReader(BridgeProtocol.SerializeEvent(toolResult) + "\n");

        var result = BridgeProtocol.TryReadEvent(reader, out var actual, out _);

        Assert.Equal(BridgeReadResult.Event, result);
        var parsed = Assert.IsType<BridgeEvent.ToolResult>(actual);
        Assert.Equal("5 lines", parsed.Summary);
        Assert.Equal("a\nb\nc\nd\ne", parsed.RawOutput);
        Assert.Equal(2, parsed.HiddenLines);
    }
}
