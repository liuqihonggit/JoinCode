
namespace Dream.Tests;

/// <summary>
/// DreamFeature 单元测试
/// </summary>
public sealed class DreamFeatureTests
{
    private readonly Mock<IChatCompletionClient> _chatCompletionClientMock;
    private readonly Mock<ISessionScanner> _sessionScannerMock;
    private readonly InMemoryDreamTaskRegistry _taskRegistry;
    private readonly DreamFeature _feature;

    public DreamFeatureTests()
    {
        _chatCompletionClientMock = new Mock<IChatCompletionClient>();
        _sessionScannerMock = new Mock<ISessionScanner>();
        _taskRegistry = new InMemoryDreamTaskRegistry();

        _feature = new DreamFeature(
            _chatCompletionClientMock.Object,
            _sessionScannerMock.Object,
            _taskRegistry,
            new AutoDreamConfig { Enabled = true, MinHours = 1, MinSessions = 1 },
            pipeline: null,
            logger: Mock.Of<ILogger<DreamFeature>>());
    }

    private void SetupChatCompletionResponse(string content)
    {
        _chatCompletionClientMock
            .Setup(c => c.GetCompletionAsync(
                It.IsAny<MessageList>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(content);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDisabledAndNotForce_ReturnsSkippedResult()
    {
        // Arrange
        var config = new AutoDreamConfig { Enabled = false };
        var feature = new DreamFeature(
            _chatCompletionClientMock.Object,
            _sessionScannerMock.Object,
            _taskRegistry,
            config);

        // Act
        var result = await feature.ExecuteAsync(new DreamRequest(Force: false)).ConfigureAwait(true);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.IsSkipped);
        Assert.Contains("禁用", result.Content);
    }

    [Fact]
    public async Task ExecuteAsync_WhenForceTrue_IgnoresGateCheck()
    {
        // Arrange
        _sessionScannerMock.Setup(s => s.ListSessionsTouchedSinceAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "session1" });

        SetupChatCompletionResponse("整合结果");

        var config = new AutoDreamConfig { Enabled = false }; // 即使禁用
        var feature = new DreamFeature(
            _chatCompletionClientMock.Object,
            _sessionScannerMock.Object,
            _taskRegistry,
            config);

        // Act
        var result = await feature.ExecuteAsync(new DreamRequest(Force: true)).ConfigureAwait(true);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsSkipped);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoSessions_ReturnsSkippedResult()
    {
        // Arrange
        _sessionScannerMock.Setup(s => s.ListSessionsTouchedSinceAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        // Act
        var result = await _feature.ExecuteAsync(new DreamRequest()).ConfigureAwait(true);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.IsSkipped);
        Assert.Contains("门控未通过", result.Content);
    }

    [Fact]
    public async Task ExecuteAsync_WithInsufficientSessions_ReturnsSkippedResult()
    {
        // Arrange
        var config = new AutoDreamConfig { Enabled = true, MinSessions = 5 };
        var feature = new DreamFeature(
            _chatCompletionClientMock.Object,
            _sessionScannerMock.Object,
            _taskRegistry,
            config);

        _sessionScannerMock.Setup(s => s.ListSessionsTouchedSinceAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "session1" }); // 只有1个，少于5个

        // Act
        var result = await feature.ExecuteAsync(new DreamRequest()).ConfigureAwait(true);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.IsSkipped);
        Assert.Contains("门控未通过", result.Content);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidSessions_ReturnsSuccessResult()
    {
        // Arrange
        var sessions = new[] { "session1", "session2", "session3" };
        _sessionScannerMock.Setup(s => s.ListSessionsTouchedSinceAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessions);

        SetupChatCompletionResponse("整合完成");

        // Act
        var result = await _feature.ExecuteAsync(new DreamRequest()).ConfigureAwait(true);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsSkipped);
        Assert.Equal("整合完成", result.Content);
        Assert.Equal(sessions.Length, result.SessionsProcessed);
        Assert.NotNull(result.TaskId);
        Assert.True(result.ExecutionTimeMs >= 0);
    }

    [Fact]
    public async Task ExecuteAsync_WithSpecifiedSessions_UsesProvidedSessions()
    {
        // Arrange
        var specifiedSessions = new[] { "custom1", "custom2" };

        SetupChatCompletionResponse("整合完成");

        // Act
        var result = await _feature.ExecuteAsync(new DreamRequest(SessionIds: specifiedSessions)).ConfigureAwait(true);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.SessionsProcessed);

        // 验证没有调用扫描器
        _sessionScannerMock.Verify(s => s.ListSessionsTouchedSinceAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenLLMThrows_ReturnsFailureResult()
    {
        // Arrange
        _sessionScannerMock.Setup(s => s.ListSessionsTouchedSinceAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "session1" });

        _chatCompletionClientMock
            .Setup(c => c.GetCompletionAsync(
                It.IsAny<MessageList>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM服务异常"));

        // Act
        var result = await _feature.ExecuteAsync(new DreamRequest()).ConfigureAwait(true);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.False(result.IsSkipped);
        Assert.Contains("失败", result.Content);
        Assert.Contains("LLM服务异常", result.Content);
    }

    [Fact]
    public async Task ExecuteAsync_RegistersAndCompletesTask()
    {
        // Arrange
        _sessionScannerMock.Setup(s => s.ListSessionsTouchedSinceAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "session1" });

        SetupChatCompletionResponse("整合完成");

        // Act
        var result = await _feature.ExecuteAsync(new DreamRequest()).ConfigureAwait(true);

        // Assert
        Assert.True(result.IsSuccess);

        // 验证任务已注册并完成
        var task = await _taskRegistry.GetTaskStateAsync(result.TaskId!).ConfigureAwait(true);
        Assert.NotNull(task);
        Assert.Equal(DreamTaskStatus.Completed, task.Status);
        Assert.Single(task.Turns);
        Assert.Equal("整合完成", task.Turns[0].Text);
    }

    [Fact]
    public async Task GetTaskStatusAsync_ReturnsCorrectTask()
    {
        // Arrange
        _sessionScannerMock.Setup(s => s.ListSessionsTouchedSinceAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "session1" });

        SetupChatCompletionResponse("整合完成");

        var result = await _feature.ExecuteAsync(new DreamRequest()).ConfigureAwait(true);

        // Act
        var task = await _feature.GetTaskStatusAsync(result.TaskId!).ConfigureAwait(true);

        // Assert
        Assert.NotNull(task);
        Assert.Equal(result.TaskId, task.Id);
    }

    [Fact]
    public async Task GetTaskStatusAsync_NonExistentTask_ReturnsNull()
    {
        // Act
        var task = await _feature.GetTaskStatusAsync("nonexistent").ConfigureAwait(true);

        // Assert
        Assert.Null(task);
    }

    [Fact]
    public async Task ListTasksAsync_ReturnsAllTasks()
    {
        // Arrange
        _sessionScannerMock.Setup(s => s.ListSessionsTouchedSinceAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "session1" });

        SetupChatCompletionResponse("整合完成");

        await _feature.ExecuteAsync(new DreamRequest()).ConfigureAwait(true);
        await _feature.ExecuteAsync(new DreamRequest()).ConfigureAwait(true);

        // Act
        var tasks = await _feature.ListTasksAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(2, tasks.Count);
    }

    [Fact]
    public async Task KillTaskAsync_TerminatesRunningTask()
    {
        // Arrange - 注册一个任务但不完成它
        var taskId = await _taskRegistry.RegisterDreamTaskAsync(
            new DreamTaskRegistrationRequest(
                1,
                DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond,
                new CancellationTokenSource()),
            CancellationToken.None).ConfigureAwait(true);

        // Act
        await _feature.KillTaskAsync(taskId).ConfigureAwait(true);

        // Assert
        var task = await _feature.GetTaskStatusAsync(taskId).ConfigureAwait(true);
        Assert.Equal(DreamTaskStatus.Killed, task!.Status);
    }
}
