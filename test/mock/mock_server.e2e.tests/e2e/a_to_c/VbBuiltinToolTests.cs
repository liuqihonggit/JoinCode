namespace MockServer.E2E.Tests;

/// <summary>
/// VB 组内置工具 E2E 测试 — 覆盖 vb01-vb35 内置工具行为助记符。
/// 每个助记符对应一个 ConversationScript，验证内置工具的特定行为信号。
/// vb03/vb05/vb09/vb14 因 ConversationScript 无法表达真实超时/大输出/大文件/未读先写状态，
/// 标注 TODO 暂用模拟方式覆盖工具调用链路，真实场景需手动 exe 验证（ADR 0080）。
/// </summary>
[Collection("MockServerShared")]
public sealed class VbBuiltinToolTests : CoverageTestBase {
    /// <summary>
    /// 初始化 VB 组内置工具测试实例。
    /// </summary>
    /// <param name="output">xUnit 测试输出辅助器。</param>
    /// <param name="fixture">共享 MockServer 固件。</param>
    public VbBuiltinToolTests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    /// <summary>
    /// VB 组内置工具测试数据 — 引用 VbBuiltinToolScripts 中的脚本。
    /// 每条数据包含 (ConversationScript, vbId) 用于参数化测试。
    /// 覆盖 vb01-vb35 共 35 个助记符。
    /// </summary>
    public static IEnumerable<object[]> VbBuiltinToolScriptsData => new[]
    {
        new object[] { VbBuiltinToolScripts.Vb01BashSuccess, "vb01" },
        new object[] { VbBuiltinToolScripts.Vb02BashFailure, "vb02" },
        new object[] { VbBuiltinToolScripts.Vb03BashTimeout, "vb03" },
        new object[] { VbBuiltinToolScripts.Vb04BashCommandNotFound, "vb04" },
        new object[] { VbBuiltinToolScripts.Vb05BashLargeOutputTruncated, "vb05" },
        new object[] { VbBuiltinToolScripts.Vb06ReadSuccess, "vb06" },
        new object[] { VbBuiltinToolScripts.Vb07ReadFileNotFound, "vb07" },
        new object[] { VbBuiltinToolScripts.Vb08ReadDirectoryNotFile, "vb08" },
        new object[] { VbBuiltinToolScripts.Vb09ReadTokenLimitExceeded, "vb09" },
        new object[] { VbBuiltinToolScripts.Vb10ReadBinaryRejected, "vb10" },
        new object[] { VbBuiltinToolScripts.Vb11ReadImageBase64, "vb11" },
        new object[] { VbBuiltinToolScripts.Vb12WriteCreateNewFile, "vb12" },
        new object[] { VbBuiltinToolScripts.Vb13WriteOverwriteExisting, "vb13" },
        new object[] { VbBuiltinToolScripts.Vb14WriteWithoutReadRejected, "vb14" },
        new object[] { VbBuiltinToolScripts.Vb15EditSuccess, "vb15" },
        new object[] { VbBuiltinToolScripts.Vb16EditOldStringNotFound, "vb16" },
        new object[] { VbBuiltinToolScripts.Vb17EditReplaceAll, "vb17" },
        new object[] { VbBuiltinToolScripts.Vb18GlobMatch, "vb18" },
        new object[] { VbBuiltinToolScripts.Vb19GlobNoMatch, "vb19" },
        new object[] { VbBuiltinToolScripts.Vb20GrepMatch, "vb20" },
        new object[] { VbBuiltinToolScripts.Vb21GrepNoMatch, "vb21" },
        new object[] { VbBuiltinToolScripts.Vb22TodoWriteCreate, "vb22" },
        new object[] { VbBuiltinToolScripts.Vb23TodoListList, "vb23" },
        new object[] { VbBuiltinToolScripts.Vb24TodoUpdateStatus, "vb24" },
        new object[] { VbBuiltinToolScripts.Vb25TaskCreate, "vb25" },
        new object[] { VbBuiltinToolScripts.Vb26TaskListList, "vb26" },
        new object[] { VbBuiltinToolScripts.Vb27SkillLoad, "vb27" },
        new object[] { VbBuiltinToolScripts.Vb28SkillNotFound, "vb28" },
        new object[] { VbBuiltinToolScripts.Vb29WebfetchSuccess, "vb29" },
        new object[] { VbBuiltinToolScripts.Vb30WebfetchRedirect, "vb30" },
        new object[] { VbBuiltinToolScripts.Vb31WebfetchFailure, "vb31" },
        new object[] { VbBuiltinToolScripts.Vb32QuestionAskAndAnswer, "vb32" },
        new object[] { VbBuiltinToolScripts.Vb33QuestionOptionValidation, "vb33" },
        new object[] { VbBuiltinToolScripts.Vb34AgentCreateSubAgent, "vb34" },
        new object[] { VbBuiltinToolScripts.Vb35AgentStatusQuery, "vb35" },
    };

    /// <summary>
    /// 验证 VB 组内置工具脚本所有断言通过。
    /// 覆盖 vb01-vb35 共 35 个助记符，包含成功/失败/错误诊断场景。
    /// </summary>
    /// <param name="script">对话脚本。</param>
    /// <param name="vbId">助记符编号（vb01-vb35）。</param>
    [Theory]
    [MemberData(nameof(VbBuiltinToolScriptsData))]
    public async Task VbBuiltinToolScript_ShouldPassAllAsserts(ConversationScript script, string vbId) {
        Output.WriteLine($"[VB] 开始验证 {vbId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VB] {vbId} 验证通过");
    }
}
