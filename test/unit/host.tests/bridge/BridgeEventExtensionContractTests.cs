namespace Host.Tests.Bridge;

/// <summary>
/// 引擎侧新增事件 JSON 契约锁定测试 — TokenUsage（token_usage）与 ToolResult 扩展字段
/// （rawOutput/hiddenLines）的 jcc 侧序列化形态防漂移（与 native_tui 侧 BridgeEventExtensionContractTests 逐字节镜像）：
/// 可空字段 null 不输出（WhenWritingNull）、hiddenLines 默认 0 不输出（WhenWritingDefault）—
/// 旧 JSON golden 逐字节零破坏。
/// </summary>
public sealed class BridgeEventExtensionContractTests
{
    /// <summary>TokenUsage 全字段序列化 — input/output 恒输出 + context 双通道可选输出（camelCase）。</summary>
    [Fact]
    public void Serialize_TokenUsage_FullFields()
    {
        var json = JsonSerializer.Serialize(
            new CliBridge.BridgeEvent.TokenUsage(InputTokens: 120, OutputTokens: 45, ContextTokens: 980, ContextLimitTokens: 131072),
            CliBridge.BridgeJsonContext.Safe.BridgeEvent);

        Assert.Equal(
            "{\"type\":\"token_usage\",\"inputTokens\":120,\"outputTokens\":45,\"contextTokens\":980,\"contextLimitTokens\":131072}",
            json);
    }

    /// <summary>TokenUsage context 通道 null — 字段省略（WhenWritingNull；引擎暂无 context window 数据源的默认形态）。</summary>
    [Fact]
    public void Serialize_TokenUsage_NullContextOmitted()
    {
        var json = JsonSerializer.Serialize(
            new CliBridge.BridgeEvent.TokenUsage(10, 5),
            CliBridge.BridgeJsonContext.Safe.BridgeEvent);

        Assert.Equal("{\"type\":\"token_usage\",\"inputTokens\":10,\"outputTokens\":5}", json);
    }

    /// <summary>ToolResult 带原始输出与折叠隐藏行数 — rawOutput/hiddenLines 字段输出（camelCase）。</summary>
    [Fact]
    public void Serialize_ToolResult_CarriesRawOutputAndHiddenLines()
    {
        var json = JsonSerializer.Serialize(
            new CliBridge.BridgeEvent.ToolResult("5 lines", RawOutput: "a\nb\nc\nd\ne", HiddenLines: 2),
            CliBridge.BridgeJsonContext.Safe.BridgeEvent);

        Assert.Equal(
            "{\"type\":\"tool_result\",\"summary\":\"5 lines\",\"rawOutput\":\"a\\nb\\nc\\nd\\ne\",\"hiddenLines\":2}",
            json);
    }

    /// <summary>ToolResult 默认构造 — rawOutput null 省略 + hiddenLines 0 省略，旧 JSON golden 逐字节不变。</summary>
    [Fact]
    public void Serialize_ToolResult_DefaultsOmitted_LegacyGolden()
    {
        var json = JsonSerializer.Serialize(
            new CliBridge.BridgeEvent.ToolResult("41 lines"),
            CliBridge.BridgeJsonContext.Safe.BridgeEvent);

        Assert.Equal("{\"type\":\"tool_result\",\"summary\":\"41 lines\"}", json);
    }

    /// <summary>TokenUsage 反序列化回读 — 四字段全量保真（context 通道 null 保持 null）。</summary>
    [Fact]
    public void Deserialize_TokenUsage_ParsesBack()
    {
        var parsed = Assert.IsType<CliBridge.BridgeEvent.TokenUsage>(JsonSerializer.Deserialize(
            "{\"type\":\"token_usage\",\"inputTokens\":100,\"outputTokens\":50}",
            CliBridge.BridgeJsonContext.Safe.BridgeEvent));

        Assert.Equal(100L, parsed.InputTokens);
        Assert.Equal(50L, parsed.OutputTokens);
        Assert.Null(parsed.ContextTokens);
        Assert.Null(parsed.ContextLimitTokens);
    }

    /// <summary>旧 TUI 兼容验证 — 旧形态 ToolResult JSON（无新字段）引擎侧可解析（缺字段 → 默认值）。</summary>
    [Fact]
    public void Deserialize_LegacyToolResult_ParsesDefaults()
    {
        var parsed = Assert.IsType<CliBridge.BridgeEvent.ToolResult>(JsonSerializer.Deserialize(
            "{\"type\":\"tool_result\",\"summary\":\"41 lines\"}",
            CliBridge.BridgeJsonContext.Safe.BridgeEvent));

        Assert.Equal("41 lines", parsed.Summary);
        Assert.Null(parsed.RawOutput);
        Assert.Equal(0, parsed.HiddenLines);
    }
}
