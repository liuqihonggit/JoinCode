namespace NativeTui.Host.Tests;

/// <summary>
/// ADR 0165 阶段 0 红测试 — 查询生来有身份：Submit 返回 queryId、事件按 queryId 归因、
/// 两条查询并存且 consumer 不串台、Interrupt(queryId) 定向取消。
/// 红点：IReplEngine.Submit 仍返回 void、BridgeEvent 无 QueryId、EngineTurnClient 单回合槽，编译失败即证明地基未实现。
/// </summary>
public sealed class QueryIdentityTests {
    private static async Task WaitAllQueriesAsync(EngineTurnClient client) {
        var deadline = Environment.TickCount64 + 10_000;
        while (client.IsTurnRunning && Environment.TickCount64 < deadline)
            await Task.Delay(10);
        Assert.False(client.IsTurnRunning, "查询未在超时内全部结束");
    }

    [Fact]
    public async Task TwoQueries_SeparateConsumers_ShareSessionLevelMessageIds() {
        var client = new EngineTurnClient((_, consumer, _) => {
            consumer.OnText("输出");
            consumer.OnDone(null, null);
            return Task.CompletedTask;
        });

        client.Submit("A", images: null, queryId: "q-a");
        await WaitAllQueriesAsync(client);
        client.Submit("B", images: null, queryId: "q-b");
        await WaitAllQueriesAsync(client);

        var ids = client.DrainEvents().OfType<BridgeEvent.AssistantMessage>().Select(m => m.MessageId).ToArray();
        // 每查询独立 consumer，但 MessageId 是会话级递增（Ctrl+O 定位 / transcript 对账依赖唯一序号）
        Assert.Equal(2, ids.Length);
        Assert.Equal(2, ids.Distinct().Count());
    }

    [Fact]
    public async Task Submit_ReturnsQueryId_AndTurnEventsCarrySameId() {
        var client = new EngineTurnClient((_, consumer, _) => {
            consumer.OnText("回答");
            return Task.CompletedTask;
        });

        var queryId = client.Submit("你好");

        Assert.False(string.IsNullOrWhiteSpace(queryId));
        await WaitAllQueriesAsync(client);
        var events = client.DrainEvents();
        Assert.NotEmpty(events);
        Assert.All(events, e => Assert.Equal(queryId, e.QueryId));
    }

    [Fact]
    public async Task Submit_TwoQueries_BothAcceptedAndEventsAttributedIndependently() {
        var client = new EngineTurnClient((input, consumer, _) => {
            consumer.OnText($"{input}-output");
            return Task.CompletedTask;
        });

        var first = client.Submit("A");
        var second = client.Submit("B");

        Assert.False(string.IsNullOrWhiteSpace(first));
        Assert.False(string.IsNullOrWhiteSpace(second));
        Assert.NotEqual(first, second);

        await WaitAllQueriesAsync(client);
        var events = client.DrainEvents();

        var firstDelta = Assert.Single(events.OfType<BridgeEvent.TokenDelta>(), e => e.QueryId == first);
        var secondDelta = Assert.Single(events.OfType<BridgeEvent.TokenDelta>(), e => e.QueryId == second);
        Assert.Equal("A-output", firstDelta.Text);
        Assert.Equal("B-output", secondDelta.Text);
        Assert.Contains(events, e => e is BridgeEvent.TurnStarted { QueryId: var q } && q == first);
        Assert.Contains(events, e => e is BridgeEvent.TurnStarted { QueryId: var q } && q == second);
        Assert.Contains(events, e => e is BridgeEvent.TurnCompleted { QueryId: var q } && q == first);
        Assert.Contains(events, e => e is BridgeEvent.TurnCompleted { QueryId: var q } && q == second);
        Assert.DoesNotContain(events, e => e is BridgeEvent.Error);
    }

    [Fact]
    public async Task Interrupt_WithQueryId_CancelsOnlyThatQuery() {
        var releaseSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new EngineTurnClient(async (input, consumer, ct) => {
            if (input == "A") {
                await Task.Delay(Timeout.Infinite, ct);
                return;
            }

            consumer.OnText("B-output");
            await releaseSecond.Task;
        });

        var first = client.Submit("A");
        var second = client.Submit("B");
        client.Interrupt(first);
        releaseSecond.SetResult();

        await WaitAllQueriesAsync(client);
        var events = client.DrainEvents();

        Assert.Contains(events, e => e is BridgeEvent.TurnInterrupted { QueryId: var q } && q == first);
        Assert.Contains(events, e => e is BridgeEvent.TurnCompleted { QueryId: var q } && q == second);
        Assert.DoesNotContain(events, e => e is BridgeEvent.Error);
    }
}
