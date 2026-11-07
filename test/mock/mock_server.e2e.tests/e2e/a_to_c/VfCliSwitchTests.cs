namespace MockServer.E2E.Tests;

/// <summary>
/// Vf 组 E2E 测试 — 每个用例一个独立 Fact 类，串行跑避免并行启动多个 jcc.exe 资源竞争超时。
/// </summary>

[Collection("MockServerShared")]
public sealed class Vf01Tests : CoverageTestBase {
    public Vf01Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vf01() { Output.WriteLine("[Vf] vf01"); await RunScriptAsync(VfCliSwitchScripts.Vf01ChatDefault).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vf02Tests : CoverageTestBase {
    public Vf02Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vf02() { Output.WriteLine("[Vf] vf02"); await RunScriptAsync(VfCliSwitchScripts.Vf02NonInteractiveMode).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vf03Tests : CoverageTestBase {
    public Vf03Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vf03() { Output.WriteLine("[Vf] vf03"); await RunScriptAsync(VfCliSwitchScripts.Vf03FormatNdjson).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vf04Tests : CoverageTestBase {
    public Vf04Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vf04() { Output.WriteLine("[Vf] vf04"); await RunScriptAsync(VfCliSwitchScripts.Vf04FormatJson).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vf05Tests : CoverageTestBase {
    public Vf05Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vf05() { Output.WriteLine("[Vf] vf05"); await RunScriptAsync(VfCliSwitchScripts.Vf05FormatText).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vf06Tests : CoverageTestBase {
    public Vf06Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vf06() { Output.WriteLine("[Vf] vf06"); await RunScriptAsync(VfCliSwitchScripts.Vf06PermissionModeBypass).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vf07Tests : CoverageTestBase {
    public Vf07Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vf07() { Output.WriteLine("[Vf] vf07"); await RunScriptAsync(VfCliSwitchScripts.Vf07PermissionModePlan).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vf08Tests : CoverageTestBase {
    public Vf08Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vf08() { Output.WriteLine("[Vf] vf08"); await RunScriptAsync(VfCliSwitchScripts.Vf08Trust).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vf09Tests : CoverageTestBase {
    public Vf09Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vf09() { Output.WriteLine("[Vf] vf09"); await RunScriptAsync(VfCliSwitchScripts.Vf09Continue).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vf10Tests : CoverageTestBase {
    public Vf10Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vf10() { Output.WriteLine("[Vf] vf10"); await RunScriptAsync(VfCliSwitchScripts.Vf10AwaitTimeout).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vf11Tests : CoverageTestBase {
    public Vf11Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vf11() { Output.WriteLine("[Vf] vf11"); await RunScriptAsync(VfCliSwitchScripts.Vf11DryRun).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vf12Tests : CoverageTestBase {
    public Vf12Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vf12() { Output.WriteLine("[Vf] vf12"); await RunScriptAsync(VfCliSwitchScripts.Vf12EnvModelId).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vf13Tests : CoverageTestBase {
    public Vf13Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vf13() { Output.WriteLine("[Vf] vf13"); await RunScriptAsync(VfCliSwitchScripts.Vf13EnvEndpoint).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vf14Tests : CoverageTestBase {
    public Vf14Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vf14() { Output.WriteLine("[Vf] vf14"); await RunScriptAsync(VfCliSwitchScripts.Vf14EnvPermissionMode).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vf15Tests : CoverageTestBase {
    public Vf15Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vf15() { Output.WriteLine("[Vf] vf15"); await RunScriptAsync(VfCliSwitchScripts.Vf15EnvDisableStreamingFallback).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vf16Tests : CoverageTestBase {
    public Vf16Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vf16() { Output.WriteLine("[Vf] vf16"); await RunScriptAsync(VfCliSwitchScripts.Vf16EnvFileSystemInMemory).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vf17Tests : CoverageTestBase {
    public Vf17Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vf17() { Output.WriteLine("[Vf] vf17"); await RunScriptAsync(VfCliSwitchScripts.Vf17EnvHttpModeMock).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vf18Tests : CoverageTestBase {
    public Vf18Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vf18() { Output.WriteLine("[Vf] vf18"); await RunScriptAsync(VfCliSwitchScripts.Vf18EnvNoMouseTracking).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vf19Tests : CoverageTestBase {
    public Vf19Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vf19() { Output.WriteLine("[Vf] vf19"); await RunScriptAsync(VfCliSwitchScripts.Vf19EnvDebuglog).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vf20Tests : CoverageTestBase {
    public Vf20Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vf20() { Output.WriteLine("[Vf] vf20"); await RunScriptAsync(VfCliSwitchScripts.Vf20EnvSkipModelFetch).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vf21Tests : CoverageTestBase {
    public Vf21Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vf21() { Output.WriteLine("[Vf] vf21"); await RunScriptAsync(VfCliSwitchScripts.Vf21SchemaSubCommand).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vf22Tests : CoverageTestBase {
    public Vf22Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vf22() { Output.WriteLine("[Vf] vf22"); await RunScriptAsync(VfCliSwitchScripts.Vf22ConfigPrintSubCommand).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vf23Tests : CoverageTestBase {
    public Vf23Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vf23() { Output.WriteLine("[Vf] vf23"); await RunScriptAsync(VfCliSwitchScripts.Vf23McpServeSubCommand).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vf24Tests : CoverageTestBase {
    public Vf24Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vf24() { Output.WriteLine("[Vf] vf24"); await RunScriptAsync(VfCliSwitchScripts.Vf24GhSubCommand).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vf25Tests : CoverageTestBase {
    public Vf25Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vf25() { Output.WriteLine("[Vf] vf25"); await RunScriptAsync(VfCliSwitchScripts.Vf25UnknownSubCommand).ConfigureAwait(true); }
}
