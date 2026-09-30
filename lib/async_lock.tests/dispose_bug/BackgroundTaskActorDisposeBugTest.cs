namespace Core.Utils;

/// <summary>
/// BackgroundTaskActor Dispose 时序 bug 复现 — CI #691 PR #352 偶发失败根因。
/// <para>根因: Handle 里 _ = ExecuteTaskAsync 是 fire-and-forget,ActorBase.DisposeAsync 只 await _consumerTask,</para>
/// <para>不等待 in-flight 的 ExecuteTaskAsync。Consumer 退出后任务可能仍在跑 → 调用方拿不到任务结果。</para>
/// <para>复现场景: AnalyticsService 构造时 Tell(LoadHistory),DisposeAsync 后 GetEventHistory 拿不到数据。</para>
/// </summary>
public class BackgroundTaskActorDisposeBugTest {
    /// <summary>
    /// 复现: DisposeAsync 不等待 in-flight 任务,任务在 Dispose 返回后仍未完成。
    /// <para>修复前: disposeTask 在 200ms 内完成(未等待 in-flight) → 断言失败。</para>
    /// <para>修复后: disposeTask 阻塞等待 in-flight → delayTask 先完成 → 断言通过。</para>
    /// </summary>
    [Fact]
    public async Task DisposeAsync_ShouldWaitForInFlightTask_NotReturnBeforeTaskCompletes() {
        var taskStarted = new TaskCompletionSource<bool>();
        var releaseGate = new TaskCompletionSource<bool>();
        var actor = new BackgroundTaskActor();

        actor.Tell(new BackgroundTaskCommand("inflight-test", async ct => {
            taskStarted.TrySetResult(true);
            await releaseGate.Task;
        }));

        await taskStarted.Task;

        var disposeTask = actor.DisposeAsync().AsTask();
        var delayTask = Task.Delay(200);
        var winner = await Task.WhenAny(disposeTask, delayTask);

        winner.Should().Be(delayTask,
            "DisposeAsync 应等待 in-flight 任务完成,但 Dispose 在 200ms 内就返回了(未等待 in-flight 任务) — CI #691 根因");

        releaseGate.TrySetResult(true);
        await disposeTask;
    }

    /// <summary>
    /// 复现: 多个 in-flight 任务,DisposeAsync 应全部等待。
    /// </summary>
    [Fact]
    public async Task DisposeAsync_ShouldWaitForAllInFlightTasks_WhenMultiple() {
        var startedCount = 0;
        var startedGate = new TaskCompletionSource<bool>();
        var releaseGate = new TaskCompletionSource<bool>();
        var actor = new BackgroundTaskActor();

        for (var i = 0; i < 3; i++) {
            actor.Tell(new BackgroundTaskCommand($"multi-{i}", async ct => {
                if (Interlocked.Increment(ref startedCount) == 3) {
                    startedGate.TrySetResult(true);
                }
                await releaseGate.Task;
            }));
        }

        await startedGate.Task;

        var disposeTask = actor.DisposeAsync().AsTask();
        var delayTask = Task.Delay(200);
        var winner = await Task.WhenAny(disposeTask, delayTask);

        winner.Should().Be(delayTask,
            "DisposeAsync 应等待所有 in-flight 任务完成,但 Dispose 在 200ms 内就返回了");

        releaseGate.TrySetResult(true);
        await disposeTask;
    }
}
