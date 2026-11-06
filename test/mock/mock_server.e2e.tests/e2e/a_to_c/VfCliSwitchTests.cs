namespace MockServer.E2E.Tests;

/// <summary>
/// VF 组命令行+开关+权限 E2E 测试 — 覆盖 vf01-vf25 助记符。
/// 每个助记符对应一个 ConversationScript，验证命令行开关/环境变量/子命令的特定行为信号。
/// vf10(--await 超时 → exit=1234)和 vf25(未知子命令 → exit=1)涉及退出码验证，
/// ConversationScript 当前不支持退出码断言，标注 TODO 待扩展后补充。
/// </summary>
[Collection("MockServerShared")]
public sealed class VfCliSwitchTests : CoverageTestBase {
    /// <summary>
    /// 初始化 VF 组命令行+开关+权限测试实例。
    /// </summary>
    /// <param name="output">xUnit 测试输出辅助器。</param>
    /// <param name="fixture">共享 MockServer 固件。</param>
    public VfCliSwitchTests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    /// <summary>
    /// VF 组命令行+开关+权限测试数据 — 引用 VfCliSwitchScripts 中的脚本。
    /// 每条数据包含 (ConversationScript, vfId) 用于参数化测试。
    /// 覆盖 vf01-vf25 共 25 个助记符。
    /// </summary>
    public static IEnumerable<object[]> VfCliSwitchScriptsData => new[]
    {
        new object[] { VfCliSwitchScripts.Vf01ChatDefault, "vf01" },
        new object[] { VfCliSwitchScripts.Vf02NonInteractiveMode, "vf02" },
        new object[] { VfCliSwitchScripts.Vf03FormatNdjson, "vf03" },
        new object[] { VfCliSwitchScripts.Vf04FormatJson, "vf04" },
        new object[] { VfCliSwitchScripts.Vf05FormatText, "vf05" },
        new object[] { VfCliSwitchScripts.Vf06PermissionModeBypass, "vf06" },
        new object[] { VfCliSwitchScripts.Vf07PermissionModePlan, "vf07" },
        new object[] { VfCliSwitchScripts.Vf08Trust, "vf08" },
        new object[] { VfCliSwitchScripts.Vf09Continue, "vf09" },
        new object[] { VfCliSwitchScripts.Vf10AwaitTimeout, "vf10" },
        new object[] { VfCliSwitchScripts.Vf11DryRun, "vf11" },
        new object[] { VfCliSwitchScripts.Vf12EnvModelId, "vf12" },
        new object[] { VfCliSwitchScripts.Vf13EnvEndpoint, "vf13" },
        new object[] { VfCliSwitchScripts.Vf14EnvPermissionMode, "vf14" },
        new object[] { VfCliSwitchScripts.Vf15EnvDisableStreamingFallback, "vf15" },
        new object[] { VfCliSwitchScripts.Vf16EnvFileSystemInMemory, "vf16" },
        new object[] { VfCliSwitchScripts.Vf17EnvHttpModeMock, "vf17" },
        new object[] { VfCliSwitchScripts.Vf18EnvNoMouseTracking, "vf18" },
        new object[] { VfCliSwitchScripts.Vf19EnvDebuglog, "vf19" },
        new object[] { VfCliSwitchScripts.Vf20EnvSkipModelFetch, "vf20" },
        new object[] { VfCliSwitchScripts.Vf21SchemaSubCommand, "vf21" },
        new object[] { VfCliSwitchScripts.Vf22ConfigPrintSubCommand, "vf22" },
        new object[] { VfCliSwitchScripts.Vf23McpServeSubCommand, "vf23" },
        new object[] { VfCliSwitchScripts.Vf24GhSubCommand, "vf24" },
        new object[] { VfCliSwitchScripts.Vf25UnknownSubCommand, "vf25" },
    };

    /// <summary>
    /// 验证 VF 组命令行+开关+权限脚本所有断言通过。
    /// 覆盖 vf01-vf25 共 25 个助记符，包含 CLI 开关/环境变量/子命令场景。
    /// </summary>
    /// <param name="script">对话脚本。</param>
    /// <param name="vfId">助记符编号（vf01-vf25）。</param>
    [Theory]
    [MemberData(nameof(VfCliSwitchScriptsData))]
    public async Task VfCliSwitchScript_ShouldPassAllAsserts(ConversationScript script, string vfId) {
        Output.WriteLine($"[VF] 开始验证 {vfId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VF] {vfId} 验证通过");
    }
}
