namespace MockServer.E2E.Tests;

/// <summary>
/// VE 组多轮+子代理+会话 E2E 测试 — 覆盖 ve01-ve25 行为助记符。
/// 每个助记符对应一个 ConversationScript，验证多轮对话/子代理/会话恢复的特定行为信号。
/// ve17/ve20/ve21/ve22 因 ConversationScript 无法表达多进程/运行中子代理打断/transcript 文件/大上下文，
/// 标注 TODO 暂用模拟方式覆盖链路，真实场景需手动 exe 验证（ADR 0080）。
/// </summary>
[Collection("MockServerShared")]
public sealed class VeMultiTurnTests : CoverageTestBase {
    /// <summary>
    /// 初始化 VE 组多轮+子代理+会话测试实例。
    /// </summary>
    /// <param name="output">xUnit 测试输出辅助器。</param>
    /// <param name="fixture">共享 MockServer 固件。</param>
    public VeMultiTurnTests(ITestOutputHelper output, MockServerSharedFixture fixture) : base(output, fixture) { }

    /// <summary>
    /// VE 组多轮+子代理+会话测试数据 — 引用 VeMultiTurnScripts 中的脚本。
    /// 每条数据包含 (ConversationScript, veId) 用于参数化测试。
    /// 覆盖 ve01-ve25 共 25 个助记符。
    /// </summary>
    public static IEnumerable<object[]> VeMultiTurnScriptsData => new[]
    {
        new object[] { VeMultiTurnScripts.Ve01ContinueResumeHistory, "ve01" },
        new object[] { VeMultiTurnScripts.Ve02ContinueNoHistory, "ve02" },
        new object[] { VeMultiTurnScripts.Ve03ResumeIdExactMatch, "ve03" },
        new object[] { VeMultiTurnScripts.Ve04ResumeIdNotFound, "ve04" },
        new object[] { VeMultiTurnScripts.Ve05ResumeTitleFuzzyMatch, "ve05" },
        new object[] { VeMultiTurnScripts.Ve06MultiTurnMessagesSequence, "ve06" },
        new object[] { VeMultiTurnScripts.Ve07TranscriptWrite, "ve07" },
        new object[] { VeMultiTurnScripts.Ve08TranscriptRestore, "ve08" },
        new object[] { VeMultiTurnScripts.Ve09SessionIdConsistency, "ve09" },
        new object[] { VeMultiTurnScripts.Ve10SubagentTypeParse, "ve10" },
        new object[] { VeMultiTurnScripts.Ve11SubagentStarted, "ve11" },
        new object[] { VeMultiTurnScripts.Ve12SubagentFinished, "ve12" },
        new object[] { VeMultiTurnScripts.Ve13SubagentExceptionFallback, "ve13" },
        new object[] { VeMultiTurnScripts.Ve14SubagentControl, "ve14" },
        new object[] { VeMultiTurnScripts.Ve15SubagentUsageAggregate, "ve15" },
        new object[] { VeMultiTurnScripts.Ve16NumTurnsCount, "ve16" },
        new object[] { VeMultiTurnScripts.Ve17ConcurrentSessions, "ve17" },
        new object[] { VeMultiTurnScripts.Ve18ForkSubagent, "ve18" },
        new object[] { VeMultiTurnScripts.Ve19TeammateSubagent, "ve19" },
        new object[] { VeMultiTurnScripts.Ve20SubagentSteer, "ve20" },
        new object[] { VeMultiTurnScripts.Ve21SubagentResumeFromTranscript, "ve21" },
        new object[] { VeMultiTurnScripts.Ve22AutoCompact, "ve22" },
        new object[] { VeMultiTurnScripts.Ve23ContextUsage, "ve23" },
        new object[] { VeMultiTurnScripts.Ve24PermissionDenialsAccumulate, "ve24" },
        new object[] { VeMultiTurnScripts.Ve25ThinkingPersist, "ve25" },
    };

    /// <summary>
    /// 验证 VE 组多轮+子代理+会话脚本所有断言通过。
    /// 覆盖 ve01-ve25 共 25 个助记符，包含会话恢复/多轮对话/子代理启动完成/自动压缩等场景。
    /// </summary>
    /// <param name="script">对话脚本。</param>
    /// <param name="veId">助记符编号（ve01-ve25）。</param>
    [Theory]
    [MemberData(nameof(VeMultiTurnScriptsData))]
    public async Task VeMultiTurnScript_ShouldPassAllAsserts(ConversationScript script, string veId) {
        Output.WriteLine($"[VE] 开始验证 {veId}: {script.Name}");
        await RunScriptAsync(script).ConfigureAwait(true);
        Output.WriteLine($"[VE] {veId} 验证通过");
    }
}
