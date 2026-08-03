namespace Core.Context;

public sealed class TaskProgressTrackerTests
{
    [Fact]
    public async Task GetCompletedTodoCountAsync_ReturnsCompletedCount()
    {
        var todoService = CreateTodoService(completedCount: 3, totalCount: 5);
        var tracker = new TaskProgressTracker(todoService.Object);

        var count = await tracker.GetCompletedTodoCountAsync().ConfigureAwait(true);

        count.Should().Be(3);
    }

    [Fact]
    public async Task GetCompletedTodoCountAsync_ReturnsZeroOnFailure()
    {
        var todoService = new Mock<ITodoService>();
        todoService.Setup(s => s.ListTodosAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoListResult(false, new List<TodoItem>()));

        var tracker = new TaskProgressTracker(todoService.Object);

        var count = await tracker.GetCompletedTodoCountAsync().ConfigureAwait(true);

        count.Should().Be(0);
    }

    [Fact]
    public async Task GetCompletedTodoCountAsync_ReturnsZeroOnException()
    {
        var todoService = new Mock<ITodoService>();
        todoService.Setup(s => s.ListTodosAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("test"));

        var tracker = new TaskProgressTracker(todoService.Object);

        var count = await tracker.GetCompletedTodoCountAsync().ConfigureAwait(true);

        count.Should().Be(0);
    }

    [Fact]
    public async Task SnapshotCurrentProgressAsync_RecordsCompletedCount()
    {
        var todoService = CreateTodoService(completedCount: 5, totalCount: 8);
        var tracker = new TaskProgressTracker(todoService.Object);

        await tracker.SnapshotCurrentProgressAsync().ConfigureAwait(true);

        var hasProgressed = await tracker.HasProgressedSinceLastSnapshotAsync().ConfigureAwait(true);
        hasProgressed.Should().BeFalse();
    }

    [Fact]
    public async Task HasProgressedSinceLastSnapshotAsync_ReturnsTrueWhenCountIncreased()
    {
        var completedCount = 3;
        var todoService = new Mock<ITodoService>();
        todoService.Setup(s => s.ListTodosAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                var items = CreateTodoItems(completedCount, 5);
                completedCount++;
                return Task.FromResult(new TodoListResult(true, items));
            });

        var tracker = new TaskProgressTracker(todoService.Object);

        await tracker.SnapshotCurrentProgressAsync().ConfigureAwait(true);

        var hasProgressed = await tracker.HasProgressedSinceLastSnapshotAsync().ConfigureAwait(true);
        hasProgressed.Should().BeTrue();
    }

    [Fact]
    public async Task HasProgressedSinceLastSnapshotAsync_ReturnsFalseWhenCountUnchanged()
    {
        var todoService = CreateTodoService(completedCount: 3, totalCount: 5);
        var tracker = new TaskProgressTracker(todoService.Object);

        await tracker.SnapshotCurrentProgressAsync().ConfigureAwait(true);

        var hasProgressed = await tracker.HasProgressedSinceLastSnapshotAsync().ConfigureAwait(true);
        hasProgressed.Should().BeFalse();
    }

    [Fact]
    public async Task HasProgressedSinceLastSnapshotAsync_ReturnsFalseBeforeSnapshot()
    {
        var todoService = CreateTodoService(completedCount: 3, totalCount: 5);
        var tracker = new TaskProgressTracker(todoService.Object);

        var hasProgressed = await tracker.HasProgressedSinceLastSnapshotAsync().ConfigureAwait(true);

        hasProgressed.Should().BeFalse();
    }

    [Fact]
    public async Task FullProgressTrackingWorkflow_SnapshotThenProgressThenCheck()
    {
        var callCount = 0;
        var todoService = new Mock<ITodoService>();
        todoService.Setup(s => s.ListTodosAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                var completed = callCount <= 2 ? 2 : 5;
                var items = CreateTodoItems(completed, 8);
                return Task.FromResult(new TodoListResult(true, items));
            });

        var tracker = new TaskProgressTracker(todoService.Object);

        var initialCount = await tracker.GetCompletedTodoCountAsync().ConfigureAwait(true);
        initialCount.Should().Be(2);

        await tracker.SnapshotCurrentProgressAsync().ConfigureAwait(true);

        var hasProgressed = await tracker.HasProgressedSinceLastSnapshotAsync().ConfigureAwait(true);
        hasProgressed.Should().BeTrue();
    }

    private static Mock<ITodoService> CreateTodoService(int completedCount, int totalCount)
    {
        var items = CreateTodoItems(completedCount, totalCount);
        var mock = new Mock<ITodoService>();
        mock.Setup(s => s.ListTodosAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoListResult(true, items));
        return mock;
    }

    private static List<TodoItem> CreateTodoItems(int completedCount, int totalCount)
    {
        var items = new List<TodoItem>();
        for (var i = 0; i < totalCount; i++)
        {
            var isCompleted = i < completedCount;
            items.Add(new TodoItem(
                Id: $"todo-{i}",
                Content: $"任务{i}",
                Status: isCompleted ? "completed" : "in_progress",
                Priority: "medium",
                CreatedAt: DateTime.UtcNow));
        }

        return items;
    }
}
