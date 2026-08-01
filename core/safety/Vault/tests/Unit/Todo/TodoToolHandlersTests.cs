
namespace Core.Tests.Todo;

public sealed class TodoToolHandlersTests
{
    private readonly Mock<ITodoService> _todoServiceMock = new();

    private TodoToolHandlers CreateSut() => new(_todoServiceMock.Object);

    [Fact]
    public async Task TodoWriteAsync_EmptyList_ReturnsSuccess()
    {
        var sut = CreateSut();
        _todoServiceMock.Setup(s => s.WriteTodosAsync(It.IsAny<List<TodoItemInput>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoServiceResult(true, 0, 0, 0, new List<TodoItem>()));

        var result = await sut.TodoWriteAsync([]).ConfigureAwait(true);

        result.IsError.Should().BeFalse();
        result.GetTextContent().Should().Contain("Todos have been successfully written");
    }

    [Fact]
    public async Task TodoWriteAsync_NullInput_TreatsAsEmpty()
    {
        var sut = CreateSut();
        _todoServiceMock.Setup(s => s.WriteTodosAsync(It.IsAny<List<TodoItemInput>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoServiceResult(true, 0, 0, 0, new List<TodoItem>()));

        var result = await sut.TodoWriteAsync(null).ConfigureAwait(true);

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task TodoWriteAsync_EmptyContent_ReturnsError()
    {
        var sut = CreateSut();
        var todos = new List<TodoItemInput>
        {
            new(Content: "  ", Status: TodoStatusConstants.Pending, ActiveForm: "Spacing")
        };

        var result = await sut.TodoWriteAsync(todos).ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.GetTextContent().Should().Contain("content cannot be empty");
    }

    [Fact]
    public async Task TodoWriteAsync_InvalidStatus_ReturnsError()
    {
        var sut = CreateSut();
        var todos = new List<TodoItemInput>
        {
            new(Content: "Bad status", Status: "blocked", ActiveForm: "Blocking")
        };

        var result = await sut.TodoWriteAsync(todos).ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.GetTextContent().Should().Contain("Invalid status");
    }

    [Fact]
    public async Task TodoWriteAsync_InvalidPriority_ReturnsError()
    {
        var sut = CreateSut();
        var todos = new List<TodoItemInput>
        {
            new(Content: "Bad priority", Status: TodoStatusConstants.Pending, Priority: "urgent", ActiveForm: "Prioritizing")
        };

        var result = await sut.TodoWriteAsync(todos).ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.GetTextContent().Should().Contain("Invalid priority");
    }

    [Fact]
    public async Task TodoWriteAsync_ValidTodo_CallsServiceAndReturnsSuccess()
    {
        var sut = CreateSut();
        var todos = new List<TodoItemInput>
        {
            new(Content: "Do work", Status: TodoStatusConstants.InProgress, ActiveForm: "Doing work")
        };
        _todoServiceMock.Setup(s => s.WriteTodosAsync(It.IsAny<List<TodoItemInput>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoServiceResult(true, 1, 0, 0, new List<TodoItem>()));

        var result = await sut.TodoWriteAsync(todos).ConfigureAwait(true);

        _todoServiceMock.Verify(s => s.WriteTodosAsync(It.Is<List<TodoItemInput>>(list =>
            list.Count == 1 &&
            list[0].Content == "Do work" &&
            list[0].Priority == TodoPriorityConstants.Medium &&
            !string.IsNullOrEmpty(list[0].Id)), It.IsAny<CancellationToken>()), Times.Once);
        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task TodoWriteAsync_ServiceFailure_ReturnsError()
    {
        var sut = CreateSut();
        var todos = new List<TodoItemInput>
        {
            new(Content: "Fail", Status: TodoStatusConstants.Pending, ActiveForm: "Failing")
        };
        _todoServiceMock.Setup(s => s.WriteTodosAsync(It.IsAny<List<TodoItemInput>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoServiceResult(false, 0, 0, 0, new List<TodoItem>(), "boom"));

        var result = await sut.TodoWriteAsync(todos).ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.GetTextContent().Should().Contain("boom");
    }

    [Fact]
    public async Task TodoWriteAsync_AllDone_ClearsAndAddsVerificationNudge()
    {
        var sut = CreateSut();
        var todos = new List<TodoItemInput>
        {
            new(Content: "A", Status: TodoStatusConstants.Completed, ActiveForm: "Completing A"),
            new(Content: "B", Status: TodoStatusConstants.Completed, ActiveForm: "Completing B"),
            new(Content: "C", Status: TodoStatusConstants.Completed, ActiveForm: "Completing C")
        };

        var result = await sut.TodoWriteAsync(todos).ConfigureAwait(true);

        _todoServiceMock.Verify(s => s.WriteTodosAsync(It.IsAny<List<TodoItemInput>>(), It.IsAny<CancellationToken>()), Times.Once);
        _todoServiceMock.Verify(s => s.ClearTodosAsync(It.IsAny<CancellationToken>()), Times.Once);
        result.IsError.Should().BeFalse();
        result.GetTextContent().Should().Contain("verification agent");
    }

    [Fact]
    public async Task TodoWriteAsync_AllDone_LessThanThree_NoNudge()
    {
        var sut = CreateSut();
        var todos = new List<TodoItemInput>
        {
            new(Content: "A", Status: TodoStatusConstants.Completed, ActiveForm: "Completing A"),
            new(Content: "B", Status: TodoStatusConstants.Completed, ActiveForm: "Completing B")
        };

        var result = await sut.TodoWriteAsync(todos).ConfigureAwait(true);

        result.GetTextContent().Should().NotContain("verification agent");
    }

    [Fact]
    public async Task TodoWriteAsync_AllDone_ContainsVerify_NoNudge()
    {
        var sut = CreateSut();
        var todos = new List<TodoItemInput>
        {
            new(Content: "verify output", Status: TodoStatusConstants.Completed, ActiveForm: "Verifying output"),
            new(Content: "B", Status: TodoStatusConstants.Completed, ActiveForm: "Completing B"),
            new(Content: "C", Status: TodoStatusConstants.Completed, ActiveForm: "Completing C")
        };

        var result = await sut.TodoWriteAsync(todos).ConfigureAwait(true);

        result.GetTextContent().Should().NotContain("verification agent");
    }

    [Fact]
    public async Task TodoListAsync_InvalidStatusFilter_ReturnsError()
    {
        var sut = CreateSut();

        var result = await sut.TodoListAsync(status: "unknown").ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.GetTextContent().Should().Contain("Invalid status filter");
    }

    [Fact]
    public async Task TodoListAsync_InvalidPriorityFilter_ReturnsError()
    {
        var sut = CreateSut();

        var result = await sut.TodoListAsync(priority: "unknown").ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.GetTextContent().Should().Contain("Invalid priority filter");
    }

    [Fact]
    public async Task TodoListAsync_ServiceFailure_ReturnsError()
    {
        var sut = CreateSut();
        _todoServiceMock.Setup(s => s.ListTodosAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoListResult(false, new List<TodoItem>(), "fail"));

        var result = await sut.TodoListAsync().ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.GetTextContent().Should().Contain("fail");
    }

    [Fact]
    public async Task TodoListAsync_EmptyResult_IncludesNoItemsMessage()
    {
        var sut = CreateSut();
        _todoServiceMock.Setup(s => s.ListTodosAsync(null, null, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoListResult(true, new List<TodoItem>()));

        var result = await sut.TodoListAsync().ConfigureAwait(true);

        result.IsError.Should().BeFalse();
        result.GetTextContent().Should().Contain("No todo items found");
        result.GetTextContent().Should().Contain("Total: 0");
    }

    [Fact]
    public async Task TodoListAsync_WithResult_IncludesFormattedSummary()
    {
        var sut = CreateSut();
        var items = new List<TodoItem>
        {
            new("id1", "Task one", TodoStatusConstants.InProgress, TodoPriorityConstants.High, ActiveForm: "Doing task one"),
            new("id2", "Task two", TodoStatusConstants.Pending, TodoPriorityConstants.Low, ActiveForm: "Doing task two")
        };
        _todoServiceMock.Setup(s => s.ListTodosAsync(null, null, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoListResult(true, items));

        var result = await sut.TodoListAsync().ConfigureAwait(true);

        var text = result.GetTextContent();
        text.Should().Contain("Total: 2");
        text.Should().Contain("Pending: 2");
        text.Should().Contain("Completed: 0");
        text.Should().Contain("[id1]");
        text.Should().Contain("(Doing task one)");
    }

    [Fact]
    public async Task TodoUpdateAsync_EmptyId_ReturnsError()
    {
        var sut = CreateSut();

        var result = await sut.TodoUpdateAsync("   ").ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.GetTextContent().Should().Contain("cannot be empty");
    }

    [Fact]
    public async Task TodoUpdateAsync_InvalidStatus_ReturnsError()
    {
        var sut = CreateSut();

        var result = await sut.TodoUpdateAsync("id", status: "unknown").ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.GetTextContent().Should().Contain("Invalid status");
    }

    [Fact]
    public async Task TodoUpdateAsync_InvalidPriority_ReturnsError()
    {
        var sut = CreateSut();

        var result = await sut.TodoUpdateAsync("id", priority: "unknown").ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.GetTextContent().Should().Contain("Invalid priority");
    }

    [Fact]
    public async Task TodoUpdateAsync_ServiceFailure_ReturnsError()
    {
        var sut = CreateSut();
        _todoServiceMock.Setup(s => s.UpdateTodoAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<TodoItem?>.Fail("not found"));

        var result = await sut.TodoUpdateAsync("id", content: "X").ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.GetTextContent().Should().Contain("not found");
    }

    [Fact]
    public async Task TodoUpdateAsync_Success_IncludesSummary()
    {
        var sut = CreateSut();
        var updated = new TodoItem("id", "New content", TodoStatusConstants.Completed, TodoPriorityConstants.Medium, ActiveForm: "Doing new");
        _todoServiceMock.Setup(s => s.UpdateTodoAsync("id", "New content", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<TodoItem?>.Ok(updated));

        var result = await sut.TodoUpdateAsync("id", content: "New content").ConfigureAwait(true);

        result.IsError.Should().BeFalse();
        var text = result.GetTextContent();
        text.Should().Contain("updated successfully");
        text.Should().Contain("[id]");
        text.Should().Contain("New content");
    }
}
