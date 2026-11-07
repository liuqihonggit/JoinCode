namespace MockServer.E2E.Tests;

/// <summary>
/// Vc 组 E2E 测试 — 每个用例一个独立 Fact 类，串行跑避免并行启动多个 jcc.exe 资源竞争超时。
/// </summary>

[Collection("MockServerShared")]
public sealed class Vc01Tests : CoverageTestBase {
    public Vc01Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vc01() { Output.WriteLine("[Vc] vc01"); await RunScriptAsync(VcMcpToolScripts.Vc01McpServeHttpStart).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vc02Tests : CoverageTestBase {
    public Vc02Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vc02() { Output.WriteLine("[Vc] vc02"); await RunScriptAsync(VcMcpToolScripts.Vc02McpServeTokenAuth).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vc03Tests : CoverageTestBase {
    public Vc03Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vc03() { Output.WriteLine("[Vc] vc03"); await RunScriptAsync(VcMcpToolScripts.Vc03McpServeProtocolVersion).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vc04Tests : CoverageTestBase {
    public Vc04Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vc04() { Output.WriteLine("[Vc] vc04"); await RunScriptAsync(VcMcpToolScripts.Vc04McpCallSuccess).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vc05Tests : CoverageTestBase {
    public Vc05Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vc05() { Output.WriteLine("[Vc] vc05"); await RunScriptAsync(VcMcpToolScripts.Vc05McpCallToolNotFound).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vc06Tests : CoverageTestBase {
    public Vc06Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vc06() { Output.WriteLine("[Vc] vc06"); await RunScriptAsync(VcMcpToolScripts.Vc06McpCallParamValidationFail).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vc07Tests : CoverageTestBase {
    public Vc07Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vc07() { Output.WriteLine("[Vc] vc07"); await RunScriptAsync(VcMcpToolScripts.Vc07McpListTools).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vc08Tests : CoverageTestBase {
    public Vc08Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vc08() { Output.WriteLine("[Vc] vc08"); await RunScriptAsync(VcMcpToolScripts.Vc08McpListFilterByCategory).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vc09Tests : CoverageTestBase {
    public Vc09Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vc09() { Output.WriteLine("[Vc] vc09"); await RunScriptAsync(VcMcpToolScripts.Vc09McpSchemaQuery).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vc10Tests : CoverageTestBase {
    public Vc10Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vc10() { Output.WriteLine("[Vc] vc10"); await RunScriptAsync(VcMcpToolScripts.Vc10McpConnectAdd).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vc11Tests : CoverageTestBase {
    public Vc11Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vc11() { Output.WriteLine("[Vc] vc11"); await RunScriptAsync(VcMcpToolScripts.Vc11McpDisconnectRemove).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vc12Tests : CoverageTestBase {
    public Vc12Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vc12() { Output.WriteLine("[Vc] vc12"); await RunScriptAsync(VcMcpToolScripts.Vc12McpConnectPersist).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vc13Tests : CoverageTestBase {
    public Vc13Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vc13() { Output.WriteLine("[Vc] vc13"); await RunScriptAsync(VcMcpToolScripts.Vc13McpConnectRestore).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vc14Tests : CoverageTestBase {
    public Vc14Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vc14() { Output.WriteLine("[Vc] vc14"); await RunScriptAsync(VcMcpToolScripts.Vc14TodoToolViaMcp).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vc15Tests : CoverageTestBase {
    public Vc15Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vc15() { Output.WriteLine("[Vc] vc15"); await RunScriptAsync(VcMcpToolScripts.Vc15NoteToolViaMcp).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vc16Tests : CoverageTestBase {
    public Vc16Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vc16() { Output.WriteLine("[Vc] vc16"); await RunScriptAsync(VcMcpToolScripts.Vc16MemorySave).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vc17Tests : CoverageTestBase {
    public Vc17Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vc17() { Output.WriteLine("[Vc] vc17"); await RunScriptAsync(VcMcpToolScripts.Vc17MemoryRetrieve).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vc18Tests : CoverageTestBase {
    public Vc18Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vc18() { Output.WriteLine("[Vc] vc18"); await RunScriptAsync(VcMcpToolScripts.Vc18GhPrList).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vc19Tests : CoverageTestBase {
    public Vc19Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vc19() { Output.WriteLine("[Vc] vc19"); await RunScriptAsync(VcMcpToolScripts.Vc19GhIssueList).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vc20Tests : CoverageTestBase {
    public Vc20Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vc20() { Output.WriteLine("[Vc] vc20"); await RunScriptAsync(VcMcpToolScripts.Vc20CronCreate).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vc21Tests : CoverageTestBase {
    public Vc21Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vc21() { Output.WriteLine("[Vc] vc21"); await RunScriptAsync(VcMcpToolScripts.Vc21McpPermissionDenied).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vc22Tests : CoverageTestBase {
    public Vc22Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vc22() { Output.WriteLine("[Vc] vc22"); await RunScriptAsync(VcMcpToolScripts.Vc22McpbPackageLoad).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vc23Tests : CoverageTestBase {
    public Vc23Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vc23() { Output.WriteLine("[Vc] vc23"); await RunScriptAsync(VcMcpToolScripts.Vc23ToolInterventionBlacklist).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vc24Tests : CoverageTestBase {
    public Vc24Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vc24() { Output.WriteLine("[Vc] vc24"); await RunScriptAsync(VcMcpToolScripts.Vc24ToolInterventionDowngrade).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vc25Tests : CoverageTestBase {
    public Vc25Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vc25() { Output.WriteLine("[Vc] vc25"); await RunScriptAsync(VcMcpToolScripts.Vc25ToolInterventionRedirect).ConfigureAwait(true); }
}
