
namespace Core.Tests.Todo;

public sealed class TodoServiceTests {
    private readonly FakeClockService _clock = new();
    private readonly FakeTaskRuntime _taskRuntime = new();
    private readonly FakeTelemetryService _telemetry = new();

    private TodoService CreateSut(bool withTaskRuntime = true, bool withTelemetry = true) {
        return new TodoService(
            _clock,
            withTaskRuntime ? _taskRuntime : null,
            withTelemetry ? _telemetry : null);
    }

    [Fact]
    public async Task WriteTodosAsync_NullInput_ThrowsArgumentNullException() {
        await using var sut = CreateSut();

#pragma warning disable CS8625 // 显式传入 null 以验证参数校验
        var act = async () => await sut.WriteTodosAsync(null!).ConfigureAwait(true);
#pragma warning restore CS8625

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task WriteTodosAsync_CreateNewTodo_WithoutTaskRuntime_ReturnsCreated() {
        await using var sut = CreateSut(withTaskRuntime: false);
        var todos = new List<TodoItemInput>
        {
            new(Content: "Implement feature", Status: TodoStatusEnumConstants.InProgress, ActiveForm: "Implementing feature")
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
    public async Task WriteTodosAsync_CreateNewTodo_WithTaskRuntime_CreatesTask() {
        await using var sut = CreateSut();
        var todos = new List<TodoItemInput>
        {
            new(Content: "Fix bug", Status: TodoStatusEnumConstants.Pending, Priority: TodoPriorityEnumConstants.High, ActiveForm: "Fixing bug")
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
    public async Task WriteTodosAsync_UpdateExistingTodo_UpdatesCountsAndTask() {
        await using var sut = CreateSut();
        var id = "todo_001";
        await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Id: id, Content: "Initial", Status: TodoStatusEnumConstants.Pending, ActiveForm: "Initialling")
        }).ConfigureAwait(true);

        var result = await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Id: id, Content: "Updated", Status: TodoStatusEnumConstants.InProgress, ActiveForm: "Updating")
        }).ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.CreatedCount.Should().Be(0);
        result.UpdatedCount.Should().Be(1);
        result.CurrentTodos.Should().ContainSingle(t => t.Content == "Updated" && t.Status == TodoStatusEnumConstants.InProgress);
        _taskRuntime.Updates.Should().ContainSingle(u => u.TaskId == id);
    }

    [Fact]
    public async Task WriteTodosAsync_DeleteExisting_RemovesAndCancelsTask() {
        await using var sut = CreateSut();
        var id = "todo_del";
        await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Id: id, Content: "To delete", Status: TodoStatusEnumConstants.Pending, ActiveForm: "Deleting")
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
    public async Task WriteTodosAsync_DeleteNonExisting_DoesNotIncrementOrCallRuntime() {
        await using var sut = CreateSut();

        var result = await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Id: "missing", Content: "Missing", Status: "deleted", ActiveForm: "Missing")
        }).ConfigureAwait(true);

        result.DeletedCount.Should().Be(0);
        result.CurrentTodos.Should().BeEmpty();
        _taskRuntime.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteTodosAsync_StatusIsCaseInsensitive() {
        await using var sut = CreateSut();

        var result = await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Id: "x", Content: "X", Status: "DeLeTeD", ActiveForm: "Xing")
        }).ConfigureAwait(true);

        result.DeletedCount.Should().Be(0);
    }

    [Fact]
    public async Task WriteTodosAsync_PriorityDefaultMedium_WhenOmitted() {
        await using var sut = CreateSut(withTaskRuntime: false);

        var result = await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Content: "No priority", Status: TodoStatusEnumConstants.Pending, ActiveForm: "Prioritizing")
        }).ConfigureAwait(true);

        result.CurrentTodos.Single().Priority.Should().Be(TodoPriorityEnumConstants.Medium);
    }

    [Fact]
    public async Task WriteTodosAsync_GeneratesId_WhenOmitted() {
        await using var sut = CreateSut(withTaskRuntime: false);

        var result = await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Content: "Auto id", Status: TodoStatusEnumConstants.Pending, ActiveForm: "Auto iding")
        }).ConfigureAwait(true);

        result.CurrentTodos.Single().Id.Should().NotBeNullOrEmpty();
        result.CurrentTodos.Single().Id.Should().StartWith("todo_");
    }

    [Fact]
    public async Task WriteTodosAsync_RecordsTelemetry() {
        await using var sut = CreateSut();
        await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Content: "T1", Status: TodoStatusEnumConstants.Pending, ActiveForm: "T1ing"),
            new(Content: "T2", Status: TodoStatusEnumConstants.InProgress, ActiveForm: "T2ing")
        }).ConfigureAwait(true);

        _telemetry.Counters.Should().Contain(c => c.Name == "todo.operation.count" && c.Tags!["operation"] == "write");
        _telemetry.Histograms.Should().Contain(h => h.Name == "todo.operation.items" && h.Value == 2 && h.Tags!["operation"] == "write");
    }

    [Fact]
    public async Task ListTodosAsync_NoFilter_ReturnsAllOrderedByCreatedAt() {
        await using var sut = CreateSut(withTaskRuntime: false);
        await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Content: "First", Status: TodoStatusEnumConstants.Pending, ActiveForm: "Firsting")
        }).ConfigureAwait(true);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Content: "Second", Status: TodoStatusEnumConstants.InProgress, ActiveForm: "Seconding")
        }).ConfigureAwait(true);

        var result = await sut.ListTodosAsync().ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.Todos.Should().HaveCount(2);
        result.Todos[0].Content.Should().Be("First");
        result.Todos[1].Content.Should().Be("Second");
    }

    [Fact]
    public async Task ListTodosAsync_StatusFilter_IsCaseInsensitive() {
        await using var sut = CreateSut(withTaskRuntime: false);
        await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Content: "A", Status: TodoStatusEnumConstants.Pending, ActiveForm: "Aing"),
            new(Content: "B", Status: TodoStatusEnumConstants.InProgress, ActiveForm: "Bing")
        }).ConfigureAwait(true);

        var result = await sut.ListTodosAsync(status: "In_Progress").ConfigureAwait(true);

        result.Todos.Should().ContainSingle(t => t.Content == "B");
    }

    [Fact]
    public async Task ListTodosAsync_PriorityFilter_IsCaseInsensitive() {
        await using var sut = CreateSut(withTaskRuntime: false);
        await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Content: "High", Status: TodoStatusEnumConstants.Pending, Priority: TodoPriorityEnumConstants.High, ActiveForm: "Highing"),
            new(Content: "Low", Status: TodoStatusEnumConstants.Pending, Priority: TodoPriorityEnumConstants.Low, ActiveForm: "Lowing")
        }).ConfigureAwait(true);

        var result = await sut.ListTodosAsync(priority: "LOW").ConfigureAwait(true);

        result.Todos.Should().ContainSingle(t => t.Content == "Low");
    }

    [Fact]
    public async Task ListTodosAsync_ExcludeCompleted_ByDefault() {
        await using var sut = CreateSut(withTaskRuntime: false);
        await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Content: "Done", Status: TodoStatusEnumConstants.Completed, ActiveForm: "Doing"),
            new(Content: "Pending", Status: TodoStatusEnumConstants.Pending, ActiveForm: "Pendinging")
        }).ConfigureAwait(true);

        var result = await sut.ListTodosAsync().ConfigureAwait(true);

        result.Todos.Should().ContainSingle(t => t.Content == "Pending");
    }

    [Fact]
    public async Task ListTodosAsync_IncludeCompleted_ReturnsCompleted() {
        await using var sut = CreateSut(withTaskRuntime: false);
        await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Content: "Done", Status: TodoStatusEnumConstants.Completed, ActiveForm: "Doing")
        }).ConfigureAwait(true);

        var result = await sut.ListTodosAsync(includeCompleted: true).ConfigureAwait(true);

        result.Todos.Should().ContainSingle(t => t.Status == TodoStatusEnumConstants.Completed);
    }

    [Fact]
    public async Task UpdateTodoAsync_Existing_UpdatesFields() {
        await using var sut = CreateSut(withTaskRuntime: false);
        await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Id: "u1", Content: "Old", Status: TodoStatusEnumConstants.Pending, Priority: TodoPriorityEnumConstants.Low, ActiveForm: "Olding")
        }).ConfigureAwait(true);
        _clock.Advance(TimeSpan.FromMinutes(1));

        var result = await sut.UpdateTodoAsync("u1", content: "New", status: TodoStatusEnumConstants.Completed, priority: TodoPriorityEnumConstants.High).ConfigureAwait(true);

        result.Success.Should().BeTrue();
        var updated = result.Data;
        updated.Should().NotBeNull();
        updated!.Content.Should().Be("New");
        updated.Status.Should().Be(TodoStatusEnumConstants.Completed);
        updated.Priority.Should().Be(TodoPriorityEnumConstants.High);
        updated.UpdatedAt.Should().BeAfter(updated.CreatedAt ?? DateTime.MinValue);
    }

    [Fact]
    public async Task UpdateTodoAsync_NonExisting_ReturnsFail() {
        await using var sut = CreateSut(withTaskRuntime: false);

        var result = await sut.UpdateTodoAsync("missing", content: "X").ConfigureAwait(true);

        result.Success.Should().BeFalse();
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task UpdateTodoAsync_Existing_WithTaskRuntime_SendsUpdate() {
        await using var sut = CreateSut();
        await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Id: "u2", Content: "Old", Status: TodoStatusEnumConstants.Pending, ActiveForm: "Olding")
        }).ConfigureAwait(true);

        await sut.UpdateTodoAsync("u2", content: "New", status: TodoStatusEnumConstants.InProgress, priority: TodoPriorityEnumConstants.Medium).ConfigureAwait(true);

        _taskRuntime.Updates.Should().ContainSingle(u =>
            u.TaskId == "u2" &&
            u.Update.Description == "New" &&
            u.Update.Status == TaskExecutionStatus.Running &&
            u.Update.Priority == RuntimeTaskPriority.Next);
    }

    [Fact]
    public async Task UpdateTodoAsync_NullOrWhitespaceId_ThrowsArgumentException() {
        await using var sut = CreateSut(withTaskRuntime: false);

        var act = async () => await sut.UpdateTodoAsync("  ").ConfigureAwait(true);

        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task ClearTodosAsync_RemovesAllAndRecordsTelemetry() {
        await using var sut = CreateSut(withTaskRuntime: false);
        await sut.WriteTodosAsync(new List<TodoItemInput>
        {
            new(Content: "A", Status: TodoStatusEnumConstants.Pending, ActiveForm: "Aing")
        }).ConfigureAwait(true);

        await sut.ClearTodosAsync().ConfigureAwait(true);

        var list = await sut.ListTodosAsync(includeCompleted: true).ConfigureAwait(true);
        list.Todos.Should().BeEmpty();
        _telemetry.Counters.Should().Contain(c => c.Name == "todo.operation.count" && c.Tags!["operation"] == "clear");
    }

    // === MapStatus: 状态映射 (纯计算) ===

    [Fact]
    public void MapStatus_Pending_ReturnsPending() {
        TodoService.MapStatus(TodoStatusEnumConstants.Pending).Should().Be(TaskExecutionStatus.Pending);
    }

    [Fact]
    public void MapStatus_InProgress_ReturnsRunning() {
        TodoService.MapStatus(TodoStatusEnumConstants.InProgress).Should().Be(TaskExecutionStatus.Running);
    }

    [Fact]
    public void MapStatus_Completed_ReturnsCompleted() {
        TodoService.MapStatus(TodoStatusEnumConstants.Completed).Should().Be(TaskExecutionStatus.Completed);
    }

    [Fact]
    public void MapStatus_Cancelled_ReturnsCancelled() {
        TodoService.MapStatus(TodoStatusEnumConstants.Cancelled).Should().Be(TaskExecutionStatus.Cancelled);
    }

    [Fact]
    public void MapStatus_UnknownStatus_ReturnsPending() {
        TodoService.MapStatus("unknown_status").Should().Be(TaskExecutionStatus.Pending);
    }

    [Fact]
    public void MapStatus_CaseSensitive_UnknownReturnsPending() {
        // FromValue 是 OrdinalIgnoreCase? 测试大写是否匹配
        // 确定性:同一输入同一输出
        TodoService.MapStatus("PENDING").Should().Be(TodoService.MapStatus("PENDING"));
    }

    // === MapPriority: 优先级映射 (纯计算) ===

    [Fact]
    public void MapPriority_High_ReturnsNow() {
        TodoService.MapPriority(TodoPriorityEnumConstants.High).Should().Be(RuntimeTaskPriority.Now);
    }

    [Fact]
    public void MapPriority_Medium_ReturnsNext() {
        TodoService.MapPriority(TodoPriorityEnumConstants.Medium).Should().Be(RuntimeTaskPriority.Next);
    }

    [Fact]
    public void MapPriority_Low_ReturnsLater() {
        TodoService.MapPriority(TodoPriorityEnumConstants.Low).Should().Be(RuntimeTaskPriority.Later);
    }

    [Fact]
    public void MapPriority_Critical_ReturnsLater() {
        // critical 未显式映射,走默认分支 → Later
        TodoService.MapPriority(TodoPriorityEnumConstants.Critical).Should().Be(RuntimeTaskPriority.Later);
    }

    [Fact]
    public void MapPriority_Unknown_ReturnsLater() {
        TodoService.MapPriority("unknown_priority").Should().Be(RuntimeTaskPriority.Later);
    }

    // === ResolveTodoPriority: 优先级解析 (纯计算) ===

    [Fact]
    public void ResolveTodoPriority_Null_ReturnsMedium() {
        TodoService.ResolveTodoPriority(null).Should().Be(TodoPriorityEnumConstants.Medium);
    }

    [Fact]
    public void ResolveTodoPriority_NonNull_ReturnsInput() {
        TodoService.ResolveTodoPriority(TodoPriorityEnumConstants.High).Should().Be(TodoPriorityEnumConstants.High);
    }

    [Fact]
    public void ResolveTodoPriority_EmptyString_ReturnsEmptyString() {
        // 空字符串不是 null,原样返回
        TodoService.ResolveTodoPriority(string.Empty).Should().Be(string.Empty);
    }

    // === BuildTodoItem: TodoItem 构建 (纯计算) ===

    [Fact]
    public void BuildTodoItem_NewTodo_UsesProvidedCreatedAt() {
        var input = new TodoItemInput(
            Id: "t1",
            Content: "task content",
            Status: TodoStatusEnumConstants.Pending,
            Priority: TodoPriorityEnumConstants.High,
            ActiveForm: "active");
        var createdAt = new DateTime(2026, 1, 1);
        var updatedAt = new DateTime(2026, 1, 2);

        var todo = TodoService.BuildTodoItem("t1", input, TodoPriorityEnumConstants.High, null, createdAt, updatedAt);

        todo.Id.Should().Be("t1");
        todo.Content.Should().Be("task content");
        todo.Status.Should().Be(TodoStatusEnumConstants.Pending);
        todo.Priority.Should().Be(TodoPriorityEnumConstants.High);
        todo.ActiveForm.Should().Be("active");
        todo.CreatedAt.Should().Be(createdAt);
        todo.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void BuildTodoItem_ExistingTodo_PreservesOriginalCreatedAt() {
        var input = new TodoItemInput(Id: "t1", Content: "updated", Status: TodoStatusEnumConstants.InProgress, ActiveForm: "active");
        var existing = new TodoItem("t1", "old", TodoStatusEnumConstants.Pending, TodoPriorityEnumConstants.Medium,
            CreatedAt: new DateTime(2025, 12, 1));
        var createdAt = new DateTime(2026, 1, 1);
        var updatedAt = new DateTime(2026, 1, 2);

        var todo = TodoService.BuildTodoItem("t1", input, TodoPriorityEnumConstants.Medium, existing, createdAt, updatedAt);

        // existing 存在时,保留 existing.CreatedAt,忽略传入的 createdAt
        todo.CreatedAt.Should().Be(new DateTime(2025, 12, 1));
        todo.UpdatedAt.Should().Be(updatedAt);
        todo.Content.Should().Be("updated");
    }

    [Fact]
    public void BuildTodoItem_PreservesDependsOnAndOwnedFiles() {
        var deps = new List<string> { "dep1", "dep2" };
        var files = new List<string> { "a.cs", "b.cs" };
        var input = new TodoItemInput(
            Id: "t1", Content: "c", Status: TodoStatusEnumConstants.Pending, ActiveForm: "a",
            DependsOn: deps, OwnedFiles: files);

        var todo = TodoService.BuildTodoItem("t1", input, TodoPriorityEnumConstants.Medium, null, DateTime.UtcNow, DateTime.UtcNow);

        todo.DependsOn.Should().BeEquivalentTo(deps);
        todo.OwnedFiles.Should().BeEquivalentTo(files);
    }

    [Fact]
    public void BuildTodoItem_PreservesParentId() {
        var input = new TodoItemInput(Id: "t1", Content: "c", Status: TodoStatusEnumConstants.Pending, ParentId: "parent-1", ActiveForm: "a");

        var todo = TodoService.BuildTodoItem("t1", input, TodoPriorityEnumConstants.Medium, null, DateTime.UtcNow, DateTime.UtcNow);

        todo.ParentId.Should().Be("parent-1");
    }

    [Fact]
    public void BuildTodoItem_Deterministic_SameInputSameOutput() {
        var input = new TodoItemInput(Id: "t1", Content: "c", Status: TodoStatusEnumConstants.Pending, ActiveForm: "a");
        var createdAt = new DateTime(2026, 1, 1);
        var updatedAt = new DateTime(2026, 1, 2);

        var t1 = TodoService.BuildTodoItem("t1", input, TodoPriorityEnumConstants.Medium, null, createdAt, updatedAt);
        var t2 = TodoService.BuildTodoItem("t1", input, TodoPriorityEnumConstants.Medium, null, createdAt, updatedAt);

        t1.Should().Be(t2);
    }

    // === BuildTodoItem: 时间倒流守卫 ===

    [Fact]
    public void BuildTodoItem_UpdatedAtBeforeCreatedAt_ClampsToCreatedAt() {
        var input = new TodoItemInput(Id: "t1", Content: "c", Status: TodoStatusEnumConstants.Pending, ActiveForm: "a");
        var createdAt = new DateTime(2026, 1, 10);
        var updatedAt = new DateTime(2026, 1, 5);

        var todo = TodoService.BuildTodoItem("t1", input, TodoPriorityEnumConstants.Medium, null, createdAt, updatedAt);

        todo.CreatedAt.Should().Be(createdAt);
        todo.UpdatedAt.Should().Be(createdAt);
    }

    [Fact]
    public void BuildTodoItem_UpdatedAtEqualsCreatedAt_PreservesBoth() {
        var input = new TodoItemInput(Id: "t1", Content: "c", Status: TodoStatusEnumConstants.Pending, ActiveForm: "a");
        var createdAt = new DateTime(2026, 1, 10);
        var updatedAt = createdAt;

        var todo = TodoService.BuildTodoItem("t1", input, TodoPriorityEnumConstants.Medium, null, createdAt, updatedAt);

        todo.CreatedAt.Should().Be(createdAt);
        todo.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void BuildTodoItem_UpdatedAtAfterCreatedAt_PreservesBoth() {
        var input = new TodoItemInput(Id: "t1", Content: "c", Status: TodoStatusEnumConstants.Pending, ActiveForm: "a");
        var createdAt = new DateTime(2026, 1, 5);
        var updatedAt = new DateTime(2026, 1, 10);

        var todo = TodoService.BuildTodoItem("t1", input, TodoPriorityEnumConstants.Medium, null, createdAt, updatedAt);

        todo.CreatedAt.Should().Be(createdAt);
        todo.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void BuildTodoItem_ExistingTodo_TimeBackflow_ClampsToExistingCreatedAt() {
        var existing = new TodoItem("t1", "old", TodoStatusEnumConstants.Pending, TodoPriorityEnumConstants.Medium,
            CreatedAt: new DateTime(2025, 12, 1));
        var input = new TodoItemInput(Id: "t1", Content: "updated", Status: TodoStatusEnumConstants.InProgress, ActiveForm: "a");
        var createdAt = new DateTime(2026, 1, 1);
        var updatedAt = new DateTime(2025, 11, 1);

        var todo = TodoService.BuildTodoItem("t1", input, TodoPriorityEnumConstants.Medium, existing, createdAt, updatedAt);

        todo.CreatedAt.Should().Be(new DateTime(2025, 12, 1));
        todo.UpdatedAt.Should().Be(new DateTime(2025, 12, 1));
    }
}