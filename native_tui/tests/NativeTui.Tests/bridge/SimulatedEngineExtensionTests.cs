namespace NativeTui.Tests.Bridge;

/// <summary>
/// SimulatedEngine 新增演示事件测试 — TokenUsage 演示（回合完成发一条带假数据的用量汇总，
/// 事件序：assistant_message → token_usage → turn_completed）+ ToolResult 演示带原始输出
/// （多行输出演示折叠：rawOutput 多行 + hiddenLines > 0）。虚拟时钟注入确定性验证。
/// </summary>
public sealed class SimulatedEngineExtensionTests
{
    /// <summary>虚拟时钟 — 手动推进毫秒，驱动状态机确定性跳段（对齐 SimulatedEngineTests 先例）。</summary>
    private sealed class VirtualClock
    {
        private long _ms;

        /// <summary>当前时刻（ms）。</summary>
        public long Now => _ms;

        /// <summary>推进指定毫秒。</summary>
        public void Advance(long ms) => _ms += ms;
    }

    /// <summary>all 脚本完成拍 — TokenUsage 恰好落在 assistant_message 与 turn_completed 之间（回合收尾汇总语义）。</summary>
    [Fact]
    public void Advance_ToCompletion_EmitsTokenUsageBetweenMessageAndCompleted()
    {
        var clock = new VirtualClock();
        var engine = new SimulatedEngine(() => clock.Now);
        engine.Submit("all");
        engine.DrainEvents();

        clock.Advance(5500);
        var events = engine.DrainEvents().ToList();

        var messageIndex = events.FindIndex(e => e is BridgeEvent.AssistantMessage);
        var usageIndex = events.FindIndex(e => e is BridgeEvent.TokenUsage);
        var completedIndex = events.FindIndex(e => e is BridgeEvent.TurnCompleted);
        Assert.True(messageIndex >= 0, "完成拍应产出 assistant_message");
        Assert.True(usageIndex >= 0, "完成拍应产出 token_usage");
        Assert.True(completedIndex >= 0, "完成拍应产出 turn_completed");
        Assert.True(messageIndex < usageIndex, "token_usage 应在 assistant_message 之后");
        Assert.True(usageIndex < completedIndex, "token_usage 应在 turn_completed 之前");
    }

    /// <summary>TokenUsage 演示假数据 — 四通道全形态（input/output/context/contextLimit，确定性常量）。</summary>
    [Fact]
    public void Advance_ToCompletion_TokenUsageCarriesDemoPayload()
    {
        var clock = new VirtualClock();
        var engine = new SimulatedEngine(() => clock.Now);
        engine.Submit("md");
        engine.DrainEvents();

        clock.Advance(5500);
        var usage = Assert.IsType<BridgeEvent.TokenUsage>(
            engine.DrainEvents().Single(e => e is BridgeEvent.TokenUsage));

        Assert.Equal(1284L, usage.InputTokens);
        Assert.Equal(356L, usage.OutputTokens);
        Assert.Equal(1640L, usage.ContextTokens);
        Assert.Equal(131072L, usage.ContextLimitTokens);
    }

    /// <summary>md 脚本 2.5s — 无工具事件（ToolResult 不产出，rawOutput 演示归 diff/all 脚本）。</summary>
    [Fact]
    public void Advance_MdCommand_NoToolResult()
    {
        var clock = new VirtualClock();
        var engine = new SimulatedEngine(() => clock.Now);
        engine.Submit("md");
        engine.DrainEvents();

        clock.Advance(2500);

        Assert.DoesNotContain(engine.DrainEvents(), e => e is BridgeEvent.ToolResult);
    }

    /// <summary>all 脚本 2.5s — ToolResult 携带多行原始输出（折叠演示：rawOutput 含换行 + hiddenLines > 0）。</summary>
    [Fact]
    public void Advance_ToResponding_ToolResultCarriesMultiLineRawOutput()
    {
        var clock = new VirtualClock();
        var engine = new SimulatedEngine(() => clock.Now);
        engine.Submit("all");
        engine.DrainEvents();

        clock.Advance(2500);
        var toolResult = Assert.IsType<BridgeEvent.ToolResult>(
            engine.DrainEvents().Single(e => e is BridgeEvent.ToolResult));

        Assert.NotNull(toolResult.Patch);
        Assert.NotNull(toolResult.RawOutput);
        Assert.Contains('\n', toolResult.RawOutput);
        Assert.True(toolResult.RawOutput.Split('\n').Length > 3, "演示原始输出应超预览行数（折叠演示）");
        Assert.Equal(3, toolResult.HiddenLines);
    }

    /// <summary>agents 脚本收束拍 — 子代理回合同样发 TokenUsage（并行子代理用量汇总演示）。</summary>
    [Fact]
    public void SubmitAgents_ToCompletion_EmitsTokenUsage()
    {
        var clock = new VirtualClock();
        var engine = new SimulatedEngine(() => clock.Now);
        engine.Submit("agents 3");
        engine.DrainEvents();

        clock.Advance(3000);
        var events = engine.DrainEvents();

        Assert.Contains(events, e => e is BridgeEvent.TokenUsage);
        Assert.Contains(events, e => e is BridgeEvent.TurnCompleted);
    }

    /// <summary>打断的回合不发 TokenUsage（未完成回合无用量汇总 — 汇总语义对齐真实引擎 OnDone 收尾）。</summary>
    [Fact]
    public void InterruptedTurn_DoesNotEmitTokenUsage()
    {
        var clock = new VirtualClock();
        var engine = new SimulatedEngine(() => clock.Now);
        engine.Submit("all");
        engine.DrainEvents();

        clock.Advance(2500);
        engine.DrainEvents();
        engine.Interrupt();
        var events = engine.DrainEvents();

        Assert.Contains(events, e => e is BridgeEvent.TurnInterrupted);
        Assert.DoesNotContain(events, e => e is BridgeEvent.TokenUsage);
    }
}
