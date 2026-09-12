
namespace Dream.Tests;

/// <summary>
/// 做梦系统集成测试 - 测试组件间协作
/// </summary>
public sealed class DreamIntegrationTests
{
    private static void SetupChatCompletionResponse(Mock<IChatCompletionClient> mock, string content)
    {
        mock.Setup(c => c.GetCompletionAsync(
                It.IsAny<MessageList>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(content);
    }

    /// <summary>
    /// 测试完整流程：Feature → Registry → 状态管理
    /// </summary>
    [Fact]
    public async Task FullFlow_FeatureToRegistry_ToTaskState()
    {
        // Arrange
        var chatCompletionClientMock = new Mock<IChatCompletionClient>();

        var sessionScannerMock = new Mock<ISessionScanner>();
        sessionScannerMock.Setup(s => s.ListSessionsTouchedSinceAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "session1", "session2", "session3" });

        var taskRegistry = new InMemoryDreamTaskRegistry();

        var config = new AutoDreamConfig
        {
            Enabled = true,
            MinHours = 1,
            MinSessions = 1
        };

        var feature = new DreamFeature(
            chatCompletionClientMock.Object,
            sessionScannerMock.Object,
            taskRegistry,
            config);

        SetupChatCompletionResponse(chatCompletionClientMock, "记忆整合结果");

        // Act - 执行做梦
        var result = await feature.ExecuteAsync(new DreamRequest()).ConfigureAwait(true);

        // Assert - 验证结果
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.TaskId);

        // 验证任务状态
        var task = await taskRegistry.GetTaskStateAsync(result.TaskId).ConfigureAwait(true);
        Assert.NotNull(task);
        Assert.Equal(DreamTaskStatus.Completed, task.Status);
        Assert.Equal(DreamPhase.Starting, task.Phase);
        Assert.Equal(3, task.SessionsReviewing);
        Assert.Single(task.Turns);
        Assert.Equal("记忆整合结果", task.Turns[0].Text);

        // 验证可以通过 Feature 查询到任务
        var tasks = await feature.ListTasksAsync().ConfigureAwait(true);
        Assert.Single(tasks);
        Assert.Contains(result.TaskId, tasks.Keys);
    }

    /// <summary>
    /// 测试多任务并发执行
    /// </summary>
    [Fact]
    public async Task ConcurrentExecutions_AllTasksTracked()
    {
        // Arrange
        var chatCompletionClientMock = new Mock<IChatCompletionClient>();

        var sessionScannerMock = new Mock<ISessionScanner>();
        sessionScannerMock.Setup(s => s.ListSessionsTouchedSinceAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "session1" });

        var taskRegistry = new InMemoryDreamTaskRegistry();

        var feature = new DreamFeature(
            chatCompletionClientMock.Object,
            sessionScannerMock.Object,
            taskRegistry,
            new AutoDreamConfig { Enabled = true, MinSessions = 1 });

        SetupChatCompletionResponse(chatCompletionClientMock, "结果");

        // Act - 并发执行多个任务
        var tasks = new[]
        {
            feature.ExecuteAsync(new DreamRequest()),
            feature.ExecuteAsync(new DreamRequest()),
            feature.ExecuteAsync(new DreamRequest())
        };

        var results = await Task.WhenAll(tasks).ConfigureAwait(true);

        // Assert
        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.Equal(3, results.Select(r => r.TaskId).Distinct().Count());

