
namespace Dream.Tests;

/// <summary>
/// 任务持久化测试 - 使用内存文件系统实现高速测试
/// </summary>
public sealed class DreamTaskPersistenceTests : IDisposable
{
    private readonly InMemoryFileOperationService _fileOperationService;
    private readonly JsonFileDreamTaskPersistence _persistence;

    public DreamTaskPersistenceTests()
    {
        _fileOperationService = new InMemoryFileOperationService();

        _persistence = new JsonFileDreamTaskPersistence(
            new AutoDreamConfig { AutoMemoryPath = "dream" },
            _fileOperationService);
    }

    public void Dispose()
    {
        _fileOperationService.Dispose();
    }

    [Fact]
    public async Task SaveAsync_ShouldCreateFile()
    {
        // Arrange
        var task = CreateTestTask();

        // Act
        await _persistence.SaveAsync(task).ConfigureAwait(true);

        // Assert
        var filePath = $"dream{Path.DirectorySeparatorChar}tasks{Path.DirectorySeparatorChar}{task.Id}.json";
        Assert.True(_fileOperationService.FileExists(filePath));
    }

    [Fact]
    public async Task LoadAsync_ExistingTask_ShouldReturnTask()
    {
        // Arrange
        var task = CreateTestTask();
        await _persistence.SaveAsync(task).ConfigureAwait(true);

        // Act
        var loaded = await _persistence.LoadAsync(task.Id).ConfigureAwait(true);

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal(task.Id, loaded.Id);
        Assert.Equal(task.Description, loaded.Description);
        Assert.Equal(task.SessionsReviewing, loaded.SessionsReviewing);
        Assert.Equal(task.Status, loaded.Status);
        Assert.Equal(task.Phase, loaded.Phase);
    }

    [Fact]
    public async Task LoadAsync_NonExistentTask_ShouldReturnNull()
    {
        // Act
        var loaded = await _persistence.LoadAsync("nonexistent").ConfigureAwait(true);

        // Assert
        Assert.Null(loaded);
    }

    [Fact]
    public async Task LoadAllAsync_ShouldReturnAllTasks()
    {
        // Arrange
        var task1 = CreateTestTask("task1");
        var task2 = CreateTestTask("task2");
        await _persistence.SaveAsync(task1).ConfigureAwait(true);
        await _persistence.SaveAsync(task2).ConfigureAwait(true);

        // Act
        var all = await _persistence.LoadAllAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(2, all.Count);
        Assert.Contains(all, t => t.Id == task1.Id);
        Assert.Contains(all, t => t.Id == task2.Id);
    }

    [Fact]
    public async Task LoadAllAsync_ShouldSortByStartTimeDescending()
    {
        // Arrange
        var task1 = CreateTestTask("task1", DateTime.UtcNow.AddMilliseconds(-10));
        var task2 = CreateTestTask("task2");

        await _persistence.SaveAsync(task1).ConfigureAwait(true);
        await _persistence.SaveAsync(task2).ConfigureAwait(true);

        // Act
        var all = await _persistence.LoadAllAsync().ConfigureAwait(true);

        // Assert
        Assert.True(all[0].StartTime >= all[1].StartTime);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveFile()
    {
        // Arrange
        var task = CreateTestTask();
        await _persistence.SaveAsync(task).ConfigureAwait(true);
        var filePath = $"dream{Path.DirectorySeparatorChar}tasks{Path.DirectorySeparatorChar}{task.Id}.json";
        Assert.True(_fileOperationService.FileExists(filePath));

        // Act
        await _persistence.DeleteAsync(task.Id).ConfigureAwait(true);

        // Assert
        Assert.False(_fileOperationService.FileExists(filePath));
    }

    [Fact]
    public async Task CleanupCompletedAsync_ShouldRemoveOldCompletedTasks()
    {
        // Arrange
        for (var i = 0; i < 5; i++)
        {
            var task = CreateTestTask($"completed{i}", DateTime.UtcNow.AddMilliseconds(-10 * (5 - i)));
            task.Complete();
            await _persistence.SaveAsync(task).ConfigureAwait(true);
        }

        // Act - 保留2个
        await _persistence.CleanupCompletedAsync(2).ConfigureAwait(true);

        // Assert
        var remaining = await _persistence.LoadAllAsync().ConfigureAwait(true);
        Assert.Equal(2, remaining.Count);
        // 应该保留最新的2个
        Assert.Contains(remaining, t => t.Id == "completed3");
        Assert.Contains(remaining, t => t.Id == "completed4");
    }

    [Fact]
    public async Task CleanupCompletedAsync_ShouldNotRemoveRunningTasks()
    {
        // Arrange
        var runningTask = CreateTestTask("running");
        await _persistence.SaveAsync(runningTask).ConfigureAwait(true);

        var completedTask = CreateTestTask("completed");
        completedTask.Complete();
        await _persistence.SaveAsync(completedTask).ConfigureAwait(true);

        // Act
        await _persistence.CleanupCompletedAsync(0).ConfigureAwait(true);

        // Assert
        var remaining = await _persistence.LoadAllAsync().ConfigureAwait(true);
        Assert.Single(remaining);
        Assert.Equal("running", remaining[0].Id);
    }

    [Fact]
    public async Task SaveAsync_ShouldPersistAllFields()
    {
        // Arrange
        var task = new DreamTaskState
        {
            Id = "test123",
            Description = "test description",
            StartTime = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2024, 1, 15, 10, 35, 0, DateTimeKind.Utc),
            Status = DreamTaskStatus.Completed,
            Phase = DreamPhase.Updating,
            Notified = true,
            SessionsReviewing = 10,
            PriorMtime = 12345678
        };
        task.FilesTouched.AddRange(new[] { "file1.md", "file2.md" });
        task.Turns.Add(new DreamTurn { Text = "turn1", ToolUseCount = 1 });
        task.Turns.Add(new DreamTurn { Text = "turn2", ToolUseCount = 2 });

        // Act
        await _persistence.SaveAsync(task).ConfigureAwait(true);
        var loaded = await _persistence.LoadAsync(task.Id).ConfigureAwait(true);

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal(task.Id, loaded.Id);
        Assert.Equal(task.Description, loaded.Description);
        Assert.Equal(task.StartTime, loaded.StartTime);
        Assert.Equal(task.EndTime, loaded.EndTime);
        Assert.Equal(task.Status, loaded.Status);
        Assert.Equal(task.Phase, loaded.Phase);
        Assert.Equal(task.Notified, loaded.Notified);
        Assert.Equal(task.SessionsReviewing, loaded.SessionsReviewing);
        Assert.Equal(task.PriorMtime, loaded.PriorMtime);
        Assert.Equal(2, loaded.FilesTouched.Count);
    }

    private static DreamTaskState CreateTestTask(string? id = null, DateTime? startTime = null) => new()
    {
        Id = id ?? TaskIdGenerator.GenerateTaskId(TaskType.Dream),
        Description = "test",
        StartTime = startTime ?? DateTime.UtcNow,
        SessionsReviewing = 5,
        PriorMtime = 0
    };
}
