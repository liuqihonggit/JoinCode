namespace MockServer.E2E.Tests;

/// <summary>
/// Ve 组 E2E 测试 — 每个用例一个独立 Fact 类，串行跑避免并行启动多个 jcc.exe 资源竞争超时。
/// </summary>

[Collection("MockServerShared")]
public sealed class Ve01Tests : CoverageTestBase {
    public Ve01Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Ve01() { Output.WriteLine("[Ve] ve01"); await RunScriptAsync(VeMultiTurnScripts.Ve01ContinueResumeHistory).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Ve02Tests : CoverageTestBase {
    public Ve02Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Ve02() { Output.WriteLine("[Ve] ve02"); await RunScriptAsync(VeMultiTurnScripts.Ve02ContinueNoHistory).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Ve03Tests : CoverageTestBase {
    public Ve03Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Ve03() { Output.WriteLine("[Ve] ve03"); await RunScriptAsync(VeMultiTurnScripts.Ve03ResumeIdExactMatch).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Ve04Tests : CoverageTestBase {
    public Ve04Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Ve04() { Output.WriteLine("[Ve] ve04"); await RunScriptAsync(VeMultiTurnScripts.Ve04ResumeIdNotFound).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Ve05Tests : CoverageTestBase {
    public Ve05Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Ve05() { Output.WriteLine("[Ve] ve05"); await RunScriptAsync(VeMultiTurnScripts.Ve05ResumeTitleFuzzyMatch).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Ve06Tests : CoverageTestBase {
    public Ve06Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Ve06() { Output.WriteLine("[Ve] ve06"); await RunScriptAsync(VeMultiTurnScripts.Ve06MultiTurnMessagesSequence).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Ve07Tests : CoverageTestBase {
    public Ve07Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Ve07() { Output.WriteLine("[Ve] ve07"); await RunScriptAsync(VeMultiTurnScripts.Ve07TranscriptWrite).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Ve08Tests : CoverageTestBase {
    public Ve08Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Ve08() { Output.WriteLine("[Ve] ve08"); await RunScriptAsync(VeMultiTurnScripts.Ve08TranscriptRestore).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Ve09Tests : CoverageTestBase {
    public Ve09Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Ve09() { Output.WriteLine("[Ve] ve09"); await RunScriptAsync(VeMultiTurnScripts.Ve09SessionIdConsistency).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Ve10Tests : CoverageTestBase {
    public Ve10Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Ve10() { Output.WriteLine("[Ve] ve10"); await RunScriptAsync(VeMultiTurnScripts.Ve10SubagentTypeParse).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Ve11Tests : CoverageTestBase {
    public Ve11Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Ve11() { Output.WriteLine("[Ve] ve11"); await RunScriptAsync(VeMultiTurnScripts.Ve11SubagentStarted).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Ve12Tests : CoverageTestBase {
    public Ve12Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Ve12() { Output.WriteLine("[Ve] ve12"); await RunScriptAsync(VeMultiTurnScripts.Ve12SubagentFinished).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Ve13Tests : CoverageTestBase {
    public Ve13Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Ve13() { Output.WriteLine("[Ve] ve13"); await RunScriptAsync(VeMultiTurnScripts.Ve13SubagentExceptionFallback).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Ve14Tests : CoverageTestBase {
    public Ve14Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Ve14() { Output.WriteLine("[Ve] ve14"); await RunScriptAsync(VeMultiTurnScripts.Ve14SubagentControl).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Ve15Tests : CoverageTestBase {
    public Ve15Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Ve15() { Output.WriteLine("[Ve] ve15"); await RunScriptAsync(VeMultiTurnScripts.Ve15SubagentUsageAggregate).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Ve16Tests : CoverageTestBase {
    public Ve16Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Ve16() { Output.WriteLine("[Ve] ve16"); await RunScriptAsync(VeMultiTurnScripts.Ve16NumTurnsCount).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Ve17Tests : CoverageTestBase {
    public Ve17Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Ve17() { Output.WriteLine("[Ve] ve17"); await RunScriptAsync(VeMultiTurnScripts.Ve17ConcurrentSessions).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Ve18Tests : CoverageTestBase {
    public Ve18Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Ve18() { Output.WriteLine("[Ve] ve18"); await RunScriptAsync(VeMultiTurnScripts.Ve18ForkSubagent).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Ve19Tests : CoverageTestBase {
    public Ve19Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Ve19() { Output.WriteLine("[Ve] ve19"); await RunScriptAsync(VeMultiTurnScripts.Ve19TeammateSubagent).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Ve20Tests : CoverageTestBase {
    public Ve20Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Ve20() { Output.WriteLine("[Ve] ve20"); await RunScriptAsync(VeMultiTurnScripts.Ve20SubagentSteer).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Ve21Tests : CoverageTestBase {
    public Ve21Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Ve21() { Output.WriteLine("[Ve] ve21"); await RunScriptAsync(VeMultiTurnScripts.Ve21SubagentResumeFromTranscript).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Ve22Tests : CoverageTestBase {
    public Ve22Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Ve22() { Output.WriteLine("[Ve] ve22"); await RunScriptAsync(VeMultiTurnScripts.Ve22AutoCompact).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Ve23Tests : CoverageTestBase {
    public Ve23Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Ve23() { Output.WriteLine("[Ve] ve23"); await RunScriptAsync(VeMultiTurnScripts.Ve23ContextUsage).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Ve24Tests : CoverageTestBase {
    public Ve24Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Ve24() { Output.WriteLine("[Ve] ve24"); await RunScriptAsync(VeMultiTurnScripts.Ve24PermissionDenialsAccumulate).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Ve25Tests : CoverageTestBase {
    public Ve25Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Ve25() { Output.WriteLine("[Ve] ve25"); await RunScriptAsync(VeMultiTurnScripts.Ve25ThinkingPersist).ConfigureAwait(true); }
}
