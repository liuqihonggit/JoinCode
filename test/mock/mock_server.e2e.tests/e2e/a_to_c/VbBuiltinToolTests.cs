namespace MockServer.E2E.Tests;

/// <summary>
/// Vb 组 E2E 测试 — 每个用例一个独立 Fact 类，串行跑避免并行启动多个 jcc.exe 资源竞争超时。
/// </summary>

[Collection("MockServerShared")]
public sealed class Vb01Tests : CoverageTestBase {
    public Vb01Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb01() { Output.WriteLine("[Vb] vb01"); await RunScriptAsync(VbBuiltinToolScripts.Vb01BashSuccess).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb02Tests : CoverageTestBase {
    public Vb02Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb02() { Output.WriteLine("[Vb] vb02"); await RunScriptAsync(VbBuiltinToolScripts.Vb02BashFailure).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb03Tests : CoverageTestBase {
    public Vb03Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb03() { Output.WriteLine("[Vb] vb03"); await RunScriptAsync(VbBuiltinToolScripts.Vb03BashTimeout).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb04Tests : CoverageTestBase {
    public Vb04Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb04() { Output.WriteLine("[Vb] vb04"); await RunScriptAsync(VbBuiltinToolScripts.Vb04BashCommandNotFound).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb05Tests : CoverageTestBase {
    public Vb05Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb05() { Output.WriteLine("[Vb] vb05"); await RunScriptAsync(VbBuiltinToolScripts.Vb05BashLargeOutputTruncated).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb06Tests : CoverageTestBase {
    public Vb06Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb06() { Output.WriteLine("[Vb] vb06"); await RunScriptAsync(VbBuiltinToolScripts.Vb06ReadSuccess).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb07Tests : CoverageTestBase {
    public Vb07Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb07() { Output.WriteLine("[Vb] vb07"); await RunScriptAsync(VbBuiltinToolScripts.Vb07ReadFileNotFound).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb08Tests : CoverageTestBase {
    public Vb08Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb08() { Output.WriteLine("[Vb] vb08"); await RunScriptAsync(VbBuiltinToolScripts.Vb08ReadDirectoryNotFile).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb09Tests : CoverageTestBase {
    public Vb09Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb09() { Output.WriteLine("[Vb] vb09"); await RunScriptAsync(VbBuiltinToolScripts.Vb09ReadTokenLimitExceeded).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb10Tests : CoverageTestBase {
    public Vb10Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb10() { Output.WriteLine("[Vb] vb10"); await RunScriptAsync(VbBuiltinToolScripts.Vb10ReadBinaryRejected).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb11Tests : CoverageTestBase {
    public Vb11Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb11() { Output.WriteLine("[Vb] vb11"); await RunScriptAsync(VbBuiltinToolScripts.Vb11ReadImageBase64).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb12Tests : CoverageTestBase {
    public Vb12Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb12() { Output.WriteLine("[Vb] vb12"); await RunScriptAsync(VbBuiltinToolScripts.Vb12WriteCreateNewFile).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb13Tests : CoverageTestBase {
    public Vb13Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb13() { Output.WriteLine("[Vb] vb13"); await RunScriptAsync(VbBuiltinToolScripts.Vb13WriteOverwriteExisting).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb14Tests : CoverageTestBase {
    public Vb14Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb14() { Output.WriteLine("[Vb] vb14"); await RunScriptAsync(VbBuiltinToolScripts.Vb14WriteWithoutReadRejected).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb15Tests : CoverageTestBase {
    public Vb15Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb15() { Output.WriteLine("[Vb] vb15"); await RunScriptAsync(VbBuiltinToolScripts.Vb15EditSuccess).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb16Tests : CoverageTestBase {
    public Vb16Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb16() { Output.WriteLine("[Vb] vb16"); await RunScriptAsync(VbBuiltinToolScripts.Vb16EditOldStringNotFound).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb17Tests : CoverageTestBase {
    public Vb17Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb17() { Output.WriteLine("[Vb] vb17"); await RunScriptAsync(VbBuiltinToolScripts.Vb17EditReplaceAll).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb18Tests : CoverageTestBase {
    public Vb18Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb18() { Output.WriteLine("[Vb] vb18"); await RunScriptAsync(VbBuiltinToolScripts.Vb18GlobMatch).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb19Tests : CoverageTestBase {
    public Vb19Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb19() { Output.WriteLine("[Vb] vb19"); await RunScriptAsync(VbBuiltinToolScripts.Vb19GlobNoMatch).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb20Tests : CoverageTestBase {
    public Vb20Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb20() { Output.WriteLine("[Vb] vb20"); await RunScriptAsync(VbBuiltinToolScripts.Vb20GrepMatch).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb21Tests : CoverageTestBase {
    public Vb21Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb21() { Output.WriteLine("[Vb] vb21"); await RunScriptAsync(VbBuiltinToolScripts.Vb21GrepNoMatch).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb22Tests : CoverageTestBase {
    public Vb22Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb22() { Output.WriteLine("[Vb] vb22"); await RunScriptAsync(VbBuiltinToolScripts.Vb22TodoWriteCreate).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb23Tests : CoverageTestBase {
    public Vb23Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb23() { Output.WriteLine("[Vb] vb23"); await RunScriptAsync(VbBuiltinToolScripts.Vb23TodoListList).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb24Tests : CoverageTestBase {
    public Vb24Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb24() { Output.WriteLine("[Vb] vb24"); await RunScriptAsync(VbBuiltinToolScripts.Vb24TodoUpdateStatus).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb25Tests : CoverageTestBase {
    public Vb25Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb25() { Output.WriteLine("[Vb] vb25"); await RunScriptAsync(VbBuiltinToolScripts.Vb25TaskCreate).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb26Tests : CoverageTestBase {
    public Vb26Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb26() { Output.WriteLine("[Vb] vb26"); await RunScriptAsync(VbBuiltinToolScripts.Vb26TaskListList).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb27Tests : CoverageTestBase {
    public Vb27Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb27() { Output.WriteLine("[Vb] vb27"); await RunScriptAsync(VbBuiltinToolScripts.Vb27SkillLoad).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb28Tests : CoverageTestBase {
    public Vb28Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb28() { Output.WriteLine("[Vb] vb28"); await RunScriptAsync(VbBuiltinToolScripts.Vb28SkillNotFound).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb29Tests : CoverageTestBase {
    public Vb29Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb29() { Output.WriteLine("[Vb] vb29"); await RunScriptAsync(VbBuiltinToolScripts.Vb29WebfetchSuccess).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb30Tests : CoverageTestBase {
    public Vb30Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb30() { Output.WriteLine("[Vb] vb30"); await RunScriptAsync(VbBuiltinToolScripts.Vb30WebfetchRedirect).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb31Tests : CoverageTestBase {
    public Vb31Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb31() { Output.WriteLine("[Vb] vb31"); await RunScriptAsync(VbBuiltinToolScripts.Vb31WebfetchFailure).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb32Tests : CoverageTestBase {
    public Vb32Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb32() { Output.WriteLine("[Vb] vb32"); await RunScriptAsync(VbBuiltinToolScripts.Vb32QuestionAskAndAnswer).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb33Tests : CoverageTestBase {
    public Vb33Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb33() { Output.WriteLine("[Vb] vb33"); await RunScriptAsync(VbBuiltinToolScripts.Vb33QuestionOptionValidation).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb34Tests : CoverageTestBase {
    public Vb34Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb34() { Output.WriteLine("[Vb] vb34"); await RunScriptAsync(VbBuiltinToolScripts.Vb34AgentCreateSubAgent).ConfigureAwait(true); }
}

[Collection("MockServerShared")]
public sealed class Vb35Tests : CoverageTestBase {
    public Vb35Tests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }
    [Fact] public async Task Vb35() { Output.WriteLine("[Vb] vb35"); await RunScriptAsync(VbBuiltinToolScripts.Vb35AgentStatusQuery).ConfigureAwait(true); }
}
