namespace Core.Utils;

/// <summary>
/// in-flight 内存泄漏测试 — DSG033 方案B。
/// 验证高频 Tell 后 _inFlightTasks 队列长度有界,已完成 Task 被清理可 GC。
/// </summary>
public class InFlightLeakTest {
    /// <summary>
    /// 确定性验证: 高频 Tell 同步完成任务后,in-flight 队列长度有界。
    /// 同步完成 TaskFactory 确保 Tell 期间任务立即 IsCompleted,触发清理时已完成项被移除。
    /// </summary>
    [Fact]
    public async Task HighFrequencyTell_InFlightQueueStaysBounded() {
        const int count = 600;
        var completed = 0;
        await using var actor = new BackgroundTaskActor(boundedCapacity: 1024);

        for (var i = 0; i < count; i++) {
            var taskName = $"t-{i}";
            actor.Tell(new BackgroundTaskCommand(taskName, _ => {
                Interlocked.Increment(ref completed);
                return Task.CompletedTask;
            }));
        }

        await TestWaitHelper.WaitUntilAsync(
            () => Volatile.Read(ref completed) >= count,
            TimeSpan.FromSeconds(2));

        actor.InFlightCountForTest.Should().BeLessThan(320,
            "高频 Tell 后已完成 Task 应被水位线清理,队列长度有界(阈值256+保留64=320),防止长生命周期 Actor 内存泄漏");
    }

    /// <summary>
    /// 确定性验证: DisposeAsync 清理后,in-flight 队列中已完成项被移除。
    /// Dispose 后队列应只含未完成项(此时应全部完成,队列接近空)。
    /// </summary>
    [Fact]
    public async Task DisposeAsync_CleansCompletedInFlight_QueueNearEmpty() {
        const int count = 600;
        var completed = 0;
        var actor = new BackgroundTaskActor(boundedCapacity: 1024);

        for (var i = 0; i < count; i++) {
            var taskName = $"t-{i}";
            actor.Tell(new BackgroundTaskCommand(taskName, _ => {
                Interlocked.Increment(ref completed);
                return Task.CompletedTask;
            }));
        }

        await TestWaitHelper.WaitUntilAsync(
            () => Volatile.Read(ref completed) >= count,
            TimeSpan.FromSeconds(2));

        await actor.DisposeAsync();

        actor.InFlightCountForTest.Should().BeLessThan(70,
            "Dispose 后已完成 in-flight 应被清理,队列接近空(仅保留最近64个诊断项)");
    }
}
