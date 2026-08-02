
namespace Core.Tests.Todo;

public sealed class TodoServiceTests
{
    private readonly FakeClockService _clock = new();
    private readonly FakeTaskRuntime _taskRuntime = new();
    private readonly FakeTelemetryService _telemetry = new();

    private TodoService CreateSut(bool withTaskRuntime = true, bool withTelemetry = true)
    {
        return new TodoService(
            _clock,
            withTaskRuntime ? _taskRuntime : null,
            withTelemetry ? _telemetry : null);
    }

    [Fact]
    public async Task WriteTodosAsync_NullInput_ThrowsArgumentNullException()
    {
        var sut = CreateSut();

#pragma warning disable CS8625 // 显式传入 null 以验证参数校验
        var act = async () => await sut.WriteTodosAsync(null!).ConfigureAwait(true);
#pragma warning restore CS8625

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task WriteTodosAsync_CreateNewTodo_WithoutTaskRuntime_ReturnsCreated()
    {
        var sut = CreateSut(withTaskRuntime: false);
        var todos = new List<TodoItemInput>
        {
            new(Content: "Implement feature", Status: TodoStatusConstants.InProgress, ActiveForm: "Implementing feature")
        };

        var result = await sut.WriteTodosAsync(todos).ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.CreatedCount.Should().Be(1);
        result.UpdatedCount.Should().Be(0);
        result.DeletedCount.Should().Be(0);
        result.CurrentTodos.Should().ContainSingle(t => t.Content == "Implement feature");
        _taskRuntime.CreatedInputs.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteTodosAsync_CreateNewTodo_WithTaskRuntime_CreatesTask()
    {
        var sut = CreateSut();
        var todos = new List<TodoItemInput>
        {
            new(Content: "Fix bug", Status: TodoStatusConstants.Pending, Priority: TodoPriorityConstants.High, ActiveForm: "Fixing bug")
        };

        var result = await sut.WriteTodosAsync(todos).ConfigureAwait(true);

        result.CreatedCount.Should().Be(1);
        _taskRuntime.CreatedInputs.Should().ContainSingle()
            .Which.Should().Match<RuntimeTaskInput>(i =>
                i.Description == "Fix bug" &&
                i.Priority == RuntimeTaskPriority.Now &&
                i.IsLightweight &&
                !i.IsDurable);
    }

    [Fact]
    public async Task WriteTodosAsync_UpdateExistingTodo_UpdatesCountsAndTask()
    {
        var sut = CreateSut();
        var id = "todo_001";
        await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Id: id, Content: "Initial", Status: TodoStatusConstants.Pending, ActiveForm: "Initialling")
        }).ConfigureAwait(true);

