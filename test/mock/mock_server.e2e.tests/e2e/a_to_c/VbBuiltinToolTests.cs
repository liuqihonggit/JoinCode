namespace MockServer.E2E.Tests;

/// <summary>
/// VB 组内置工具 E2E 测试 — 拆分为 7 个小测试类（每 5 个用例一个），
/// 避免单类 35 个 Theory 用例并行启动过多 jcc.exe 进程导致测试主机崩溃。
/// 每个小类用 [Collection("MockServerShared")] 串行化，崩溃隔离不影响其他小类。
/// vb03/vb05/vb09/vb14 需手动 exe 验证（ADR 0080），脚本覆盖工具调用链路。
/// </summary>

[Collection("MockServerShared")]
public sealed class VbBuiltinToolTests01 : CoverageTestBase {
    public VbBuiltinToolTests01(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    public static IEnumerable<object[]> Data => new[]
    {
        new object[] { VbBuiltinToolScripts.Vb01BashSuccess, "vb01" },
        new object[] { VbBuiltinToolScripts.Vb02BashFailure, "vb02" },
        new object[] { VbBuiltinToolScripts.Vb03BashTimeout, "vb03" },
        new object[] { VbBuiltinToolScripts.Vb04BashCommandNotFound, "vb04" },
        new object[] { VbBuiltinToolScripts.Vb05BashLargeOutputTruncated, "vb05" },
    };

    [Theory]
    [MemberData(nameof(Data))]
    public async Task Vb01_05_ShouldPassAllAsserts(ConversationScript script, string vbId) {
        Output.WriteLine($"[VB] 开始验证 {vbId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VB] {vbId} 验证通过");
    }
}

[Collection("MockServerShared")]
public sealed class VbBuiltinToolTests02 : CoverageTestBase {
    public VbBuiltinToolTests02(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    public static IEnumerable<object[]> Data => new[]
    {
        new object[] { VbBuiltinToolScripts.Vb06ReadSuccess, "vb06" },
        new object[] { VbBuiltinToolScripts.Vb07ReadFileNotFound, "vb07" },
        new object[] { VbBuiltinToolScripts.Vb08ReadDirectoryNotFile, "vb08" },
        new object[] { VbBuiltinToolScripts.Vb09ReadTokenLimitExceeded, "vb09" },
        new object[] { VbBuiltinToolScripts.Vb10ReadBinaryRejected, "vb10" },
    };

    [Theory]
    [MemberData(nameof(Data))]
    public async Task Vb06_10_ShouldPassAllAsserts(ConversationScript script, string vbId) {
        Output.WriteLine($"[VB] 开始验证 {vbId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VB] {vbId} 验证通过");
    }
}

[Collection("MockServerShared")]
public sealed class VbBuiltinToolTests03 : CoverageTestBase {
    public VbBuiltinToolTests03(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    public static IEnumerable<object[]> Data => new[]
    {
        new object[] { VbBuiltinToolScripts.Vb11ReadImageBase64, "vb11" },
        new object[] { VbBuiltinToolScripts.Vb12WriteCreateNewFile, "vb12" },
        new object[] { VbBuiltinToolScripts.Vb13WriteOverwriteExisting, "vb13" },
        new object[] { VbBuiltinToolScripts.Vb14WriteWithoutReadRejected, "vb14" },
        new object[] { VbBuiltinToolScripts.Vb15EditSuccess, "vb15" },
    };

    [Theory]
    [MemberData(nameof(Data))]
    public async Task Vb11_15_ShouldPassAllAsserts(ConversationScript script, string vbId) {
        Output.WriteLine($"[VB] 开始验证 {vbId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VB] {vbId} 验证通过");
    }
}

[Collection("MockServerShared")]
public sealed class VbBuiltinToolTests04 : CoverageTestBase {
    public VbBuiltinToolTests04(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    public static IEnumerable<object[]> Data => new[]
    {
        new object[] { VbBuiltinToolScripts.Vb16EditOldStringNotFound, "vb16" },
        new object[] { VbBuiltinToolScripts.Vb17EditReplaceAll, "vb17" },
        new object[] { VbBuiltinToolScripts.Vb18GlobMatch, "vb18" },
        new object[] { VbBuiltinToolScripts.Vb19GlobNoMatch, "vb19" },
        new object[] { VbBuiltinToolScripts.Vb20GrepMatch, "vb20" },
    };

    [Theory]
    [MemberData(nameof(Data))]
    public async Task Vb16_20_ShouldPassAllAsserts(ConversationScript script, string vbId) {
        Output.WriteLine($"[VB] 开始验证 {vbId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VB] {vbId} 验证通过");
    }
}

[Collection("MockServerShared")]
public sealed class VbBuiltinToolTests05 : CoverageTestBase {
    public VbBuiltinToolTests05(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    public static IEnumerable<object[]> Data => new[]
    {
        new object[] { VbBuiltinToolScripts.Vb21GrepNoMatch, "vb21" },
        new object[] { VbBuiltinToolScripts.Vb22TodoWriteCreate, "vb22" },
        new object[] { VbBuiltinToolScripts.Vb23TodoListList, "vb23" },
        new object[] { VbBuiltinToolScripts.Vb24TodoUpdateStatus, "vb24" },
        new object[] { VbBuiltinToolScripts.Vb25TaskCreate, "vb25" },
    };

    [Theory]
    [MemberData(nameof(Data))]
    public async Task Vb21_25_ShouldPassAllAsserts(ConversationScript script, string vbId) {
        Output.WriteLine($"[VB] 开始验证 {vbId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VB] {vbId} 验证通过");
    }
}

[Collection("MockServerShared")]
public sealed class VbBuiltinToolTests06 : CoverageTestBase {
    public VbBuiltinToolTests06(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    public static IEnumerable<object[]> Data => new[]
    {
        new object[] { VbBuiltinToolScripts.Vb26TaskListList, "vb26" },
        new object[] { VbBuiltinToolScripts.Vb27SkillLoad, "vb27" },
        new object[] { VbBuiltinToolScripts.Vb28SkillNotFound, "vb28" },
        new object[] { VbBuiltinToolScripts.Vb29WebfetchSuccess, "vb29" },
        new object[] { VbBuiltinToolScripts.Vb30WebfetchRedirect, "vb30" },
    };

    [Theory]
    [MemberData(nameof(Data))]
    public async Task Vb26_30_ShouldPassAllAsserts(ConversationScript script, string vbId) {
        Output.WriteLine($"[VB] 开始验证 {vbId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VB] {vbId} 验证通过");
    }
}

[Collection("MockServerShared")]
public sealed class VbBuiltinToolTests07 : CoverageTestBase {
    public VbBuiltinToolTests07(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    public static IEnumerable<object[]> Data => new[]
    {
        new object[] { VbBuiltinToolScripts.Vb31WebfetchFailure, "vb31" },
        new object[] { VbBuiltinToolScripts.Vb32QuestionAskAndAnswer, "vb32" },
        new object[] { VbBuiltinToolScripts.Vb33QuestionOptionValidation, "vb33" },
        new object[] { VbBuiltinToolScripts.Vb34AgentCreateSubAgent, "vb34" },
        new object[] { VbBuiltinToolScripts.Vb35AgentStatusQuery, "vb35" },
    };

    [Theory]
    [MemberData(nameof(Data))]
    public async Task Vb31_35_ShouldPassAllAsserts(ConversationScript script, string vbId) {
        Output.WriteLine($"[VB] 开始验证 {vbId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VB] {vbId} 验证通过");
    }
}
