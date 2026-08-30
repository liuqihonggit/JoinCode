namespace Core.Goal.Tests;


public sealed class GoalUserInteractionServiceTests
{
    private static Mock<IInteractiveService> CreateInteractiveMock() => new();

    private static GoalUserInteractionService CreateService(Mock<IInteractiveService>? mock = null)
    {
        mock ??= CreateInteractiveMock();
        return new GoalUserInteractionService(mock.Object, NullLogger<GoalUserInteractionService>.Instance);
    }

    [Fact]
    public async Task AskToContinueAsync_UserChoosesContinue_ShouldReturnContinue()
    {
        var mock = CreateInteractiveMock();
        mock.Setup(s => s.AskUserQuestionAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AskUserQuestionResult.SuccessResult("继续循环"));

        var service = CreateService(mock);
        var result = await service.AskToContinueAsync("测试问题", 8, 3, timeoutSeconds: 60);

        Assert.True(result.ShouldContinue);
        Assert.False(result.CoordinatorTakenOver);
    }

    [Fact]
    public async Task AskToContinueAsync_UserChoosesStop_ShouldReturnStop()
    {
        var mock = CreateInteractiveMock();
        mock.Setup(s => s.AskUserQuestionAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AskUserQuestionResult.SuccessResult("停止循环"));

        var service = CreateService(mock);
        var result = await service.AskToContinueAsync("测试问题", 7, 2, timeoutSeconds: 60);

        Assert.False(result.ShouldContinue);
        Assert.False(result.CoordinatorTakenOver);
    }

    [Fact]
    public async Task AskToContinueAsync_UserCancels_ShouldReturnCoordinatorTakeover()
    {
        var mock = CreateInteractiveMock();
        mock.Setup(s => s.AskUserQuestionAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AskUserQuestionResult.CancelledResult());

        var service = CreateService(mock);
        var result = await service.AskToContinueAsync("测试问题", 9, 4, timeoutSeconds: 60);

        Assert.True(result.CoordinatorTakenOver);
        Assert.False(result.ShouldContinue);
    }

    [Fact]
    public async Task AskToContinueAsync_Timeout_ShouldReturnCoordinatorTakeover()
    {
        var mock = CreateInteractiveMock();
        mock.Setup(s => s.AskUserQuestionAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (string q, List<string>? opts, bool multi, CancellationToken ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                return AskUserQuestionResult.SuccessResult("继续循环");
            });

        var service = CreateService(mock);
        var result = await service.AskToContinueAsync("测试问题", 6, 1, timeoutSeconds: 1);

        Assert.True(result.CoordinatorTakenOver);
        Assert.False(result.ShouldContinue);
        Assert.Contains("Timeout", result.Reason);
    }

    [Fact]
    public async Task AskToContinueAsync_InteractionFails_ShouldReturnCoordinatorTakeover()
    {
        var mock = CreateInteractiveMock();
        mock.Setup(s => s.AskUserQuestionAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AskUserQuestionResult.FailureResult("Connection lost"));

        var service = CreateService(mock);
        var result = await service.AskToContinueAsync("测试问题", 10, 5, timeoutSeconds: 60);

        Assert.True(result.CoordinatorTakenOver);
        Assert.False(result.ShouldContinue);
    }
}
