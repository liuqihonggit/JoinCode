namespace MockServer.E2E.Tests;

/// <summary>
/// VD 组错误处理+退出码 E2E 测试 — 拆分为 5 个小测试类（每 5 个用例一个），
/// 避免单类 25 个 Theory 用例并行启动过多 jcc.exe 进程导致测试主机崩溃。
/// 每个小类用 [Collection("MockServerShared")] 串行化，崩溃隔离不影响其他小类。
/// vd11 --await 1 退出码验证场景 jcc 无 stdout 退出(exit=1234)，
/// RunScriptAsync 会抛 GEN019 重试16次，已移除用 .xxx/test_exit_codes.py 独立验证。
/// vd10/vd13/vd14/vd15/vd17-vd21 脚本本身能跑（有输出），加回来覆盖错误处理链路。
/// vd12 断言已修复：从 HasAssistantResponse 改为 ContainsText "空回复"（jcc 正确报错）。
/// </summary>

[Collection("MockServerShared")]
public sealed class VdErrorHandlingTests01 : CoverageTestBase {
    public VdErrorHandlingTests01(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    public static IEnumerable<object[]> Data => new[]
    {
        new object[] { VdErrorHandlingScripts.Vd01Http200Success, "vd01" },
        new object[] { VdErrorHandlingScripts.Vd02Http401AuthError, "vd02" },
        new object[] { VdErrorHandlingScripts.Vd03Http403Forbidden, "vd03" },
        new object[] { VdErrorHandlingScripts.Vd04Http429RateLimit, "vd04" },
        new object[] { VdErrorHandlingScripts.Vd05Http500ServerError, "vd05" },
    };

    [Theory]
    [MemberData(nameof(Data))]
    public async Task Vd01_05_ShouldPassAllAsserts(ConversationScript script, string vdId) {
        Output.WriteLine($"[VD] 开始验证 {vdId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VD] {vdId} 验证通过");
    }
}

[Collection("MockServerShared")]
public sealed class VdErrorHandlingTests02 : CoverageTestBase {
    public VdErrorHandlingTests02(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    public static IEnumerable<object[]> Data => new[]
    {
        new object[] { VdErrorHandlingScripts.Vd06Http502BadGateway, "vd06" },
        new object[] { VdErrorHandlingScripts.Vd07Http503ServiceUnavailable, "vd07" },
        new object[] { VdErrorHandlingScripts.Vd08StreamingFallback, "vd08" },
        new object[] { VdErrorHandlingScripts.Vd09ConnectionErrorNoFallback, "vd09" },
        new object[] { VdErrorHandlingScripts.Vd10TimeoutExit1240, "vd10" },
    };

    [Theory]
    [MemberData(nameof(Data))]
    public async Task Vd06_10_ShouldPassAllAsserts(ConversationScript script, string vdId) {
        Output.WriteLine($"[VD] 开始验证 {vdId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VD] {vdId} 验证通过");
    }
}

[Collection("MockServerShared")]
public sealed class Vd12Tests : CoverageTestBase {
    public Vd12Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact]
    public async Task Vd12_ShouldPass() {
        Output.WriteLine("[VD] 开始验证 vd12: 空响应");
        await RunScriptAsync(VdErrorHandlingScripts.Vd12EmptyResponse).ConfigureAwait(true);
        Output.WriteLine("[VD] vd12 验证通过");
    }
}

[Collection("MockServerShared")]
public sealed class Vd13Tests : CoverageTestBase {
    public Vd13Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact]
    public async Task Vd13_ShouldPass() {
        Output.WriteLine("[VD] 开始验证 vd13: 空流式响应");
        await RunScriptAsync(VdErrorHandlingScripts.Vd13EmptyBodySseIdleTimeout).ConfigureAwait(true);
        Output.WriteLine("[VD] vd13 验证通过");
    }
}

[Collection("MockServerShared")]
public sealed class Vd14Tests : CoverageTestBase {
    public Vd14Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact]
    public async Task Vd14_ShouldPass() {
        Output.WriteLine("[VD] 开始验证 vd14: 重试耗尽");
        await RunScriptAsync(VdErrorHandlingScripts.Vd14RetryExhausted).ConfigureAwait(true);
        Output.WriteLine("[VD] vd14 验证通过");
    }
}

[Collection("MockServerShared")]
public sealed class Vd15Tests : CoverageTestBase {
    public Vd15Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact]
    public async Task Vd15_ShouldPass() {
        Output.WriteLine("[VD] 开始验证 vd15: 熔断器");
        await RunScriptAsync(VdErrorHandlingScripts.Vd15CircuitBreakerOpen).ConfigureAwait(true);
        Output.WriteLine("[VD] vd15 验证通过");
    }
}

[Collection("MockServerShared")]
public sealed class Vd16Tests : CoverageTestBase {
    public Vd16Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact]
    public async Task Vd16_ShouldPass() {
        Output.WriteLine("[VD] 开始验证 vd16: 预算耗尽");
        await RunScriptAsync(VdErrorHandlingScripts.Vd16RetryBudgetExhausted).ConfigureAwait(true);
        Output.WriteLine("[VD] vd16 验证通过");
    }
}

[Collection("MockServerShared")]
public sealed class VdErrorHandlingTests04 : CoverageTestBase {
    public VdErrorHandlingTests04(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    public static IEnumerable<object[]> Data => new[]
    {
        new object[] { VdErrorHandlingScripts.Vd17CtrlCExit130, "vd17" },
        new object[] { VdErrorHandlingScripts.Vd18ConfigErrorExit2, "vd18" },
        new object[] { VdErrorHandlingScripts.Vd19ApiKeyMissingExit4, "vd19" },
        new object[] { VdErrorHandlingScripts.Vd20ArgParseErrorExit3, "vd20" },
        new object[] { VdErrorHandlingScripts.Vd21SessionResumeFailedExit5, "vd21" },
    };

    [Theory]
    [MemberData(nameof(Data))]
    public async Task Vd17_21_ShouldPassAllAsserts(ConversationScript script, string vdId) {
        Output.WriteLine($"[VD] 开始验证 {vdId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VD] {vdId} 验证通过");
    }
}

[Collection("MockServerShared")]
public sealed class VdErrorHandlingTests05 : CoverageTestBase {
    public VdErrorHandlingTests05(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    public static IEnumerable<object[]> Data => new[]
    {
        new object[] { VdErrorHandlingScripts.Vd22CrashStoreSnapshot, "vd22" },
        new object[] { VdErrorHandlingScripts.Vd23ThreeLayerRetry, "vd23" },
        new object[] { VdErrorHandlingScripts.Vd24ExceptionClassificationRecursive, "vd24" },
        new object[] { VdErrorHandlingScripts.Vd25ContentFilterStopReason, "vd25" },
    };

    [Theory]
    [MemberData(nameof(Data))]
    public async Task Vd22_25_ShouldPassAllAsserts(ConversationScript script, string vdId) {
        Output.WriteLine($"[VD] 开始验证 {vdId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VD] {vdId} 验证通过");
    }
}
