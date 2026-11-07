namespace MockServer.E2E.Tests;

/// <summary>
/// VF 组命令行+开关+权限 E2E 测试 — 拆分为 5 个小测试类（每 5 个用例一个），
/// 避免单类 25 个 Theory 用例并行启动过多 jcc.exe 进程导致测试主机崩溃。
/// 每个小类用 [Collection("MockServerShared")] 串行化，崩溃隔离不影响其他小类。
/// vf10(--await 5 → exit=1234) jcc 无 stdout 退出导致 GEN019 重试崩溃，
/// 已从 E2E 移除用 .xxx/test_exit_codes.py 独立验证退出码。
/// vf25 未知子命令 jcc 当作 prompt 正常响应(exit=0)，保留在 E2E 中。
/// </summary>

[Collection("MockServerShared")]
public sealed class VfCliSwitchTests01 : CoverageTestBase {
    public VfCliSwitchTests01(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    public static IEnumerable<object[]> Data => new[]
    {
        new object[] { VfCliSwitchScripts.Vf01ChatDefault, "vf01" },
        new object[] { VfCliSwitchScripts.Vf02NonInteractiveMode, "vf02" },
        new object[] { VfCliSwitchScripts.Vf03FormatNdjson, "vf03" },
        new object[] { VfCliSwitchScripts.Vf04FormatJson, "vf04" },
        new object[] { VfCliSwitchScripts.Vf05FormatText, "vf05" },
    };

    [Theory]
    [MemberData(nameof(Data))]
    public async Task Vf01_05_ShouldPassAllAsserts(ConversationScript script, string vfId) {
        Output.WriteLine($"[VF] 开始验证 {vfId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VF] {vfId} 验证通过");
    }
}

[Collection("MockServerShared")]
public sealed class VfCliSwitchTests02 : CoverageTestBase {
    public VfCliSwitchTests02(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    /// <summary>vf10 --await 5 会导致 jcc exit=1234 无 stdout，已移除标注 TODO</summary>
    public static IEnumerable<object[]> Data => new[]
    {
        new object[] { VfCliSwitchScripts.Vf06PermissionModeBypass, "vf06" },
        new object[] { VfCliSwitchScripts.Vf07PermissionModePlan, "vf07" },
        new object[] { VfCliSwitchScripts.Vf08Trust, "vf08" },
        new object[] { VfCliSwitchScripts.Vf09Continue, "vf09" },
    };

    [Theory]
    [MemberData(nameof(Data))]
    public async Task Vf06_09_ShouldPassAllAsserts(ConversationScript script, string vfId) {
        Output.WriteLine($"[VF] 开始验证 {vfId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VF] {vfId} 验证通过");
    }
}

[Collection("MockServerShared")]
public sealed class VfCliSwitchTests03 : CoverageTestBase {
    public VfCliSwitchTests03(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    public static IEnumerable<object[]> Data => new[]
    {
        new object[] { VfCliSwitchScripts.Vf11DryRun, "vf11" },
        new object[] { VfCliSwitchScripts.Vf12EnvModelId, "vf12" },
        new object[] { VfCliSwitchScripts.Vf13EnvEndpoint, "vf13" },
        new object[] { VfCliSwitchScripts.Vf14EnvPermissionMode, "vf14" },
        new object[] { VfCliSwitchScripts.Vf15EnvDisableStreamingFallback, "vf15" },
    };

    [Theory]
    [MemberData(nameof(Data))]
    public async Task Vf11_15_ShouldPassAllAsserts(ConversationScript script, string vfId) {
        Output.WriteLine($"[VF] 开始验证 {vfId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VF] {vfId} 验证通过");
    }
}

[Collection("MockServerShared")]
public sealed class VfCliSwitchTests04 : CoverageTestBase {
    public VfCliSwitchTests04(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    public static IEnumerable<object[]> Data => new[]
    {
        new object[] { VfCliSwitchScripts.Vf16EnvFileSystemInMemory, "vf16" },
        new object[] { VfCliSwitchScripts.Vf17EnvHttpModeMock, "vf17" },
        new object[] { VfCliSwitchScripts.Vf18EnvNoMouseTracking, "vf18" },
        new object[] { VfCliSwitchScripts.Vf19EnvDebuglog, "vf19" },
        new object[] { VfCliSwitchScripts.Vf20EnvSkipModelFetch, "vf20" },
    };

    [Theory]
    [MemberData(nameof(Data))]
    public async Task Vf16_20_ShouldPassAllAsserts(ConversationScript script, string vfId) {
        Output.WriteLine($"[VF] 开始验证 {vfId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VF] {vfId} 验证通过");
    }
}

[Collection("MockServerShared")]
public sealed class VfCliSwitchTests05 : CoverageTestBase {
    public VfCliSwitchTests05(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    public static IEnumerable<object[]> Data => new[]
    {
        new object[] { VfCliSwitchScripts.Vf21SchemaSubCommand, "vf21" },
        new object[] { VfCliSwitchScripts.Vf22ConfigPrintSubCommand, "vf22" },
        new object[] { VfCliSwitchScripts.Vf23McpServeSubCommand, "vf23" },
        new object[] { VfCliSwitchScripts.Vf24GhSubCommand, "vf24" },
        new object[] { VfCliSwitchScripts.Vf25UnknownSubCommand, "vf25" },
    };

    [Theory]
    [MemberData(nameof(Data))]
    public async Task Vf21_25_ShouldPassAllAsserts(ConversationScript script, string vfId) {
        Output.WriteLine($"[VF] 开始验证 {vfId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VF] {vfId} 验证通过");
    }
}
