namespace MockServer.E2E.Tests;

/// <summary>
/// VC 组 MCP 工具+外部工具 E2E 测试 — 拆分为 5 个小测试类（每 5 个用例一个），
/// 避免单类 25 个 Theory 用例并行启动过多 jcc.exe 进程导致测试主机崩溃。
/// 每个小类用 [Collection("MockServerShared")] 串行化，崩溃隔离不影响其他小类。
/// vc01-vc03/vc07-vc09/vc18-vc19 为 CLI 子命令，真实行为需手动 exe 验证（ADR 0080）。
/// vc21/vc23-vc25 需特殊环境，标注 TODO。
/// </summary>

[Collection("MockServerShared")]
public sealed class VcMcpToolTests01 : CoverageTestBase {
    public VcMcpToolTests01(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    public static IEnumerable<object[]> Data => new[]
    {
        new object[] { VcMcpToolScripts.Vc01McpServeHttpStart, "vc01" },
        new object[] { VcMcpToolScripts.Vc02McpServeTokenAuth, "vc02" },
        new object[] { VcMcpToolScripts.Vc03McpServeProtocolVersion, "vc03" },
        new object[] { VcMcpToolScripts.Vc04McpCallSuccess, "vc04" },
        new object[] { VcMcpToolScripts.Vc05McpCallToolNotFound, "vc05" },
    };

    [Theory]
    [MemberData(nameof(Data))]
    public async Task Vc01_05_ShouldPassAllAsserts(ConversationScript script, string vcId) {
        Output.WriteLine($"[VC] 开始验证 {vcId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VC] {vcId} 验证通过");
    }
}

[Collection("MockServerShared")]
public sealed class VcMcpToolTests02 : CoverageTestBase {
    public VcMcpToolTests02(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    public static IEnumerable<object[]> Data => new[]
    {
        new object[] { VcMcpToolScripts.Vc06McpCallParamValidationFail, "vc06" },
        new object[] { VcMcpToolScripts.Vc07McpListTools, "vc07" },
        new object[] { VcMcpToolScripts.Vc08McpListFilterByCategory, "vc08" },
        new object[] { VcMcpToolScripts.Vc09McpSchemaQuery, "vc09" },
        new object[] { VcMcpToolScripts.Vc10McpConnectAdd, "vc10" },
    };

    [Theory]
    [MemberData(nameof(Data))]
    public async Task Vc06_10_ShouldPassAllAsserts(ConversationScript script, string vcId) {
        Output.WriteLine($"[VC] 开始验证 {vcId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VC] {vcId} 验证通过");
    }
}

[Collection("MockServerShared")]
public sealed class VcMcpToolTests03 : CoverageTestBase {
    public VcMcpToolTests03(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    public static IEnumerable<object[]> Data => new[]
    {
        new object[] { VcMcpToolScripts.Vc11McpDisconnectRemove, "vc11" },
        new object[] { VcMcpToolScripts.Vc12McpConnectPersist, "vc12" },
        new object[] { VcMcpToolScripts.Vc13McpConnectRestore, "vc13" },
        new object[] { VcMcpToolScripts.Vc14TodoToolViaMcp, "vc14" },
        new object[] { VcMcpToolScripts.Vc15NoteToolViaMcp, "vc15" },
    };

    [Theory]
    [MemberData(nameof(Data))]
    public async Task Vc11_15_ShouldPassAllAsserts(ConversationScript script, string vcId) {
        Output.WriteLine($"[VC] 开始验证 {vcId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VC] {vcId} 验证通过");
    }
}

[Collection("MockServerShared")]
public sealed class VcMcpToolTests04 : CoverageTestBase {
    public VcMcpToolTests04(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    public static IEnumerable<object[]> Data => new[]
    {
        new object[] { VcMcpToolScripts.Vc16MemorySave, "vc16" },
        new object[] { VcMcpToolScripts.Vc17MemoryRetrieve, "vc17" },
        new object[] { VcMcpToolScripts.Vc18GhPrList, "vc18" },
        new object[] { VcMcpToolScripts.Vc19GhIssueList, "vc19" },
        new object[] { VcMcpToolScripts.Vc20CronCreate, "vc20" },
    };

    [Theory]
    [MemberData(nameof(Data))]
    public async Task Vc16_20_ShouldPassAllAsserts(ConversationScript script, string vcId) {
        Output.WriteLine($"[VC] 开始验证 {vcId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VC] {vcId} 验证通过");
    }
}

[Collection("MockServerShared")]
public sealed class VcMcpToolTests05 : CoverageTestBase {
    public VcMcpToolTests05(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    public static IEnumerable<object[]> Data => new[]
    {
        new object[] { VcMcpToolScripts.Vc21McpPermissionDenied, "vc21" },
        new object[] { VcMcpToolScripts.Vc22McpbPackageLoad, "vc22" },
        new object[] { VcMcpToolScripts.Vc23ToolInterventionBlacklist, "vc23" },
        new object[] { VcMcpToolScripts.Vc24ToolInterventionDowngrade, "vc24" },
        new object[] { VcMcpToolScripts.Vc25ToolInterventionRedirect, "vc25" },
    };

    [Theory]
    [MemberData(nameof(Data))]
    public async Task Vc21_25_ShouldPassAllAsserts(ConversationScript script, string vcId) {
        Output.WriteLine($"[VC] 开始验证 {vcId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VC] {vcId} 验证通过");
    }
}
