namespace Dream.Tests.Persistence;

/// <summary>
/// 持久化做梦任务注册表单元测试
/// </summary>
public sealed class PersistentDreamTaskRegistryTests
{
    [Fact]
    public async Task RegisterDreamTaskAsync_SavesToPersistence()
    {
        var persistence = new Mock<IDreamTaskPersistence>();
        var registry = new PersistentDreamTaskRegistry(persistence.Object);

        var taskId = await registry.RegisterDreamTaskAsync(CreateRequest()).ConfigureAwait(true);

        Assert.NotNull(taskId);
        persistence.Verify(p => p.SaveAsync(It.Is<DreamTaskState>(t => t.Id == taskId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddDreamTurnAsync_SavesUpdatedTask()
    {
        var persistence = new Mock<IDreamTaskPersistence>();
        var registry = new PersistentDreamTaskRegistry(persistence.Object);
        var taskId = await registry.RegisterDreamTaskAsync(CreateRequest()).ConfigureAwait(true);

        await registry.AddDreamTurnAsync(taskId, new DreamTurn { Text = "turn", ToolUseCount = 0 }, Array.Empty<string>()).ConfigureAwait(true);

        persistence.Verify(p => p.SaveAsync(It.Is<DreamTaskState>(t => t.Turns.Count == 1), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CompleteDreamTaskAsync_SavesAndRemovesFromActive()
    {
        var persistence = new Mock<IDreamTaskPersistence>();
        var registry = new PersistentDreamTaskRegistry(persistence.Object);
        var taskId = await registry.RegisterDreamTaskAsync(CreateRequest()).ConfigureAwait(true);

        await registry.CompleteDreamTaskAsync(taskId).ConfigureAwait(true);

        // Register + Complete 各调用一次 SaveAsync，共2次
        persistence.Verify(p => p.SaveAsync(It.IsAny<DreamTaskState>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        var active = await registry.GetTaskStateAsync(taskId).ConfigureAwait(true);
        Assert.Null(active);
    }

    [Fact]
    public async Task FailDreamTaskAsync_SavesAndRemovesFromActive()
    {
        var persistence = new Mock<IDreamTaskPersistence>();
        var registry = new PersistentDreamTaskRegistry(persistence.Object);
        var taskId = await registry.RegisterDreamTaskAsync(CreateRequest()).ConfigureAwait(true);

        await registry.FailDreamTaskAsync(taskId).ConfigureAwait(true);

        // Register + Fail 各调用一次 SaveAsync，共2次
        persistence.Verify(p => p.SaveAsync(It.IsAny<DreamTaskState>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Null(await registry.GetTaskStateAsync(taskId).ConfigureAwait(true));
    }

    [Fact]
    public async Task KillDreamTaskAsync_SavesAndRemovesFromActive()
    {
        var persistence = new Mock<IDreamTaskPersistence>();
        var registry = new PersistentDreamTaskRegistry(persistence.Object);
        var taskId = await registry.RegisterDreamTaskAsync(CreateRequest()).ConfigureAwait(true);

        await registry.KillDreamTaskAsync(taskId).ConfigureAwait(true);

        // Register + Kill 各调用一次 SaveAsync，共2次
        persistence.Verify(p => p.SaveAsync(It.IsAny<DreamTaskState>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Null(await registry.GetTaskStateAsync(taskId).ConfigureAwait(true));
    }

    [Fact]
    public async Task KillDreamTaskAsync_NonExistent_DoesNotThrow()
    {
        var persistence = new Mock<IDreamTaskPersistence>();
        var registry = new PersistentDreamTaskRegistry(persistence.Object);

        var exception = await Record.ExceptionAsync(() => registry.KillDreamTaskAsync("missing")).ConfigureAwait(true);

        Assert.Null(exception);
    }

    [Fact]
    public async Task KillDreamTaskAsync_AlreadyTerminal_DoesNotThrow()
    {
        var persistence = new Mock<IDreamTaskPersistence>();
        var registry = new PersistentDreamTaskRegistry(persistence.Object);
        var taskId = await registry.RegisterDreamTaskAsync(CreateRequest()).ConfigureAwait(true);
        await registry.CompleteDreamTaskAsync(taskId).ConfigureAwait(true);

        var exception = await Record.ExceptionAsync(() => registry.KillDreamTaskAsync(taskId)).ConfigureAwait(true);

        Assert.Null(exception);
    }

    [Fact]
    public async Task GetTaskStateAsync_FromPersistence_WhenNotActive()
    {
        var persistence = new Mock<IDreamTaskPersistence>();
        persistence.Setup(p => p.LoadAsync("d12345678", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DreamTaskState
            {
                Id = "d12345678",
                Description = "dreaming",
                StartTime = DateTime.UtcNow,
                SessionsReviewing = 1,
                PriorMtime = 0
            });
        var registry = new PersistentDreamTaskRegistry(persistence.Object);

        var task = await registry.GetTaskStateAsync("d12345678").ConfigureAwait(true);

        Assert.NotNull(task);
        Assert.Equal("d12345678", task.Id);
    }

    [Fact]
    public async Task GetAllTasksAsync_MergesActiveAndPersisted()
    {
        var persistence = new Mock<IDreamTaskPersistence>();
        persistence.Setup(p => p.LoadAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DreamTaskState>
            {
                new()
                {
                    Id = "persisted",
                    Description = "dreaming",
                    StartTime = DateTime.UtcNow,
                    SessionsReviewing = 1,
                    PriorMtime = 0
                }
            });
        var registry = new PersistentDreamTaskRegistry(persistence.Object);
        var activeId = await registry.RegisterDreamTaskAsync(CreateRequest()).ConfigureAwait(true);

        var all = await registry.GetAllTasksAsync().ConfigureAwait(true);

        Assert.Equal(2, all.Count);
        Assert.Contains(activeId, all.Keys);
        Assert.Contains("persisted", all.Keys);
    }

    [Fact]
    public async Task LoadActiveTasksAsync_LoadsNonTerminalTasks()
    {
        var persistence = new Mock<IDreamTaskPersistence>();
        persistence.Setup(p => p.LoadAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DreamTaskState>
            {
                new() { Id = "active", Description = "dreaming", StartTime = DateTime.UtcNow, SessionsReviewing = 1, PriorMtime = 0, Status = DreamTaskStatus.Running },
                new() { Id = "completed", Description = "dreaming", StartTime = DateTime.UtcNow, SessionsReviewing = 1, PriorMtime = 0, Status = DreamTaskStatus.Completed }
            });
        var registry = new PersistentDreamTaskRegistry(persistence.Object);

        await registry.LoadActiveTasksAsync().ConfigureAwait(true);

        Assert.NotNull(await registry.GetTaskStateAsync("active").ConfigureAwait(true));
        Assert.Null(await registry.GetTaskStateAsync("completed").ConfigureAwait(true));
    }

    [Fact]
    public async Task CleanupAsync_DelegatesToPersistence()
    {
        var persistence = new Mock<IDreamTaskPersistence>();
        var registry = new PersistentDreamTaskRegistry(persistence.Object);

        await registry.CleanupAsync(5).ConfigureAwait(true);

        persistence.Verify(p => p.CleanupCompletedAsync(5, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static DreamTaskRegistrationRequest CreateRequest() =>
        new(SessionsReviewing: 1, PriorMtime: 0, AbortController: new CancellationTokenSource());
}