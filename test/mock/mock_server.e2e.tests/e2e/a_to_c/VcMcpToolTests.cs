namespace MockServer.E2E.Tests;

/// <summary>
/// VC 组 MCP 工具+外部工具 E2E 测试 — 覆盖 vc01-vc25 行为助记符。
/// 每个助记符对应一个 ConversationScript，验证 MCP 工具/外部工具的特定行为信号。
/// vc01-vc03/vc07-vc09/vc18-vc19 为 CLI 子命令，NonInteractive 模式覆盖调用链路，
/// 真实子命令行为需手动 exe 验证（ADR 0080）。
/// vc21/vc23-vc25 需特殊环境（权限模式/ToolInterventionManager），标注 TODO。
/// </summary>
[Collection("MockServerShared")]
public sealed class VcMcpToolTests : CoverageTestBase {
    /// <summary>
    /// 初始化 VC 组 MCP 工具+外部工具测试实例。
    /// </summary>
    /// <param name="output">xUnit 测试输出辅助器。</param>
    /// <param name="fixture">共享 MockServer 固件。</param>
    public VcMcpToolTests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    /// <summary>
    /// VC 组 MCP 工具+外部工具测试数据 — 引用 VcMcpToolScripts 中的脚本。
    /// 每条数据包含 (ConversationScript, vcId) 用于参数化测试。
    /// 覆盖 vc01-vc25 共 25 个助记符。
    /// </summary>
    public static IEnumerable<object[]> VcMcpToolScriptsData => new[]
    {
        new object[] { VcMcpToolScripts.Vc01McpServeHttpStart, "vc01" },
        new object[] { VcMcpToolScripts.Vc02McpServeTokenAuth, "vc02" },
        new object[] { VcMcpToolScripts.Vc03McpServeProtocolVersion, "vc03" },
        new object[] { VcMcpToolScripts.Vc04McpCallSuccess, "vc04" },
        new object[] { VcMcpToolScripts.Vc05McpCallToolNotFound, "vc05" },
        new object[] { VcMcpToolScripts.Vc06McpCallParamValidationFail, "vc06" },
        new object[] { VcMcpToolScripts.Vc07McpListTools, "vc07" },
        new object[] { VcMcpToolScripts.Vc08McpListFilterByCategory, "vc08" },
        new object[] { VcMcpToolScripts.Vc09McpSchemaQuery, "vc09" },
        new object[] { VcMcpToolScripts.Vc10McpConnectAdd, "vc10" },
        new object[] { VcMcpToolScripts.Vc11McpDisconnectRemove, "vc11" },
        new object[] { VcMcpToolScripts.Vc12McpConnectPersist, "vc12" },
        new object[] { VcMcpToolScripts.Vc13McpConnectRestore, "vc13" },
        new object[] { VcMcpToolScripts.Vc14TodoToolViaMcp, "vc14" },
        new object[] { VcMcpToolScripts.Vc15NoteToolViaMcp, "vc15" },
        new object[] { VcMcpToolScripts.Vc16MemorySave, "vc16" },
        new object[] { VcMcpToolScripts.Vc17MemoryRetrieve, "vc17" },
        new object[] { VcMcpToolScripts.Vc18GhPrList, "vc18" },
        new object[] { VcMcpToolScripts.Vc19GhIssueList, "vc19" },
        new object[] { VcMcpToolScripts.Vc20CronCreate, "vc20" },
        new object[] { VcMcpToolScripts.Vc21McpPermissionDenied, "vc21" },
        new object[] { VcMcpToolScripts.Vc22McpbPackageLoad, "vc22" },
        new object[] { VcMcpToolScripts.Vc23ToolInterventionBlacklist, "vc23" },
        new object[] { VcMcpToolScripts.Vc24ToolInterventionDowngrade, "vc24" },
        new object[] { VcMcpToolScripts.Vc25ToolInterventionRedirect, "vc25" },
    };

    /// <summary>
    /// 验证 VC 组 MCP 工具+外部工具脚本所有断言通过。
    /// 覆盖 vc01-vc25 共 25 个助记符，包含 MCP 连接/调用/持久化/权限/外部工具场景。
    /// </summary>
    /// <param name="script">对话脚本。</param>
    /// <param name="vcId">助记符编号（vc01-vc25）。</param>
    [Theory]
    [MemberData(nameof(VcMcpToolScriptsData))]
    public async Task VcMcpToolScript_ShouldPassAllAsserts(ConversationScript script, string vcId) {
        Output.WriteLine($"[VC] 开始验证 {vcId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VC] {vcId} 验证通过");
    }
}
