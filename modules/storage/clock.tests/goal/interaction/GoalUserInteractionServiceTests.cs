namespace Core.Goal.Tests;


public sealed class GoalUserInteractionServiceTests {
    private static Mock<IInteractiveService> CreateInteractiveMock() => new();

    private static GoalUserInteractionService CreateService(Mock<IInteractiveService>? mock = null) {
        mock ??= CreateInteractiveMock();
        return new GoalUserInteractionService(mock.Object, permissionManager: null, logger: NullLogger<GoalUserInteractionService>.Instance);
    }

    [Fact]
    public async Task AskToContinueAsync_UserChoosesContinue_ShouldReturnContinue() {
        var mock = CreateInteractiveMock();
        mock.Setup(s => s.AskUserQuestionAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AskUserQuestionResult.SuccessResult("继续循环"));

        await using var service = CreateService(mock);
        var result = await service.AskToContinueAsync("测试问题", 8, 3, timeoutSeconds: 60);

        Assert.True(result.ShouldContinue);
        Assert.False(result.CoordinatorTakenOver);
    }

    [Fact]
    public async Task AskToContinueAsync_UserChoosesStop_ShouldReturnStop() {
        var mock = CreateInteractiveMock();
        mock.Setup(s => s.AskUserQuestionAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AskUserQuestionResult.SuccessResult("停止循环"));

        await using var service = CreateService(mock);
        var result = await service.AskToContinueAsync("测试问题", 7, 2, timeoutSeconds: 60);

        Assert.False(result.ShouldContinue);
        Assert.False(result.CoordinatorTakenOver);
    }

    [Fact]
    public async Task AskToContinueAsync_UserCancels_ShouldReturnCoordinatorTakeover() {
        var mock = CreateInteractiveMock();
        mock.Setup(s => s.AskUserQuestionAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AskUserQuestionResult.CancelledResult());

        await using var service = CreateService(mock);
        var result = await service.AskToContinueAsync("测试问题", 9, 4, timeoutSeconds: 60);

        Assert.True(result.CoordinatorTakenOver);
        Assert.False(result.ShouldContinue);
    }

    [Fact]
    public async Task AskToContinueAsync_Timeout_ShouldReturnCoordinatorTakeover() {
        var mock = CreateInteractiveMock();
        mock.Setup(s => s.AskUserQuestionAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (string q, List<string>? opts, bool multi, CancellationToken ct) => {
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                return AskUserQuestionResult.SuccessResult("继续循环");
            });

        await using var service = CreateService(mock);
        var result = await service.AskToContinueAsync("测试问题", 6, 1, timeoutSeconds: 1);

        Assert.True(result.CoordinatorTakenOver);
        Assert.False(result.ShouldContinue);
        Assert.Contains("Timeout", result.Reason);
    }

    [Fact]
    public async Task AskToContinueAsync_UnattendedMode_SkipsInteractionAndStops() {
        var mock = CreateInteractiveMock();
        var permissionManager = new Mock<IToolPermissionManager>();
        permissionManager.Setup(x => x.GetCurrentModeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PermissionMode.Unattended);

        await using var service = new GoalUserInteractionService(
            mock.Object,
            permissionManager.Object,
            NullLogger<GoalUserInteractionService>.Instance);

        var result = await service.AskToContinueAsync("测试问题", 8, 3, timeoutSeconds: 60);

        Assert.False(result.ShouldContinue);
        Assert.False(result.CoordinatorTakenOver);
        mock.Verify(s => s.AskUserQuestionAsync(
            It.IsAny<string>(),
            It.IsAny<List<string>?>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AskToContinueAsync_InteractionFails_ShouldReturnCoordinatorTakeover() {
        var mock = CreateInteractiveMock();
        mock.Setup(s => s.AskUserQuestionAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AskUserQuestionResult.FailureResult("Connection lost"));

        await using var service = CreateService(mock);
        var result = await service.AskToContinueAsync("测试问题", 10, 5, timeoutSeconds: 60);

        Assert.True(result.CoordinatorTakenOver);
        Assert.False(result.ShouldContinue);
    }
}