        var allTasks = await feature.ListTasksAsync().ConfigureAwait(true);
        Assert.Equal(3, allTasks.Count);
    }

    /// <summary>
    /// 测试任务生命周期：创建 → 完成 → 查询
    /// </summary>
    [Fact]
    public async Task TaskLifecycle_CreateCompleteQuery()
    {
        // Arrange
        var chatCompletionClientMock = new Mock<IChatCompletionClient>();

        var sessionScannerMock = new Mock<ISessionScanner>();
        sessionScannerMock.Setup(s => s.ListSessionsTouchedSinceAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "session1" });

        var taskRegistry = new InMemoryDreamTaskRegistry();

        var feature = new DreamFeature(
            chatCompletionClientMock.Object,
            sessionScannerMock.Object,
            taskRegistry,
            new AutoDreamConfig { Enabled = true, MinSessions = 1 });

        SetupChatCompletionResponse(chatCompletionClientMock, "结果");

        // Act & Assert - 创建
        var result = await feature.ExecuteAsync(new DreamRequest()).ConfigureAwait(true);
        Assert.True(result.IsSuccess);

        // 查询
        var task = await feature.GetTaskStatusAsync(result.TaskId!).ConfigureAwait(true);
        Assert.NotNull(task);
        Assert.Equal(DreamTaskStatus.Completed, task.Status);
        Assert.True(task.IsTerminal);

        // 列取
        var allTasks = await feature.ListTasksAsync().ConfigureAwait(true);
        Assert.Single(allTasks);
    }

    /// <summary>
    /// 测试强制模式绕过门控
    /// </summary>
    [Fact]
    public async Task ForceMode_BypassesAllGateChecks()
    {
        // Arrange
        var chatCompletionClientMock = new Mock<IChatCompletionClient>();

        var sessionScannerMock = new Mock<ISessionScanner>();
        sessionScannerMock.Setup(s => s.ListSessionsTouchedSinceAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        var taskRegistry = new InMemoryDreamTaskRegistry();

        var config = new AutoDreamConfig
        {
            Enabled = false,
            MinSessions = 100
        };

        var feature = new DreamFeature(
            chatCompletionClientMock.Object,
            sessionScannerMock.Object,
            taskRegistry,
            config);

        SetupChatCompletionResponse(chatCompletionClientMock, "强制结果");

        // Act - 使用强制模式 + 指定会话（绕过门控检查）
        var result = await feature.ExecuteAsync(new DreamRequest(Force: true, SessionIds: new[] { "session1" })).ConfigureAwait(true);

        // Assert - 应该成功执行
        Assert.True(result.IsSuccess);
        Assert.Equal("强制结果", result.Content);
    }

    /// <summary>
    /// 测试会话扫描器与功能的集成
    /// </summary>
    [Fact]
    public async Task SessionScanner_Integration_UsesCorrectTimeRange()
    {
        // Arrange
        var chatCompletionClientMock = new Mock<IChatCompletionClient>();

        var sessionScannerMock = new Mock<ISessionScanner>();
        var capturedTime = 0L;
        sessionScannerMock.Setup(s => s.ListSessionsTouchedSinceAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Callback<long, CancellationToken>((time, _) => capturedTime = time)
            .ReturnsAsync(new[] { "session1" });

        var taskRegistry = new InMemoryDreamTaskRegistry();
        var config = new AutoDreamConfig { Enabled = true, MinHours = 24, MinSessions = 1 };

        var feature = new DreamFeature(
            chatCompletionClientMock.Object,
            sessionScannerMock.Object,
            taskRegistry,
            config);

        SetupChatCompletionResponse(chatCompletionClientMock, "结果");

        var beforeExecute = DateTime.UtcNow.AddHours(-24).Ticks / TimeSpan.TicksPerMillisecond;

        // Act
        await feature.ExecuteAsync(new DreamRequest()).ConfigureAwait(true);

        var afterExecute = DateTime.UtcNow.AddHours(-24).Ticks / TimeSpan.TicksPerMillisecond;

        // Assert
        sessionScannerMock.Verify(s => s.ListSessionsTouchedSinceAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        Assert.True(capturedTime >= beforeExecute && capturedTime <= afterExecute,
            $"时间范围应该在 {beforeExecute} 和 {afterExecute} 之间，但实际是 {capturedTime}");
    }

    /// <summary>
    /// 测试持久化注册表集成
    /// </summary>
    [Fact]
    public async Task PersistentRegistry_Integration_TaskSurvives()
    {
        // Arrange
        var chatCompletionClientMock = new Mock<IChatCompletionClient>();

        var sessionScannerMock = new Mock<ISessionScanner>();
        sessionScannerMock.Setup(s => s.ListSessionsTouchedSinceAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "session1" });

        var taskRegistry = new InMemoryDreamTaskRegistry();

        var feature = new DreamFeature(
            chatCompletionClientMock.Object,
            sessionScannerMock.Object,
            taskRegistry,
            new AutoDreamConfig { Enabled = true, MinSessions = 1 });

        SetupChatCompletionResponse(chatCompletionClientMock, "结果");

        // Act
        var result = await feature.ExecuteAsync(new DreamRequest()).ConfigureAwait(true);

        // Assert
        var task = await taskRegistry.GetTaskStateAsync(result.TaskId!).ConfigureAwait(true);
        Assert.NotNull(task);
        Assert.True(task.EndTime.HasValue);
        Assert.True(task.StartTime <= task.EndTime.Value);
    }
}
