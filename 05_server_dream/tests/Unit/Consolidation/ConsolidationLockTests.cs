
namespace Dream.Tests;

/// <summary>
/// 梦境整合锁测试 - 测试使用 RobustFileLockService 的锁机制
/// </summary>
public sealed class ConsolidationLockTests : IDisposable
{
    private readonly string _testDir;
    private readonly InMemoryDreamTaskRegistry _taskRegistry;
    private readonly DefaultSessionScanner _sessionScanner;
    private readonly AutoDreamConfig _config;

    public ConsolidationLockTests()
    {
        _testDir = "/test/dream";

        _config = new AutoDreamConfig
        {
            Enabled = true,
            MinHours = 0,
            MinSessions = 1,
            ProjectDir = _testDir
        };

        _taskRegistry = new InMemoryDreamTaskRegistry();
        _sessionScanner = new DefaultSessionScanner(_config, TestFileSystem.Current);
    }

    public void Dispose()
    {
    }

    [Fact]
    public async Task TaskRegistry_RegisterDreamTask_ShouldReturnTaskId()
    {
        // Act
        var taskId = await _taskRegistry.RegisterDreamTaskAsync(new DreamTaskRegistrationRequest(
            SessionsReviewing: 5,
            PriorMtime: 0,
            AbortController: new CancellationTokenSource())).ConfigureAwait(true);

        // Assert
        Assert.NotNull(taskId);
        Assert.NotEmpty(taskId);

        var task = await _taskRegistry.GetTaskStateAsync(taskId).ConfigureAwait(true);
        Assert.NotNull(task);
        Assert.Equal(5, task.SessionsReviewing);
        Assert.Equal(DreamTaskStatus.Running, task.Status);
    }

    [Fact]
    public async Task TaskRegistry_CompleteDreamTask_ShouldMarkAsCompleted()
    {
        // Arrange
        var taskId = await _taskRegistry.RegisterDreamTaskAsync(new DreamTaskRegistrationRequest(
            SessionsReviewing: 3,
            PriorMtime: 0,
            AbortController: new CancellationTokenSource())).ConfigureAwait(true);

        // Act
        await _taskRegistry.CompleteDreamTaskAsync(taskId).ConfigureAwait(true);

        // Assert
        var task = await _taskRegistry.GetTaskStateAsync(taskId).ConfigureAwait(true);
        Assert.NotNull(task);
        Assert.Equal(DreamTaskStatus.Completed, task.Status);
        Assert.True(task.IsTerminal);
    }

    [Fact]
    public async Task TaskRegistry_AddDreamTurn_ShouldAddTurnAndFiles()
    {
        // Arrange
        var taskId = await _taskRegistry.RegisterDreamTaskAsync(new DreamTaskRegistrationRequest(
            SessionsReviewing: 2,
            PriorMtime: 0,
            AbortController: new CancellationTokenSource())).ConfigureAwait(true);

        var turn = new DreamTurn { Text = "Test turn content", ToolUseCount = 0 };
        var files = new[] { "file1.txt", "file2.txt" };

        // Act
        await _taskRegistry.AddDreamTurnAsync(taskId, turn, files).ConfigureAwait(true);

        // Assert
        var task = await _taskRegistry.GetTaskStateAsync(taskId).ConfigureAwait(true);
        Assert.NotNull(task);
        Assert.Single(task.Turns);
        Assert.Equal(2, task.FilesTouched.Count);
        Assert.Equal(DreamPhase.Updating, task.Phase);
    }

    [Fact]
    public async Task TaskRegistry_GetAllTasks_ShouldReturnAllTasks()
    {
        // Arrange
        await _taskRegistry.RegisterDreamTaskAsync(new DreamTaskRegistrationRequest(
            SessionsReviewing: 1,
            PriorMtime: 0,
            AbortController: new CancellationTokenSource())).ConfigureAwait(true);
        await _taskRegistry.RegisterDreamTaskAsync(new DreamTaskRegistrationRequest(
            SessionsReviewing: 2,
            PriorMtime: 0,
            AbortController: new CancellationTokenSource())).ConfigureAwait(true);

        // Act
        var tasks = await _taskRegistry.GetAllTasksAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(2, tasks.Count);
    }

    [Fact]
    public async Task SessionScanner_ListSessionsTouchedSinceAsync_ShouldReturnEmptyForNewDirectory()
    {
        // Act
        var sessions = await _sessionScanner.ListSessionsTouchedSinceAsync(0).ConfigureAwait(true);

        // Assert
        Assert.Empty(sessions);
    }

    [Fact]
    public void AutoDreamConfig_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var config = new AutoDreamConfig();

        // Assert
        Assert.Equal(24, config.MinHours);
        Assert.Equal(5, config.MinSessions);
        Assert.True(config.Enabled);
        Assert.True(config.AutoMemoryEnabled);
    }

    [Fact]
    public void DreamTurn_Creation_ShouldSetProperties()
    {
        // Act
        var turn = new DreamTurn
        {
            Text = "Test content",
            ToolUseCount = 5
        };

        // Assert
        Assert.Equal("Test content", turn.Text);
        Assert.Equal(5, turn.ToolUseCount);
    }

    [Fact]
    public void DreamTaskState_AddTurn_ShouldLimitMaxTurns()
    {
        // Arrange
        var task = new DreamTaskState
        {
            Id = "test-task",
            Description = "Test",
            StartTime = DateTime.UtcNow,
            SessionsReviewing = 1,
            PriorMtime = 0
        };

        // Act - 添加超过30个回合
        for (int i = 0; i < 35; i++)
        {
            task.AddTurn(new DreamTurn { Text = $"Turn {i}", ToolUseCount = 0 }, Array.Empty<string>());
        }

        // Assert - 应该只保留最新的30个
        Assert.Equal(30, task.Turns.Count);
    }

    [Fact]
    public void DreamTaskState_Complete_ShouldSetStatusAndEndTime()
    {
        // Arrange
        var task = new DreamTaskState
        {
            Id = "test-task",
            Description = "Test",
            StartTime = DateTime.UtcNow,
            SessionsReviewing = 1,
            PriorMtime = 0
        };

        // Act
        task.Complete();

        // Assert
        Assert.Equal(DreamTaskStatus.Completed, task.Status);
        Assert.NotNull(task.EndTime);
        Assert.True(task.IsTerminal);
    }

    [Fact]
    public void DreamTaskState_Kill_ShouldCancelToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var task = new DreamTaskState
        {
            Id = "test-task",
            Description = "Test",
            StartTime = DateTime.UtcNow,
            SessionsReviewing = 1,
            PriorMtime = 0,
            AbortController = cts
        };

        // Act
        task.Kill();

        // Assert
        Assert.Equal(DreamTaskStatus.Killed, task.Status);
        Assert.True(cts.IsCancellationRequested);
    }
}
