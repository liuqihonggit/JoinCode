namespace NativeTui.Host.Tests;

/// <summary>
/// EngineTurnClient 回合生命周期测试 — BridgeServeLoop.DispatchRequest 的进程内直连版本：
/// 回合生命周期事件（turn_started/completed/interrupted）由本类统一发出，回合内部事件由
/// InProcessEventConsumer 发（同队 FIFO 保序：turn_started 必先于回合内部事件）；
/// submit 忙时拒绝（Error 诱导，对齐 bridge 语义）；interrupt 取消在途回合 → turn_interrupted；
/// set_permission_mode → 应用器（解析+闸门）→ permission_mode_changed 回执（实际生效值）。
/// 委托注入假执行器 — 零 engine DI 依赖，纯状态机验证。
/// </summary>
public sealed class EngineTurnClientTests {
    /// <summary>等待回合结束并返回排空的全部事件（终态事件入队先于 busy 标志清零，无竞态）。</summary>
    private static async Task<List<BridgeEvent>> WaitTurnAndDrainAsync(EngineTurnClient client) {
        var deadline = Environment.TickCount64 + 10_000;
        while (client.IsTurnRunning && Environment.TickCount64 < deadline)
            await Task.Delay(10);
        Assert.False(client.IsTurnRunning, "回合未在超时内结束");
        return [.. client.DrainEvents()];
    }

    [Fact]
    public async Task Submit_Idle_EmitsTurnStartedThenInnerEventsThenCompleted() {
        var consumer = default(InProcessEventConsumer);
        var client = new EngineTurnClient((input, c, ct) => {
            consumer = (InProcessEventConsumer)c;
            c.OnText("回答");
            return Task.CompletedTask;
        });

        client.Submit("你好");
        var events = await WaitTurnAndDrainAsync(client);

        Assert.Equal([
            typeof(BridgeEvent.TurnStarted),
            typeof(BridgeEvent.TokenDelta),
            typeof(BridgeEvent.TurnCompleted)], events.Select(e => e.GetType()).ToArray());
    }

    [Fact]
    public async Task Submit_ConsumerResetBetweenTurns_ThinkingSignalsAgain() {
        var client = new EngineTurnClient((input, c, ct) => {
            c.OnThinking("思考");
            return Task.CompletedTask;
        });

        client.Submit("第一回合");
        var first = await WaitTurnAndDrainAsync(client);
        client.Submit("第二回合");
        var second = await WaitTurnAndDrainAsync(client);

        Assert.Equal(3, first.Count);
        Assert.Equal(3, second.Count);
        Assert.IsType<BridgeEvent.ThinkingStarted>(second[1]);
    }

    [Fact]
    public async Task Submit_WhileBusy_RejectsWithInductiveError() {
        var release = new TaskCompletionSource();
        var client = new EngineTurnClient((input, c, ct) => release.Task);

        client.Submit("第一回合");
        client.Submit("第二回合");
        var beforeRelease = client.DrainEvents();
        release.SetResult();
        var turnEvents = await WaitTurnAndDrainAsync(client);

        // 第一回合 TurnStarted 已入队（先于忙时拒绝）+ 拒绝 Error 诱导
        Assert.Equal([
            typeof(BridgeEvent.TurnStarted),
            typeof(BridgeEvent.Error)], beforeRelease.Select(e => e.GetType()).ToArray());
        var error = Assert.IsType<BridgeEvent.Error>(beforeRelease[1]);
        Assert.Contains("Esc", error.Message);
        Assert.IsType<BridgeEvent.TurnCompleted>(Assert.Single(turnEvents));
    }

    [Fact]
    public async Task Interrupt_CancelsInFlightTurn_EmitsTurnInterrupted() {
        var client = new EngineTurnClient(async (input, c, ct) => {
            c.OnText("部分输出");
            await Task.Delay(Timeout.Infinite, ct);
        });

        client.Submit("你好");
        client.Interrupt();
        var events = await WaitTurnAndDrainAsync(client);

        Assert.Equal([
            typeof(BridgeEvent.TurnStarted),
            typeof(BridgeEvent.TokenDelta),
            typeof(BridgeEvent.TurnInterrupted)], events.Select(e => e.GetType()).ToArray());
    }

    [Fact]
    public async Task Submit_ExecutorThrows_EmitsError() {
        var client = new EngineTurnClient((input, c, ct) => throw new InvalidOperationException("引擎超时"));

        client.Submit("你好");
        var events = await WaitTurnAndDrainAsync(client);

        Assert.Equal([
            typeof(BridgeEvent.TurnStarted),
            typeof(BridgeEvent.Error)], events.Select(e => e.GetType()).ToArray());
        var error = Assert.IsType<BridgeEvent.Error>(events[1]);
        Assert.Contains("引擎超时", error.Message);
    }

    [Fact]
    public void SetPermissionMode_Valid_EmitsPermissionModeChangedWithEffectiveValue() {
        var client = new EngineTurnClient(
            (input, c, ct) => Task.CompletedTask,
            applyPermissionMode: mode => mode == "bypass" ? "auto" : mode);

        var applied = client.SetPermissionMode("bypass");
        var events = client.DrainEvents();

        Assert.True(applied);
        var changed = Assert.IsType<BridgeEvent.PermissionModeChanged>(Assert.Single(events));
        Assert.Equal("auto", changed.Mode);
    }

    [Fact]
    public void SetPermissionMode_Invalid_EmitsInductiveErrorWithValidModes() {
        var client = new EngineTurnClient(
            (input, c, ct) => Task.CompletedTask,
            applyPermissionMode: static _ => null);

        var applied = client.SetPermissionMode("fast");
        var events = client.DrainEvents();

        Assert.False(applied);
        var error = Assert.IsType<BridgeEvent.Error>(Assert.Single(events));
        Assert.Contains("plan, auto, ask, bypass, unattended", error.Message);
    }
}
