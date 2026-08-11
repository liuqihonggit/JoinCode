
namespace Core.Tests.Todo;

public sealed class TaskToolHandlersTests
{
    private readonly Mock<ITaskService> _taskServiceMock = new();

    private TaskToolHandlers CreateSut() => new(_taskServiceMock.Object);

    [Fact]
    public async Task TaskCreateAsync_EmptyTitle_ReturnsEmptyTitleDiagnostic()
    {
        var sut = CreateSut();

        var result = await sut.TaskCreateAsync("  ").ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.Diagnostic.Should().NotBeNull();
        result.Diagnostic!.Reason.Should().Be("EmptyTitle");
        result.Diagnostic!.Details.Should().Contain(d => d.Key == "field" && d.Value == "title");
    }

    [Fact]
    public async Task TaskUpdateAsync_EmptyTaskId_ReturnsEmptyTaskIdDiagnostic()
    {
        var sut = CreateSut();
        var options = new TaskUpdateOptions { TaskId = "  " };

        var result = await sut.TaskUpdateAsync(options).ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.Diagnostic.Should().NotBeNull();
        result.Diagnostic!.Reason.Should().Be("EmptyTaskId");
        result.Diagnostic!.Details.Should().Contain(d => d.Key == "field" && d.Value == "task_id");
    }

    [Fact]
    public async Task TaskStopAsync_EmptyTaskId_ReturnsEmptyTaskIdDiagnostic()
    {
        var sut = CreateSut();

        var result = await sut.TaskStopAsync("  ").ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.Diagnostic.Should().NotBeNull();
        result.Diagnostic!.Reason.Should().Be("EmptyTaskId");
    }

    [Fact]
    public async Task TaskGetAsync_EmptyTaskId_ReturnsEmptyTaskIdDiagnostic()
    {
        var sut = CreateSut();

        var result = await sut.TaskGetAsync("  ").ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.Diagnostic.Should().NotBeNull();
        result.Diagnostic!.Reason.Should().Be("EmptyTaskId");
    }

    [Fact]
    public async Task TaskGetAsync_TaskNotFound_ReturnsTaskNotFoundDiagnostic()
    {
        var sut = CreateSut();
        _taskServiceMock.Setup(s => s.GetTaskAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TaskItem?)null);

        var result = await sut.TaskGetAsync("ghost-id").ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.Diagnostic.Should().NotBeNull();
        result.Diagnostic!.Reason.Should().Be("TaskNotFound");
        result.Diagnostic!.Details.Should().Contain(d => d.Key == "taskId" && d.Value == "ghost-id");
    }

    [Fact]
    public async Task TaskSetDependencyAsync_EmptyTaskId_ReturnsEmptyTaskIdDiagnostic()
    {
        var sut = CreateSut();

        var result = await sut.TaskSetDependencyAsync("  ", "dep-id").ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.Diagnostic.Should().NotBeNull();
        result.Diagnostic!.Reason.Should().Be("EmptyTaskId");
    }

    [Fact]
    public async Task TaskSetDependencyAsync_EmptyDependsOnTaskId_ReturnsEmptyDependsOnTaskIdDiagnostic()
    {
        var sut = CreateSut();

        var result = await sut.TaskSetDependencyAsync("task-id", "  ").ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.Diagnostic.Should().NotBeNull();
        result.Diagnostic!.Reason.Should().Be("EmptyDependsOnTaskId");
        result.Diagnostic!.Details.Should().Contain(d => d.Key == "field" && d.Value == "depends_on_task_id");
    }

    [Fact]
    public async Task TaskRemoveDependencyAsync_EmptyDependsOnTaskId_ReturnsEmptyDependsOnTaskIdDiagnostic()
    {
        var sut = CreateSut();

        var result = await sut.TaskRemoveDependencyAsync("task-id", "  ").ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.Diagnostic.Should().NotBeNull();
        result.Diagnostic!.Reason.Should().Be("EmptyDependsOnTaskId");
    }

    [Fact]
    public async Task TaskCreateAsync_ServiceFailure_ReturnsServiceFailureDiagnostic()
    {
        var sut = CreateSut();
        _taskServiceMock.Setup(s => s.CreateTaskAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<DateTime?>(), It.IsAny<string>(), It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<TaskItem?>.Fail("db error"));

        var result = await sut.TaskCreateAsync("valid title").ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.GetTextContent().Should().Contain("db error");
        result.Diagnostic.Should().NotBeNull();
        result.Diagnostic!.Reason.Should().Be("ServiceFailure");
        result.Diagnostic!.Details.Should().Contain(d => d.Key == "operation" && d.Value == "CreateTask");
    }

    [Fact]
    public async Task TaskUpdateAsync_ServiceFailure_ReturnsServiceFailureDiagnostic()
    {
        var sut = CreateSut();
        var options = new TaskUpdateOptions { TaskId = "task-id" };
        _taskServiceMock.Setup(s => s.UpdateTaskAsync(It.IsAny<UpdateTaskRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<TaskItem?>.Fail("not found"));

        var result = await sut.TaskUpdateAsync(options).ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.Diagnostic.Should().NotBeNull();
        result.Diagnostic!.Reason.Should().Be("ServiceFailure");
        result.Diagnostic!.Details.Should().Contain(d => d.Key == "taskId" && d.Value == "task-id");
    }

    [Fact]
    public void BuildEmptyTaskIdDiagnostic_ReturnsCorrectReasonAndDetails()
    {
        var diagnostic = TaskToolHandlers.BuildEmptyTaskIdDiagnostic();

        diagnostic.Reason.Should().Be("EmptyTaskId");
        diagnostic.Details.Should().Contain(d => d.Key == "field" && d.Value == "task_id");
        diagnostic.Suggestions.Should().Contain(s => s.Contains("TaskList"));
    }

    [Fact]
    public void BuildEmptyFieldDiagnostic_ReturnsCorrectReasonAndDetails()
    {
        var diagnostic = TaskToolHandlers.BuildEmptyFieldDiagnostic("EmptyTitle", "title", "title cannot be empty");

        diagnostic.Reason.Should().Be("EmptyTitle");
        diagnostic.FormattedMessage.Should().Be("title cannot be empty");
        diagnostic.Details.Should().Contain(d => d.Key == "field" && d.Value == "title");
        diagnostic.Suggestions.Should().Contain(s => s.Contains("title"));
    }
}
