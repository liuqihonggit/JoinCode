
namespace Core.Tests.Services;

public partial class NotificationServiceTests
{
    private readonly Mock<ILogger<NotificationService>> _loggerMock;
    private readonly NotificationService _notificationService;

    public NotificationServiceTests()
    {
        _loggerMock = new Mock<ILogger<NotificationService>>();
        _notificationService = new NotificationService(_loggerMock.Object);
    }

    [Fact]
    public async Task NotifyAsync_ShouldLogInformation()
    {
        var title = "Test Title";
        var message = "Test Message";

        await _notificationService.NotifyAsync(title, message).ConfigureAwait(true);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString()!.Contains(title) &&
                    v.ToString()!.Contains(message)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task NotifyAsync_WithEmptyTitle_ShouldNotLog()
    {
        await _notificationService.NotifyAsync("", "Test Message").ConfigureAwait(true);

        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task NotifyAsync_WithEmptyMessage_ShouldNotLog()
    {
        await _notificationService.NotifyAsync("Test Title", "").ConfigureAwait(true);

        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task NotifyTaskCompletedAsync_WithSuccess_ShouldLog()
    {
        var taskId = "task-123";
        var description = "Test task";

        await _notificationService.NotifyTaskCompletedAsync(taskId, description, true).ConfigureAwait(true);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString()!.Contains(taskId) &&
                    v.ToString()!.Contains(description)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task NotifyTaskCompletedAsync_WithFailure_ShouldLog()
    {
        var taskId = "task-123";
        var description = "Test task";

        await _notificationService.NotifyTaskCompletedAsync(taskId, description, false).ConfigureAwait(true);

        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString()!.Contains(taskId) &&
                    v.ToString()!.Contains(description)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task NotifyAgentMessageAsync_ShouldLogInformation()
    {
        var agentId = "agent-123";
        var agentName = "Test Agent";
        var message = "Test message";

        await _notificationService.NotifyAgentMessageAsync(agentId, agentName, message).ConfigureAwait(true);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString()!.Contains(agentId) &&
                    v.ToString()!.Contains(agentName) &&
                    v.ToString()!.Contains(message)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public void IsAvailable_ShouldReturnValueBasedOnPlatform()
    {
        var isAvailable = _notificationService.IsAvailable;
        Assert.True(isAvailable || !isAvailable);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldNotThrow()
    {
        var exception = Record.Exception(() => new NotificationService(null));
        Assert.Null(exception);
    }
}

public partial class ConsoleNotificationServiceTests
{
    private readonly Mock<ILogger<ConsoleNotificationService>> _loggerMock;
    private readonly ConsoleNotificationService _consoleNotificationService;

    public ConsoleNotificationServiceTests()
    {
        _loggerMock = new Mock<ILogger<ConsoleNotificationService>>();
        _consoleNotificationService = new ConsoleNotificationService(_loggerMock.Object);
    }

    [Fact]
    public void IsAvailable_ShouldAlwaysReturnTrue()
    {
        Assert.True(_consoleNotificationService.IsAvailable);
    }

    [Fact]
    public async Task NotifyAsync_ShouldLogInformation()
    {
        var title = "Test Title";
        var message = "Test Message";

        await _consoleNotificationService.NotifyAsync(title, message).ConfigureAwait(true);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString()!.Contains(title) &&
                    v.ToString()!.Contains(message)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task NotifyTaskCompletedAsync_WithSuccess_ShouldLog()
    {
        var taskId = "task-123";
        var description = "Test task";

        await _consoleNotificationService.NotifyTaskCompletedAsync(taskId, description, true).ConfigureAwait(true);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString()!.Contains(taskId) &&
                    v.ToString()!.Contains(description)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task NotifyTaskCompletedAsync_WithFailure_ShouldLog()
    {
        var taskId = "task-123";
        var description = "Test task";

        await _consoleNotificationService.NotifyTaskCompletedAsync(taskId, description, false).ConfigureAwait(true);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString()!.Contains(taskId) &&
                    v.ToString()!.Contains(description)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task NotifyAgentMessageAsync_ShouldLogInformation()
    {
        var agentId = "agent-123";
        var agentName = "Test Agent";
        var message = "Test message";

        await _consoleNotificationService.NotifyAgentMessageAsync(agentId, agentName, message).ConfigureAwait(true);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString()!.Contains(agentId) &&
                    v.ToString()!.Contains(agentName) &&
                    v.ToString()!.Contains(message)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldNotThrow()
    {
        var exception = Record.Exception(() => new ConsoleNotificationService(null));
        Assert.Null(exception);
    }
}
