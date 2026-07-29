
namespace Dream.Tests;

/// <summary>
/// 做梦任务注册表测试
/// </summary>
public sealed class DreamTaskRegistryTests
{
    private readonly InMemoryDreamTaskRegistry _registry;

    public DreamTaskRegistryTests()
    {
        _registry = new InMemoryDreamTaskRegistry();
    }

    [Fact]
    public async Task RegisterDreamTask_ShouldReturnValidTaskId()
    {
        // Arrange
        var request = CreateRegistrationRequest();

        // Act
        var taskId = await _registry.RegisterDreamTaskAsync(request).ConfigureAwait(true);

        // Assert
        Assert.NotNull(taskId);
        Assert.StartsWith("d", taskId);
        Assert.Equal(9, taskId.Length);
    }

    [Fact]
    public async Task RegisterDreamTask_ShouldStoreTask()
    {
        // Arrange
        var request = CreateRegistrationRequest();

        // Act
        var taskId = await _registry.RegisterDreamTaskAsync(request).ConfigureAwait(true);
        var task = await _registry.GetTaskStateAsync(taskId).ConfigureAwait(true);

        // Assert
        Assert.NotNull(task);
        Assert.Equal(taskId, task.Id);
        Assert.Equal(DreamTaskStatus.Running, task.Status);
        Assert.Equal(DreamPhase.Starting, task.Phase);
    }

    [Fact]
    public async Task GetTaskState_NonExistentTask_ShouldReturnNull()
    {
        // Act
        var task = await _registry.GetTaskStateAsync("nonexistent").ConfigureAwait(true);

        // Assert
        Assert.Null(task);
    }

    [Fact]
    public async Task AddDreamTurn_ShouldUpdateTask()
    {
        // Arrange
        var taskId = await _registry.RegisterDreamTaskAsync(CreateRegistrationRequest()).ConfigureAwait(true);
        var turn = new DreamTurn { Text = "test turn", ToolUseCount = 1 };

        // Act
        await _registry.AddDreamTurnAsync(taskId, turn, new[] { "file1.md" }).ConfigureAwait(true);
        var task = await _registry.GetTaskStateAsync(taskId).ConfigureAwait(true);

        // Assert
        Assert.NotNull(task);
        Assert.Single(task.Turns);
        Assert.Single(task.FilesTouched);
        Assert.Equal(DreamPhase.Updating, task.Phase);
    }

    [Fact]
    public async Task AddDreamTurn_NonExistentTask_ShouldNotThrow()
    {
        // Arrange
        var turn = new DreamTurn { Text = "test", ToolUseCount = 0 };

        // Act & Assert
        var exception = await Record.ExceptionAsync(() =>
            _registry.AddDreamTurnAsync("nonexistent", turn, Array.Empty<string>())).ConfigureAwait(true);
        Assert.Null(exception);
    }

    [Fact]
    public async Task CompleteDreamTask_ShouldUpdateStatus()
    {
        // Arrange
        var taskId = await _registry.RegisterDreamTaskAsync(CreateRegistrationRequest()).ConfigureAwait(true);

        // Act
        await _registry.CompleteDreamTaskAsync(taskId).ConfigureAwait(true);
        var task = await _registry.GetTaskStateAsync(taskId).ConfigureAwait(true);

        // Assert
        Assert.NotNull(task);
        Assert.Equal(DreamTaskStatus.Completed, task.Status);
        Assert.True(task.Notified);
        Assert.NotNull(task.EndTime);
    }

    [Fact]
    public async Task FailDreamTask_ShouldUpdateStatus()
    {
        // Arrange
        var taskId = await _registry.RegisterDreamTaskAsync(CreateRegistrationRequest()).ConfigureAwait(true);

        // Act
        await _registry.FailDreamTaskAsync(taskId).ConfigureAwait(true);
        var task = await _registry.GetTaskStateAsync(taskId).ConfigureAwait(true);

        // Assert
        Assert.NotNull(task);
        Assert.Equal(DreamTaskStatus.Failed, task.Status);
        Assert.True(task.Notified);
    }

    [Fact]
    public async Task KillDreamTask_ShouldCancelAndUpdateStatus()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var request = new DreamTaskRegistrationRequest(
            SessionsReviewing: 5,
            PriorMtime: 0,
            AbortController: cts);
        var taskId = await _registry.RegisterDreamTaskAsync(request).ConfigureAwait(true);

        // Act
        await _registry.KillDreamTaskAsync(taskId).ConfigureAwait(true);
        var task = await _registry.GetTaskStateAsync(taskId).ConfigureAwait(true);

        // Assert
        Assert.NotNull(task);
        Assert.Equal(DreamTaskStatus.Killed, task.Status);
        Assert.True(cts.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task KillDreamTask_AlreadyTerminal_ShouldNotThrow()
    {
        // Arrange
        var taskId = await _registry.RegisterDreamTaskAsync(CreateRegistrationRequest()).ConfigureAwait(true);
        await _registry.CompleteDreamTaskAsync(taskId).ConfigureAwait(true);

        // Act & Assert
        var exception = await Record.ExceptionAsync(() =>
            _registry.KillDreamTaskAsync(taskId)).ConfigureAwait(true);
        Assert.Null(exception);
    }

    [Fact]
    public async Task GetAllTasks_ShouldReturnAllRegisteredTasks()
    {
        // Arrange
        var request1 = CreateRegistrationRequest();
        var request2 = CreateRegistrationRequest();

        // Act
        var taskId1 = await _registry.RegisterDreamTaskAsync(request1).ConfigureAwait(true);
        var taskId2 = await _registry.RegisterDreamTaskAsync(request2).ConfigureAwait(true);
        var allTasks = await _registry.GetAllTasksAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(2, allTasks.Count);
        Assert.Contains(taskId1, allTasks.Keys);
        Assert.Contains(taskId2, allTasks.Keys);
    }

    [Fact]
    public async Task GetAllTasks_ShouldReturnSnapshot()
    {
        // Arrange
        await _registry.RegisterDreamTaskAsync(CreateRegistrationRequest()).ConfigureAwait(true);
        var allTasks = await _registry.GetAllTasksAsync().ConfigureAwait(true);

        // Act
        await _registry.RegisterDreamTaskAsync(CreateRegistrationRequest()).ConfigureAwait(true);

        // Assert
        Assert.Single(allTasks); // 快照不应改变
    }

    private static DreamTaskRegistrationRequest CreateRegistrationRequest() =>
        new(
            SessionsReviewing: 5,
            PriorMtime: 0,
            AbortController: new CancellationTokenSource());
}
