namespace MockServer.E2E.Tests;

/// <summary>
/// VD 组错误处理+退出码 E2E 测试 — 覆盖 vd01-vd25 错误处理与退出码助记符。
/// 每个助记符对应一个 ConversationScript，验证错误处理链路的特定行为信号。
/// vd10/vd11/vd17-vd21 为退出码验证场景，RunScriptAsync 不直接验证退出码，
/// 标注 TODO 需用独立 E2E 或 .xxx/test_exit_codes.py 验证退出码（ADR 0080）。
/// vd09/vd15/vd16/vd22/vd23 需特殊构造，标注 TODO。vd13/vd24 已修复。
/// </summary>
[Collection("MockServerShared")]
public sealed class VdErrorHandlingTests : CoverageTestBase {
    /// <summary>
    /// 初始化 VD 组错误处理+退出码测试实例。
    /// </summary>
    /// <param name="output">xUnit 测试输出辅助器。</param>
    /// <param name="fixture">共享 MockServer 固件。</param>
    public VdErrorHandlingTests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    /// <summary>
    /// VD 组错误处理+退出码测试数据 — 引用 VdErrorHandlingScripts 中的脚本。
    /// 每条数据包含 (ConversationScript, vdId) 用于参数化测试。
    /// 覆盖 vd01-vd25 共 25 个助记符。
    /// </summary>
    public static IEnumerable<object[]> VdErrorHandlingScriptsData => new[]
    {
        new object[] { VdErrorHandlingScripts.Vd01Http200Success, "vd01" },
        new object[] { VdErrorHandlingScripts.Vd02Http401AuthError, "vd02" },
        new object[] { VdErrorHandlingScripts.Vd03Http403Forbidden, "vd03" },
        new object[] { VdErrorHandlingScripts.Vd04Http429RateLimit, "vd04" },
        new object[] { VdErrorHandlingScripts.Vd05Http500ServerError, "vd05" },
        new object[] { VdErrorHandlingScripts.Vd06Http502BadGateway, "vd06" },
        new object[] { VdErrorHandlingScripts.Vd07Http503ServiceUnavailable, "vd07" },
        new object[] { VdErrorHandlingScripts.Vd08StreamingFallback, "vd08" },
        new object[] { VdErrorHandlingScripts.Vd09ConnectionErrorNoFallback, "vd09" },
        new object[] { VdErrorHandlingScripts.Vd10TimeoutExit1240, "vd10" },
        new object[] { VdErrorHandlingScripts.Vd11AwaitTimeoutExit1234, "vd11" },
        new object[] { VdErrorHandlingScripts.Vd12EmptyResponse, "vd12" },
        new object[] { VdErrorHandlingScripts.Vd13EmptyBodySseIdleTimeout, "vd13" },
        new object[] { VdErrorHandlingScripts.Vd14RetryExhausted, "vd14" },
        new object[] { VdErrorHandlingScripts.Vd15CircuitBreakerOpen, "vd15" },
        new object[] { VdErrorHandlingScripts.Vd16RetryBudgetExhausted, "vd16" },
        new object[] { VdErrorHandlingScripts.Vd17CtrlCExit130, "vd17" },
        new object[] { VdErrorHandlingScripts.Vd18ConfigErrorExit2, "vd18" },
        new object[] { VdErrorHandlingScripts.Vd19ApiKeyMissingExit4, "vd19" },
        new object[] { VdErrorHandlingScripts.Vd20ArgParseErrorExit3, "vd20" },
        new object[] { VdErrorHandlingScripts.Vd21SessionResumeFailedExit5, "vd21" },
        new object[] { VdErrorHandlingScripts.Vd22CrashStoreSnapshot, "vd22" },
        new object[] { VdErrorHandlingScripts.Vd23ThreeLayerRetry, "vd23" },
        new object[] { VdErrorHandlingScripts.Vd24ExceptionClassificationRecursive, "vd24" },
        new object[] { VdErrorHandlingScripts.Vd25ContentFilterStopReason, "vd25" },
    };

    /// <summary>
    /// 验证 VD 组错误处理+退出码脚本所有断言通过。
    /// 覆盖 vd01-vd25 共 25 个助记符，包含 HTTP 错误/超时/重试/退出码/异常分类场景。
    /// </summary>
    /// <param name="script">对话脚本。</param>
    /// <param name="vdId">助记符编号（vd01-vd25）。</param>
    [Theory]
    [MemberData(nameof(VdErrorHandlingScriptsData))]
    public async Task VdErrorHandlingScript_ShouldPassAllAsserts(ConversationScript script, string vdId) {
        Output.WriteLine($"[VD] 开始验证 {vdId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VD] {vdId} 验证通过");
    }
}
