namespace MockServer.E2E.Tests;

/// <summary>
/// VE 组多轮+子代理+会话 E2E 测试 — 拆分为 5 个小测试类（每 5 个用例一个），
/// 避免单类 25 个 Theory 用例并行启动过多 jcc.exe 进程导致测试主机崩溃。
/// 每个小类用 [Collection("MockServerShared")] 串行化，崩溃隔离不影响其他小类。
/// ve17/ve20/ve21/ve22 需手动 exe 验证（ADR 0080），脚本覆盖链路。
/// </summary>

[Collection("MockServerShared")]
public sealed class VeMultiTurnTests01 : CoverageTestBase {
    public VeMultiTurnTests01(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    public static IEnumerable<object[]> Data => new[]
    {
        new object[] { VeMultiTurnScripts.Ve01ContinueResumeHistory, "ve01" },
        new object[] { VeMultiTurnScripts.Ve02ContinueNoHistory, "ve02" },
        new object[] { VeMultiTurnScripts.Ve03ResumeIdExactMatch, "ve03" },
        new object[] { VeMultiTurnScripts.Ve04ResumeIdNotFound, "ve04" },
        new object[] { VeMultiTurnScripts.Ve05ResumeTitleFuzzyMatch, "ve05" },
    };

    [Theory]
    [MemberData(nameof(Data))]
    public async Task Ve01_05_ShouldPassAllAsserts(ConversationScript script, string veId) {
        Output.WriteLine($"[VE] 开始验证 {veId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VE] {veId} 验证通过");
    }
}

[Collection("MockServerShared")]
public sealed class VeMultiTurnTests02 : CoverageTestBase {
    public VeMultiTurnTests02(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    public static IEnumerable<object[]> Data => new[]
    {
        new object[] { VeMultiTurnScripts.Ve06MultiTurnMessagesSequence, "ve06" },
        new object[] { VeMultiTurnScripts.Ve07TranscriptWrite, "ve07" },
        new object[] { VeMultiTurnScripts.Ve08TranscriptRestore, "ve08" },
        new object[] { VeMultiTurnScripts.Ve09SessionIdConsistency, "ve09" },
        new object[] { VeMultiTurnScripts.Ve10SubagentTypeParse, "ve10" },
    };

    [Theory]
    [MemberData(nameof(Data))]
    public async Task Ve06_10_ShouldPassAllAsserts(ConversationScript script, string veId) {
        Output.WriteLine($"[VE] 开始验证 {veId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VE] {veId} 验证通过");
    }
}

[Collection("MockServerShared")]
public sealed class VeMultiTurnTests03 : CoverageTestBase {
    public VeMultiTurnTests03(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    public static IEnumerable<object[]> Data => new[]
    {
        new object[] { VeMultiTurnScripts.Ve11SubagentStarted, "ve11" },
        new object[] { VeMultiTurnScripts.Ve12SubagentFinished, "ve12" },
        new object[] { VeMultiTurnScripts.Ve13SubagentExceptionFallback, "ve13" },
        new object[] { VeMultiTurnScripts.Ve14SubagentControl, "ve14" },
        new object[] { VeMultiTurnScripts.Ve15SubagentUsageAggregate, "ve15" },
    };

    [Theory]
    [MemberData(nameof(Data))]
    public async Task Ve11_15_ShouldPassAllAsserts(ConversationScript script, string veId) {
        Output.WriteLine($"[VE] 开始验证 {veId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VE] {veId} 验证通过");
    }
}

[Collection("MockServerShared")]
public sealed class VeMultiTurnTests04 : CoverageTestBase {
    public VeMultiTurnTests04(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    public static IEnumerable<object[]> Data => new[]
    {
        new object[] { VeMultiTurnScripts.Ve16NumTurnsCount, "ve16" },
        new object[] { VeMultiTurnScripts.Ve17ConcurrentSessions, "ve17" },
        new object[] { VeMultiTurnScripts.Ve18ForkSubagent, "ve18" },
        new object[] { VeMultiTurnScripts.Ve19TeammateSubagent, "ve19" },
        new object[] { VeMultiTurnScripts.Ve20SubagentSteer, "ve20" },
    };

    [Theory]
    [MemberData(nameof(Data))]
    public async Task Ve16_20_ShouldPassAllAsserts(ConversationScript script, string veId) {
        Output.WriteLine($"[VE] 开始验证 {veId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VE] {veId} 验证通过");
    }
}

[Collection("MockServerShared")]
public sealed class VeMultiTurnTests05 : CoverageTestBase {
    public VeMultiTurnTests05(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    public static IEnumerable<object[]> Data => new[]
    {
        new object[] { VeMultiTurnScripts.Ve21SubagentResumeFromTranscript, "ve21" },
        new object[] { VeMultiTurnScripts.Ve22AutoCompact, "ve22" },
        new object[] { VeMultiTurnScripts.Ve23ContextUsage, "ve23" },
        new object[] { VeMultiTurnScripts.Ve24PermissionDenialsAccumulate, "ve24" },
        new object[] { VeMultiTurnScripts.Ve25ThinkingPersist, "ve25" },
    };

    [Theory]
    [MemberData(nameof(Data))]
    public async Task Ve21_25_ShouldPassAllAsserts(ConversationScript script, string veId) {
        Output.WriteLine($"[VE] 开始验证 {veId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VE] {veId} 验证通过");
    }
}
