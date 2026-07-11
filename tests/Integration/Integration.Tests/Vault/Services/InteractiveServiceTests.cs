
namespace Integration.Tests.Vault.Services;

public class InteractiveServiceTests
{
    private readonly Mock<ILogger<InteractiveService>> _loggerMock;
    private readonly InteractiveService _interactiveService;

    public InteractiveServiceTests()
    {
        _loggerMock = new Mock<ILogger<InteractiveService>>();
        _interactiveService = new InteractiveService(logger: _loggerMock.Object, clock: JoinCode.Abstractions.Clock.SystemClockService.Instance);
    }

    [Fact]
    public async Task AskUserQuestionAsync_ShouldReturnSuccessResult()
    {
        // Arrange
        var question = "Test question";

        // Act
        var result = await _interactiveService.AskUserQuestionAsync(question).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Answer);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(question)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task AskUserQuestionAsync_WithOptions_ShouldLogOptions()
    {
        // Arrange
        var question = "Test question";
        var options = new List<string> { "Option 1", "Option 2" };

        // Act
        var result = await _interactiveService.AskUserQuestionAsync(question, options).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task EnterPlanModeAsync_ShouldSetPlanModeStatus()
    {
        // Arrange
        var goal = "Test goal";

        // Act
        var result = await _interactiveService.EnterPlanModeAsync(goal: goal).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.PlanId);
        var status = await _interactiveService.GetPlanModeStatusAsync().ConfigureAwait(true);
        Assert.True(status.IsInPlanMode);
        Assert.Equal(goal, status.CurrentGoal);
    }

    [Fact]
    public async Task EnterPlanModeAsync_WithInitialPlan_ShouldParseSteps()
    {
        // Arrange
        var initialPlan = "Step 1\nStep 2\nStep 3";
        var goal = "Test goal";

        // Act
        var result = await _interactiveService.EnterPlanModeAsync(initialPlan, goal).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        var status = await _interactiveService.GetPlanModeStatusAsync().ConfigureAwait(true);
        Assert.Equal(3, status.Steps?.Count);
    }

    [Fact]
    public async Task ExitPlanModeAsync_WhenNotInPlanMode_ShouldReturnFailure()
    {
        // Act
        var result = await _interactiveService.ExitPlanModeAsync().ConfigureAwait(true);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("不在计划模式", result.ErrorMessage);
    }

    [Fact]
    public async Task ExitPlanModeAsync_WhenInPlanMode_ShouldClearStatus()
    {
        // Arrange
        await _interactiveService.EnterPlanModeAsync(goal: "Test goal").ConfigureAwait(true);

        // Act
        var result = await _interactiveService.ExitPlanModeAsync().ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        var status = await _interactiveService.GetPlanModeStatusAsync().ConfigureAwait(true);
        Assert.False(status.IsInPlanMode);
        Assert.Null(status.CurrentPlanId);
        Assert.Null(status.CurrentGoal);
    }

    [Fact]
    public async Task ExitPlanModeAsync_WithConfirmFalse_ShouldReturnFailure()
    {
        // Arrange
        await _interactiveService.EnterPlanModeAsync(goal: "Test goal").ConfigureAwait(true);

        // Act
        var result = await _interactiveService.ExitPlanModeAsync(confirm: false).ConfigureAwait(true);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("取消", result.ErrorMessage);
    }

    [Fact]
    public async Task GetPlanModeStatusAsync_Initially_ShouldReturnNotInPlanMode()
    {
        // Act
        var status = await _interactiveService.GetPlanModeStatusAsync().ConfigureAwait(true);

        // Assert
        Assert.False(status.IsInPlanMode);
        Assert.Null(status.CurrentPlanId);
        Assert.Null(status.CurrentGoal);
        Assert.Null(status.EnteredAt);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldNotThrow()
    {
        // Act & Assert
        var exception = Record.Exception(() => new InteractiveService(logger: null, clock: JoinCode.Abstractions.Clock.SystemClockService.Instance));
        Assert.Null(exception);
    }
}
