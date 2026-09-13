namespace Sync.Tests.ToolHandlers;

public class TaskOutputToolHandlersTests
{
    private readonly Mock<ITaskService> _taskService = new();
    private readonly TaskOutputToolHandlers _handler;

    public TaskOutputToolHandlersTests()
    {
        _handler = new TaskOutputToolHandlers(_taskService.Object, NullLogger<TaskOutputToolHandlers>.Instance);
    }

    [Fact]
    public async Task GetTaskOutputAsync_EmptyTaskId_ReturnsError()
    {
        var result = await _handler.GetTaskOutputAsync("", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("不能为空", result.GetTextContent());
    }

    [Fact]
    public async Task GetTaskOutputAsync_NullTaskId_ReturnsError()
    {
        var result = await _handler.GetTaskOutputAsync(null!, cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("不能为空", result.GetTextContent());
    }

    [Fact]
    public async Task GetTaskOutputAsync_TaskNotFound_ReturnsError()
    {
        _taskService.Setup(x => x.GetTaskAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TaskItem?)null);

        var result = await _handler.GetTaskOutputAsync("missing", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("未找到任务", result.GetTextContent());
    }

    [Fact]
    public async Task GetTaskOutputAsync_TaskFound_ReturnsSuccess()
    {
        _taskService.Setup(x => x.GetTaskAsync("id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskItem { Id = "id", Title = "Test", Description = "output", Status = "completed" });

        var result = await _handler.GetTaskOutputAsync("id", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("任务输出", result.GetTextContent());
        Assert.Contains("Test", result.GetTextContent());
    }

    [Fact]
    public async Task GetTaskOutputAsync_ServiceThrows_ReturnsError()
    {
        _taskService.Setup(x => x.GetTaskAsync("id", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await _handler.GetTaskOutputAsync("id", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("获取任务输出失败", result.GetTextContent());
    }
}
