namespace NativeTui.Tests.Bridge;

/// <summary>
/// SimulatedEngine 单元测试 — Demo 模拟回合的事件化改造（时钟注入确定性验证）：
/// Submit 立即产 TurnStarted、时间推进产阶段事件（ToolCall/ToolResult/TokenDelta/TurnCompleted）、
/// Interrupt 忙时打断、忙时提交拒绝（Error 诱导）、权限模式本地回执统一走事件。
/// </summary>
public sealed class SimulatedEngineTests
{
    /// <summary>虚拟时钟 — 手动推进毫秒，驱动状态机确定性跳段。</summary>
    private sealed class VirtualClock
    {
        private long _ms;

        /// <summary>当前时刻（ms）。</summary>
        public long Now => _ms;

        /// <summary>推进指定毫秒。</summary>
        public void Advance(long ms) => _ms += ms;
    }

    /// <summary>Submit 后首拍排空：立即产出 TurnStarted。</summary>
    [Fact]
    public void Submit_Drains_TurnStartedImmediately()
    {
        var engine = new SimulatedEngine(static () => 0L);
        engine.Submit("hello");
        var events = engine.DrainEvents();
        Assert.Equal([new BridgeEvent.TurnStarted()], events);
    }

    /// <summary>虚拟时钟 1.3s：进入 ToolUse 阶段，产出 ToolCall 事件（Edit — 演示 StructuredDiff 链路）。</summary>
    [Fact]
    public void Advance_ToToolUse_Emits_ToolCall()
    {
        var clock = new VirtualClock();
        var engine = new SimulatedEngine(() => clock.Now);
        engine.Submit("hello");
        engine.DrainEvents();

        clock.Advance(1300);
        Assert.Contains(engine.DrainEvents(), e => e is BridgeEvent.ToolCall { Name: "Edit" });
    }

    /// <summary>虚拟时钟 2.5s：进入 Responding 阶段，产出 ToolResult（带结构化补丁）+ TokenDelta。</summary>
    [Fact]
    public void Advance_ToResponding_Emits_ToolResultWithPatchAndTokenDelta()
    {
        var clock = new VirtualClock();
        var engine = new SimulatedEngine(() => clock.Now);
        engine.Submit("hello");
        engine.DrainEvents();

        clock.Advance(2500);
        var events = engine.DrainEvents();
        Assert.Contains(events, e => e is BridgeEvent.ToolResult);
        var result = Assert.IsType<BridgeEvent.ToolResult>(events.First(e => e is BridgeEvent.ToolResult));
        Assert.NotNull(result.Patch);
        Assert.All(result.Patch, hunk => Assert.False(string.IsNullOrEmpty(hunk.Header)));
        Assert.Contains(events, e => e is BridgeEvent.TokenDelta { Length: 9 });
    }

    /// <summary>虚拟时钟 5.5s：回合完成，产出 AssistantMessage + TurnCompleted，后续拍空。</summary>
    [Fact]
    public void Advance_ToCompletion_Emits_AssistantMessageAndTurnCompleted()
    {
        var clock = new VirtualClock();
        var engine = new SimulatedEngine(() => clock.Now);
        engine.Submit("hello");
        engine.DrainEvents();

        clock.Advance(5500);
        var events = engine.DrainEvents();
        Assert.Contains(events, e => e is BridgeEvent.AssistantMessage { Text.Length: > 0 });
        Assert.Contains(events, e => e is BridgeEvent.TurnCompleted);

        Assert.Empty(engine.DrainEvents());
    }

    /// <summary>忙时 Interrupt：产出 TurnInterrupted，回合终止。</summary>
    [Fact]
    public void Interrupt_WhileBusy_Emits_TurnInterrupted()
    {
        var engine = new SimulatedEngine(static () => 0L);
        engine.Submit("hello");
        engine.DrainEvents();

        engine.Interrupt();
        Assert.Equal([new BridgeEvent.TurnInterrupted()], engine.DrainEvents());
        Assert.Empty(engine.DrainEvents());
    }

    /// <summary>忙时再次 Submit：拒绝并回 Error 诱导（对齐 EngineTurnClient 忙时语义）。</summary>
    [Fact]
    public void Submit_WhileBusy_RejectsWithError()
    {
        var engine = new SimulatedEngine(static () => 0L);
        engine.Submit("first");
        engine.DrainEvents();

        engine.Submit("second");
        Assert.Contains(engine.DrainEvents(), e => e is BridgeEvent.Error { Message.Length: > 0 });
    }

    /// <summary>打断后再 Submit：可开始新回合（状态机复位）。</summary>
    [Fact]
    public void Submit_AfterInterrupt_StartsNewTurn()
    {
        var engine = new SimulatedEngine(static () => 0L);
        engine.Submit("first");
        engine.DrainEvents();
        engine.Interrupt();
        engine.DrainEvents();

        engine.Submit("second");
        Assert.Equal([new BridgeEvent.TurnStarted()], engine.DrainEvents());
    }

    /// <summary>SetPermissionMode：本地立即回执 PermissionModeChanged（统一事件语义）。</summary>
    [Fact]
    public void SetPermissionMode_Emits_PermissionModeChanged()
    {
        var engine = new SimulatedEngine(static () => 0L);
        engine.SetPermissionMode("plan");
        Assert.Equal([new BridgeEvent.PermissionModeChanged("plan")], engine.DrainEvents());
    }

    /// <summary>IsAlive 恒 true + DrainDiagnostics 默认空（接口默认实现）。</summary>
    [Fact]
    public void AliveForever_And_NoDiagnostics()
    {
        var engine = new SimulatedEngine(static () => 0L);
        Assert.True(engine.IsAlive);
        Assert.Empty(((IReplEngine)engine).DrainDiagnostics());
    }

    /// <summary>闲时 Interrupt：无回合可打断，no-op 不产事件。</summary>
    [Fact]
    public void Interrupt_WhileIdle_IsNoOp()
    {
        var engine = new SimulatedEngine(static () => 0L);
        engine.Interrupt();
        Assert.Empty(engine.DrainEvents());
    }
}
