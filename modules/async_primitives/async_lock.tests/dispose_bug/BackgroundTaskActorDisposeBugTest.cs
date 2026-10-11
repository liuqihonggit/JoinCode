namespace Core.Utils;

/// <summary>
/// BackgroundTaskActor Dispose 时序 bug 复现 — CI #691 PR #352 偶发失败根因。
/// <para>根因: Handle 里 fire-and-forget,ActorBase.DisposeAsync 只 await _consumerTask,</para>
/// <para>不等待 in-flight 任务。Consumer 退出后任务可能仍在跑 → 调用方拿不到任务结果。</para>
/// <para>方法论: 拆分确定性验证(Dispose 后 in-flight 一定完成) + 时序验证(重试16次×500ms)。</para>
/// </summary>
public class BackgroundTaskActorDisposeBugTest {
    /// <summary>
    /// 确定性验证: DisposeAsync 返回后,in-flight 任务一定已完成。
    /// <para>不依赖 WhenAny/delay — 直接检查 Dispose 后任务完成标志。</para>
    /// <para>修复前: Dispose 不等 in-flight → Dispose 返回时任务可能未完成 → IsCompleted=false → 失败。</para>
    /// <para>修复后: Dispose 等 in-flight → Dispose 返回时任务一定完成 → IsCompleted=true → 通过。</para>
    /// </summary>
    [Fact]
    public async Task DisposeAsync_ReturnsAfterInFlightTaskCompletes_Deterministic() {
        var taskStarted = new TaskCompletionSource<bool>();
        var taskCompleted = new TaskCompletionSource<bool>();
        var actor = new BackgroundTaskActor();

        actor.Tell(new BackgroundTaskCommand("deterministic-test", async ct => {
            taskStarted.TrySetResult(true);
            await Task.Delay(50);
            taskCompleted.TrySetResult(true);
        }));

        await taskStarted.Task;
        await actor.DisposeAsync();

        taskCompleted.Task.IsCompleted.Should().BeTrue(
            "DisposeAsync 返回后 in-flight 任务一定已完成 — Dispose 必须等待 in-flight 完成(CI #691 根因)");
    }

    /// <summary>
    /// 确定性验证: 多个 in-flight 任务,DisposeAsync 返回后全部完成。
    /// </summary>
    [Fact]
    public async Task DisposeAsync_ReturnsAfterAllInFlightTasksComplete_Deterministic() {
        const int count = 3;
        var startedCount = 0;
        var startedGate = new TaskCompletionSource<bool>();
        var completedCount = 0;
        var actor = new BackgroundTaskActor();

        for (var i = 0; i < count; i++) {
            actor.Tell(new BackgroundTaskCommand($"multi-{i}", async ct => {
                if (Interlocked.Increment(ref startedCount) == count) {
                    startedGate.TrySetResult(true);
                }
                await Task.Delay(50);
                Interlocked.Increment(ref completedCount);
            }));
        }

        await startedGate.Task;
        await actor.DisposeAsync();

        Volatile.Read(ref completedCount).Should().Be(count,
            "DisposeAsync 返回后所有 in-flight 任务一定已完成");
    }

    /// <summary>
    /// 确定性验证: in-flight 任务抛异常时 DisposeAsync 不传播异常,继续释放 CTS。
    /// </summary>
    [Fact]
    public async Task DisposeAsync_InFlightTaskThrows_DoesNotPropagate_Deterministic() {
        var taskStarted = new TaskCompletionSource<bool>();
        var actor = new BackgroundTaskActor();

        actor.Tell(new BackgroundTaskCommand("throwing-test", async ct => {
            taskStarted.TrySetResult(true);
            await Task.Delay(20);
            throw new InvalidOperationException("in-flight task failure");
        }));

        await taskStarted.Task;
        var dispose = async () => await actor.DisposeAsync();
        await dispose.Should().NotThrowAsync("DisposeAsync 应吞没 in-flight 任务异常,确保 CTS 释放");
    }

    /// <summary>
    /// 时序验证: DisposeAsync 阻塞等待 in-flight 任务,不会在 500ms 内返回。
    /// <para>用 releaseGate 挂起 in-flight 任务,Dispose 必须阻塞。重试16次×500ms 防偶发。</para>
    /// </summary>
    [Fact]
    [Trait("Category", "Flaky")]
    public async Task DisposeAsync_BlocksUntilInFlightCompletes_Retry16Times() {
        for (var attempt = 0; attempt < 16; attempt++) {
            var taskStarted = new TaskCompletionSource<bool>();
            var releaseGate = new TaskCompletionSource<bool>();
            var actor = new BackgroundTaskActor();

            actor.Tell(new BackgroundTaskCommand($"retry-{attempt}", async ct => {
                taskStarted.TrySetResult(true);
                await releaseGate.Task;
            }));

            await taskStarted.Task;

            var disposeTask = actor.DisposeAsync().AsTask();
            var delayTask = Task.Delay(500);
            var winner = await Task.WhenAny(disposeTask, delayTask);

            releaseGate.TrySetResult(true);
            await disposeTask;

            if (winner == delayTask) {
                return;
            }
        }

        throw new Xunit.Sdk.XunitException("16次重试均失败: DisposeAsync 未阻塞等待 in-flight 任务(每次都在 500ms 内返回)");
    }
}
