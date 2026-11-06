namespace MockServer.E2E.Tests;

/// <summary>
/// VA 组 NDJSON 事件输出 E2E 测试 — 覆盖 va06/va08/va09 content block 类型。
/// va01-va05/va11-va15 由 Python 脚本 .xxx/test_va_ndjson_events.py 验证（system/init + result 字段）。
/// va07（thinking block）和 va10（tool_progress）因 MockResponseType 不支持，标注 TODO 暂未覆盖。
/// </summary>
[Collection("MockServerShared")]
public sealed class VaNdjsonEventTests : CoverageTestBase {
    /// <summary>
    /// 初始化 VA 组 NDJSON 事件测试实例。
    /// </summary>
    /// <param name="output">xUnit 测试输出辅助器。</param>
    /// <param name="fixture">共享 MockServer 固件。</param>
    public VaNdjsonEventTests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    /// <summary>
    /// VA 组 NDJSON content block 测试数据 — 引用 VaNdjsonEventScripts 中的脚本。
    /// 每条数据包含 (ConversationScript, vaId) 用于参数化测试。
    /// </summary>
    public static IEnumerable<object[]> VaNdjsonScripts => new[]
    {
        new object[] { VaNdjsonEventScripts.Va06TextBlock, "va06" },
        new object[] { VaNdjsonEventScripts.Va08ToolUseBlock, "va08" },
        new object[] { VaNdjsonEventScripts.Va09ToolResultBlock, "va09" },
    };

    /// <summary>
    /// 验证 VA 组 NDJSON content block 脚本所有断言通过。
    /// 覆盖 va06（text block）、va08（tool_use block）、va09（tool_result block）。
    /// </summary>
    /// <param name="script">对话脚本。</param>
    /// <param name="vaId">助记符编号（va06/va08/va09）。</param>
    [Theory]
    [MemberData(nameof(VaNdjsonScripts))]
    public async Task VaNdjsonScript_ShouldPassAllAsserts(ConversationScript script, string vaId) {
        Output.WriteLine($"[VA] 开始验证 {vaId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VA] {vaId} 验证通过");
    }
}
