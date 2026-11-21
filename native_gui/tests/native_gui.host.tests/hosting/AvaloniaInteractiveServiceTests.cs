namespace JoinCode.Gui.Tests.Hosting;

/// <summary>
/// AvaloniaInteractiveService 单元测试 — DSH 问答卡片挂起期间后台回调到达应关闭卡片并返回 Deferred。
/// </summary>
public sealed class AvaloniaInteractiveServiceTests {
    [Fact]
    public async Task AskUserQuestionsAsync_BackgroundNotificationArrives_ReturnsDeferredAndCancelsCard() {
        await using var queue = new AgentNotificationQueue();
        var service = new AvaloniaInteractiveService(logger: null, notificationQueue: queue);
        var callbackCancelled = false;
        service.ShowQuestionsCallback = async (_, ct) => {
            await Task.Yield();
            queue.Enqueue(null, "<task-notification><task-id>g-1</task-id><status>completed</status></task-notification>");

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = ct.Register(() => gate.TrySetCanceled(ct));
            try {
                await gate.Task;
            } catch (OperationCanceledException) {
                callbackCancelled = true;
                throw;
            }
            return AskUserQuestionResult.SuccessResult("是");
        };

        var result = await service.AskUserQuestionAsync("confirm?", ["是", "否"]);

        result.Deferred.Should().BeTrue();
        callbackCancelled.Should().BeTrue("后台回调到达必须取消挂起中的问答卡片");
    }

    [Fact]
    public async Task AskUserQuestionsAsync_UserAnswersBeforeNotification_ReturnsAnswer() {
        await using var queue = new AgentNotificationQueue();
        var service = new AvaloniaInteractiveService(logger: null, notificationQueue: queue);
        service.ShowQuestionsCallback = (_, _) => Task.FromResult(AskUserQuestionResult.SuccessResult("是"));

        var result = await service.AskUserQuestionAsync("confirm?", ["是", "否"]);

        result.Deferred.Should().BeFalse();
        result.Success.Should().BeTrue();
        result.Answer.Should().Be("是");
    }

    [Fact]
    public async Task AskUserQuestionsAsync_OuterCancellation_Propagates() {
        await using var queue = new AgentNotificationQueue();
        var service = new AvaloniaInteractiveService(logger: null, notificationQueue: queue);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.ShowQuestionsCallback = async (_, ct) => {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = ct.Register(() => gate.TrySetCanceled(ct));
            started.TrySetResult();
            await gate.Task;
            return AskUserQuestionResult.SuccessResult("是");
        };
        using var cts = new CancellationTokenSource();

        var asking = service.AskUserQuestionAsync("confirm?", ["是", "否"], cancellationToken: cts.Token);
        await started.Task;
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => asking);
    }
}