        var result = await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Id: id, Content: "Updated", Status: TodoStatusConstants.InProgress, ActiveForm: "Updating")
        }).ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.CreatedCount.Should().Be(0);
        result.UpdatedCount.Should().Be(1);
        result.CurrentTodos.Should().ContainSingle(t => t.Content == "Updated" && t.Status == TodoStatusConstants.InProgress);
        _taskRuntime.Updates.Should().ContainSingle(u => u.TaskId == id);
    }

    [Fact]
    public async Task WriteTodosAsync_DeleteExisting_RemovesAndCancelsTask()
    {
        var sut = CreateSut();
        var id = "todo_del";
        await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Id: id, Content: "To delete", Status: TodoStatusConstants.Pending, ActiveForm: "Deleting")
        }).ConfigureAwait(true);

        var result = await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Id: id, Content: "To delete", Status: "deleted", ActiveForm: "Deleting")
        }).ConfigureAwait(true);

        result.DeletedCount.Should().Be(1);
        result.CurrentTodos.Should().BeEmpty();
        _taskRuntime.Updates.Should().ContainSingle(u =>
            u.TaskId == id && u.Update.Status == TaskExecutionStatus.Cancelled);
    }

    [Fact]
    public async Task WriteTodosAsync_DeleteNonExisting_DoesNotIncrementOrCallRuntime()
    {
        var sut = CreateSut();

        var result = await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Id: "missing", Content: "Missing", Status: "deleted", ActiveForm: "Missing")
        }).ConfigureAwait(true);

        result.DeletedCount.Should().Be(0);
        result.CurrentTodos.Should().BeEmpty();
        _taskRuntime.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteTodosAsync_StatusIsCaseInsensitive()
    {
        var sut = CreateSut();

        var result = await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Id: "x", Content: "X", Status: "DeLeTeD", ActiveForm: "Xing")
        }).ConfigureAwait(true);

        result.DeletedCount.Should().Be(0);
    }

    [Fact]
    public async Task WriteTodosAsync_PriorityDefaultMedium_WhenOmitted()
    {
        var sut = CreateSut(withTaskRuntime: false);

        var result = await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Content: "No priority", Status: TodoStatusConstants.Pending, ActiveForm: "Prioritizing")
        }).ConfigureAwait(true);

        result.CurrentTodos.Single().Priority.Should().Be(TodoPriorityConstants.Medium);
    }

    [Fact]
    public async Task WriteTodosAsync_GeneratesId_WhenOmitted()
    {
        var sut = CreateSut(withTaskRuntime: false);

        var result = await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Content: "Auto id", Status: TodoStatusConstants.Pending, ActiveForm: "Auto iding")
        }).ConfigureAwait(true);

        result.CurrentTodos.Single().Id.Should().NotBeNullOrEmpty();
        result.CurrentTodos.Single().Id.Should().StartWith("todo_");
    }

    [Fact]
    public async Task WriteTodosAsync_RecordsTelemetry()
    {
        var sut = CreateSut();
        await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Content: "T1", Status: TodoStatusConstants.Pending, ActiveForm: "T1ing"),
            new(Content: "T2", Status: TodoStatusConstants.InProgress, ActiveForm: "T2ing")
        }).ConfigureAwait(true);

        _telemetry.Counters.Should().Contain(c => c.Name == "todo.operation.count" && c.Tags!["operation"] == "write");
        _telemetry.Histograms.Should().Contain(h => h.Name == "todo.operation.items" && h.Value == 2 && h.Tags!["operation"] == "write");
    }

    [Fact]
    public async Task ListTodosAsync_NoFilter_ReturnsAllOrderedByCreatedAt()
    {
        var sut = CreateSut(withTaskRuntime: false);
        await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Content: "First", Status: TodoStatusConstants.Pending, ActiveForm: "Firsting")
        }).ConfigureAwait(true);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Content: "Second", Status: TodoStatusConstants.InProgress, ActiveForm: "Seconding")
        }).ConfigureAwait(true);

        var result = await sut.ListTodosAsync().ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.Todos.Should().HaveCount(2);
        result.Todos[0].Content.Should().Be("First");
        result.Todos[1].Content.Should().Be("Second");
    }

    [Fact]
    public async Task ListTodosAsync_StatusFilter_IsCaseInsensitive()
    {
        var sut = CreateSut(withTaskRuntime: false);
        await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Content: "A", Status: TodoStatusConstants.Pending, ActiveForm: "Aing"),
            new(Content: "B", Status: TodoStatusConstants.InProgress, ActiveForm: "Bing")
        }).ConfigureAwait(true);

        var result = await sut.ListTodosAsync(status: "In_Progress").ConfigureAwait(true);

        result.Todos.Should().ContainSingle(t => t.Content == "B");
    }

    [Fact]
    public async Task ListTodosAsync_PriorityFilter_IsCaseInsensitive()
    {
        var sut = CreateSut(withTaskRuntime: false);
        await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Content: "High", Status: TodoStatusConstants.Pending, Priority: TodoPriorityConstants.High, ActiveForm: "Highing"),
            new(Content: "Low", Status: TodoStatusConstants.Pending, Priority: TodoPriorityConstants.Low, ActiveForm: "Lowing")
        }).ConfigureAwait(true);

        var result = await sut.ListTodosAsync(priority: "LOW").ConfigureAwait(true);

        result.Todos.Should().ContainSingle(t => t.Content == "Low");
    }

    [Fact]
    public async Task ListTodosAsync_ExcludeCompleted_ByDefault()
    {
        var sut = CreateSut(withTaskRuntime: false);
        await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Content: "Done", Status: TodoStatusConstants.Completed, ActiveForm: "Doing"),
            new(Content: "Pending", Status: TodoStatusConstants.Pending, ActiveForm: "Pendinging")
        }).ConfigureAwait(true);

        var result = await sut.ListTodosAsync().ConfigureAwait(true);

        result.Todos.Should().ContainSingle(t => t.Content == "Pending");
    }

    [Fact]
    public async Task ListTodosAsync_IncludeCompleted_ReturnsCompleted()
    {
        var sut = CreateSut(withTaskRuntime: false);
        await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Content: "Done", Status: TodoStatusConstants.Completed, ActiveForm: "Doing")
        }).ConfigureAwait(true);

        var result = await sut.ListTodosAsync(includeCompleted: true).ConfigureAwait(true);

        result.Todos.Should().ContainSingle(t => t.Status == TodoStatusConstants.Completed);
    }

    [Fact]
    public async Task UpdateTodoAsync_Existing_UpdatesFields()
    {
        var sut = CreateSut(withTaskRuntime: false);
        await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Id: "u1", Content: "Old", Status: TodoStatusConstants.Pending, Priority: TodoPriorityConstants.Low, ActiveForm: "Olding")
        }).ConfigureAwait(true);
        _clock.Advance(TimeSpan.FromMinutes(1));

        var result = await sut.UpdateTodoAsync("u1", content: "New", status: TodoStatusConstants.Completed, priority: TodoPriorityConstants.High).ConfigureAwait(true);

        result.Success.Should().BeTrue();
        var updated = result.Data;
        updated.Should().NotBeNull();
        updated!.Content.Should().Be("New");
        updated.Status.Should().Be(TodoStatusConstants.Completed);
        updated.Priority.Should().Be(TodoPriorityConstants.High);
        updated.UpdatedAt.Should().BeAfter(updated.CreatedAt ?? DateTime.MinValue);
    }

    [Fact]
    public async Task UpdateTodoAsync_NonExisting_ReturnsFail()
    {
        var sut = CreateSut(withTaskRuntime: false);

        var result = await sut.UpdateTodoAsync("missing", content: "X").ConfigureAwait(true);

        result.Success.Should().BeFalse();
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task UpdateTodoAsync_Existing_WithTaskRuntime_SendsUpdate()
    {
        var sut = CreateSut();
        await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Id: "u2", Content: "Old", Status: TodoStatusConstants.Pending, ActiveForm: "Olding")
        }).ConfigureAwait(true);

        await sut.UpdateTodoAsync("u2", content: "New", status: TodoStatusConstants.InProgress, priority: TodoPriorityConstants.Medium).ConfigureAwait(true);

        _taskRuntime.Updates.Should().ContainSingle(u =>
            u.TaskId == "u2" &&
            u.Update.Description == "New" &&
            u.Update.Status == TaskExecutionStatus.Running &&
            u.Update.Priority == RuntimeTaskPriority.Next);
    }

    [Fact]
    public async Task UpdateTodoAsync_NullOrWhitespaceId_ThrowsArgumentException()
    {
        var sut = CreateSut(withTaskRuntime: false);

        var act = async () => await sut.UpdateTodoAsync("  ").ConfigureAwait(true);

        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task ClearTodosAsync_RemovesAllAndRecordsTelemetry()
    {
        var sut = CreateSut(withTaskRuntime: false);
        await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Content: "A", Status: TodoStatusConstants.Pending, ActiveForm: "Aing")
        }).ConfigureAwait(true);

        await sut.ClearTodosAsync().ConfigureAwait(true);

        var list = await sut.ListTodosAsync(includeCompleted: true).ConfigureAwait(true);
        list.Todos.Should().BeEmpty();
        _telemetry.Counters.Should().Contain(c => c.Name == "todo.operation.count" && c.Tags!["operation"] == "clear");
    }
}
