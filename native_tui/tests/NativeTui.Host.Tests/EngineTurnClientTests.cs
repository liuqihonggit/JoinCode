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

    // ==== G3 图片附件投影（E4 引擎协议 — string-only 链路降级引用段）====

    [Fact]
    public async Task Submit_WithImages_AppendsAttachmentReferenceSummary() {
        var received = default(string);
        var client = new EngineTurnClient((input, c, ct) => {
            received = input;
            return Task.CompletedTask;
        });

        client.Submit("看这张图", [
            new BridgeImageAttachment("QUJD", "image/png", "a.png"),
            new BridgeImageAttachment("QkM=", "image/jpeg", "b.jpg"),
        ]);
        await WaitTurnAndDrainAsync(client);

        // G3 投影 — 附件引用段追加到回合输入（引擎 string-only 结论的降级形态：模型感知图片存在 + 元数据）
        Assert.Equal("看这张图\n\n[Image #1: a.png (image/png)]\n[Image #2: b.jpg (image/jpeg)]", received);
    }

    [Fact]
    public async Task Submit_WithImages_EmptyText_SummaryAlone() {
        var received = default(string);
        var client = new EngineTurnClient((input, c, ct) => {
            received = input;
            return Task.CompletedTask;
        });

        client.Submit(string.Empty, [new BridgeImageAttachment("QUJD", "image/png", "a.png")]);
        await WaitTurnAndDrainAsync(client);

        // 剥离后空文本 — 投影段兜底非空（纯图片提交形态，引擎不收空回合输入）
        Assert.Equal("[Image #1: a.png (image/png)]", received);
    }

    [Fact]
    public async Task Submit_WithoutImages_InputUnchanged() {
        var received = default(string);
        var client = new EngineTurnClient((input, c, ct) => {
            received = input;
            return Task.CompletedTask;
        });

        client.Submit("纯文本", images: null);
        await WaitTurnAndDrainAsync(client);

        // 零破坏 — 无附件原文透传（不追加任何投影段）
        Assert.Equal("纯文本", received);
    }

    // ==== G3 新会话（D8 Clear — 清上下文先于回合执行器串行化）====

    [Fact]
    public async Task StartNewConversation_BeforeSubmit_ClearsBeforeTurnExecutor() {
        var clearStarted = false;
        var clearRelease = new TaskCompletionSource();
        var executorRanAfterClear = false;
        var client = new EngineTurnClient(
            (input, c, ct) => {
                executorRanAfterClear = clearStarted && clearRelease.Task.IsCompleted;
                return Task.CompletedTask;
            },
            startNewConversation: () => {
                clearStarted = true;
                return clearRelease.Task;
            });

        client.StartNewConversation();
        client.Submit("新会话首回合");
        await Task.Delay(50);
        // 清上下文未完成 — 回合执行器不跑（串行化：防清历史与回合读上下文竞态）
        Assert.False(executorRanAfterClear);
        clearRelease.SetResult();
        var events = await WaitTurnAndDrainAsync(client);

        // 清完成 → 回合执行器跑 → 正常终态
        Assert.True(executorRanAfterClear);
        Assert.Contains(events, e => e is BridgeEvent.TurnCompleted);
    }

    [Fact]
    public async Task StartNewConversation_WithoutStarter_TurnProceedsNormally() {
        var client = new EngineTurnClient((input, c, ct) => Task.CompletedTask);

        // 无启动器（null）纯标记 — 回合照常（测试桩/无会话概念宿主零成本）
        client.StartNewConversation();
        client.Submit("普通回合");
        var events = await WaitTurnAndDrainAsync(client);

        Assert.Contains(events, e => e is BridgeEvent.TurnCompleted);
    }

    [Fact]
    public async Task StartNewConversation_ClearFails_EmitsInductiveErrorAndContinuesTurn() {
        var client = new EngineTurnClient(
            (input, c, ct) => Task.CompletedTask,
            startNewConversation: () => Task.FromException(new InvalidOperationException("存储不可达")));

        client.StartNewConversation();
        client.Submit("清理失败后的回合");
        var events = await WaitTurnAndDrainAsync(client);

        // 清失败降级 — Error 诱导（继续原会话 + 重试提示）+ 回合照常完成（清历史失败不吞用户提交）
        var error = Assert.IsType<BridgeEvent.Error>(events.Single(e => e is BridgeEvent.Error));
        Assert.Contains("新会话清理失败", error.Message);
        Assert.Contains("存储不可达", error.Message);
        Assert.Contains(events, e => e is BridgeEvent.TurnCompleted);
    }

    [Fact]
    public async Task StartNewConversation_WhileTurnRunning_TakesEffectNextTurn() {
        var release = new TaskCompletionSource();
        var clears = 0;
        var secondTurnInput = default(string);
        var client = new EngineTurnClient((input, c, ct) => {
            if (input == "第二回合") {
                secondTurnInput = input;
            }

            return release.Task;
        }, startNewConversation: () => {
            clears++;
            return Task.CompletedTask;
        });

        client.Submit("第一回合");
        await Task.Delay(50);
        // 忙时调用 — 只置标记不打断在途回合（清上下文挂起到回合结束后的下回合生效）
        client.StartNewConversation();
        Assert.Equal(0, clears);
        release.SetResult();
        await WaitTurnAndDrainAsync(client);

        client.Submit("第二回合");
        var events = await WaitTurnAndDrainAsync(client);

        // 下回合开始前消费标记 — 清恰一次 + 回合正常
        Assert.Equal(1, clears);
        Assert.Equal("第二回合", secondTurnInput);
        Assert.Contains(events, e => e is BridgeEvent.TurnCompleted);
    }
}
