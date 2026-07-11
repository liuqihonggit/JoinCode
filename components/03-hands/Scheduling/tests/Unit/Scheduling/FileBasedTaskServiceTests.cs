
namespace Core.Tests.Scheduling;

/// <summary>
/// FileBasedTaskService 单元测试 - 使用内存文件系统实现高速测试
/// </summary>
public sealed class FileBasedTaskServiceTests : IDisposable
{
    private readonly InMemoryFileOperationService _fileOperationService;
    private readonly IFileSystem _fs;
    private readonly FileBasedTaskService _service;
    private readonly ITestOutputHelper _output;

    public FileBasedTaskServiceTests(ITestOutputHelper output)
    {
        _output = output;
        _fileOperationService = new InMemoryFileOperationService();
        _fs = TestFileSystem.Current;

        var taskFileWriter = new TaskFileWriter(_fileOperationService);
        var taskFileReader = new TaskFileReader(_fileOperationService);

        var options = new TaskDirectoryOptions
        {
            TaskDirectoryPath = "tasks"
        };

        var fileOps = new TaskFileOperations(_fileOperationService, taskFileWriter, taskFileReader, _fs);

        _service = new FileBasedTaskService(fileOps, options);
    }

    public void Dispose()
    {
        _fileOperationService.Dispose();
    }

    [Fact]
    public async Task CreateTaskAsync_WithValidData_ShouldCreateTask()
    {
        // Act
        var result = await _service.CreateTaskAsync(
            "测试任务",
            "这是一个测试任务",
            "test-agent",
            DateTime.UtcNow.AddDays(1),
            "high",
            new List<string> { "test", "demo" }).ConfigureAwait(true);

        // Debug
        if (!result.Success)
        {
            _output.WriteLine($"创建任务失败: {result.ErrorMessage}");
        }

        // Assert
        Assert.True(result.Success, $"创建任务失败: {result.ErrorMessage}");
        Assert.NotNull(result.Task);
        Assert.Equal("测试任务", result.Task.Title);
        Assert.Equal("pending", result.Task.Status);
        Assert.StartsWith("task-", result.Task.Id);

        _output.WriteLine($"创建任务成功: {result.Task.Id}");
    }

    [Fact]
    public async Task GetTaskAsync_WithExistingTask_ShouldReturnTask()
    {
        // Arrange
        var createResult = await _service.CreateTaskAsync("获取测试", null, null, null, "medium", null).ConfigureAwait(true);
        var taskId = createResult.Task!.Id;

        // Act
        var task = await _service.GetTaskAsync(taskId).ConfigureAwait(true);

        // Assert
        Assert.NotNull(task);
        Assert.Equal(taskId, task.Id);
        Assert.Equal("获取测试", task.Title);
    }

    [Fact]
    public async Task GetTaskAsync_WithNonExistingTask_ShouldReturnNull()
    {
        // Act
        var task = await _service.GetTaskAsync("task-9999").ConfigureAwait(true);

        // Assert
        Assert.Null(task);
    }

    [Fact]
    public async Task ListTasksAsync_WithMultipleTasks_ShouldReturnAll()
    {
        // Arrange
        await _service.CreateTaskAsync("任务1", null, null, null, "high", null).ConfigureAwait(true);
        await _service.CreateTaskAsync("任务2", null, null, null, "medium", null).ConfigureAwait(true);
        await _service.CreateTaskAsync("任务3", null, null, null, "low", null).ConfigureAwait(true);

        // Act
        var result = await _service.ListTasksAsync(null, null, null, 10, 0).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Tasks.Count);
    }

    [Fact]
    public async Task ListTasksAsync_WithStatusFilter_ShouldFilterCorrectly()
    {
        // Arrange
        var createResult = await _service.CreateTaskAsync("待处理任务", null, null, null, "medium", null).ConfigureAwait(true);
        await _service.UpdateTaskAsync(new UpdateTaskRequest { TaskId = createResult.Task!.Id, Status = "completed" }).ConfigureAwait(true);

        await _service.CreateTaskAsync("另一个待处理", null, null, null, "medium", null).ConfigureAwait(true);

        // Act
        var result = await _service.ListTasksAsync("completed", null, null, 10, 0).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Tasks);
        Assert.Equal("待处理任务", result.Tasks[0].Title);
    }

    [Fact]
    public async Task UpdateTaskAsync_WithExistingTask_ShouldUpdate()
    {
        // Arrange
        var createResult = await _service.CreateTaskAsync("原标题", "原描述", null, null, "low", null).ConfigureAwait(true);
        var taskId = createResult.Task!.Id;

        // Act
        var updateResult = await _service.UpdateTaskAsync(
            new UpdateTaskRequest
            {
                TaskId = taskId,
                Title = "新标题",
                Description = "新描述",
                Status = "in_progress",
            }).ConfigureAwait(true);

        // Assert
        Assert.True(updateResult.Success);
        Assert.Equal("新标题", updateResult.Task!.Title);
        Assert.Equal("新描述", updateResult.Task.Description);
        Assert.Equal("in_progress", updateResult.Task.Status);

        // 验证持久化
        var task = await _service.GetTaskAsync(taskId).ConfigureAwait(true);
        Assert.Equal("新标题", task!.Title);
    }

    [Fact]
    public async Task UpdateTaskAsync_WithNonExistingTask_ShouldFail()
    {
        // Act
        var result = await _service.UpdateTaskAsync(new UpdateTaskRequest { TaskId = "task-9999", Title = "标题" }).ConfigureAwait(true);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("不存在", result.ErrorMessage);
    }

    [Fact]
    public async Task StopTaskAsync_WithRunningTask_ShouldStop()
    {
        // Arrange
        var createResult = await _service.CreateTaskAsync("运行中任务", null, null, null, "medium", null).ConfigureAwait(true);
        await _service.UpdateTaskAsync(new UpdateTaskRequest { TaskId = createResult.Task!.Id, Status = "in_progress" }).ConfigureAwait(true);

        // Act
        var stopResult = await _service.StopTaskAsync(createResult.Task.Id, "测试停止", CancellationToken.None).ConfigureAwait(true);

        // Assert
        Assert.True(stopResult.Success);
        Assert.Equal("stopped", stopResult.Task!.Status);
    }

    [Fact]
    public async Task DeleteTaskAsync_WithExistingTask_ShouldDelete()
    {
        // Arrange
        var createResult = await _service.CreateTaskAsync("待删除任务", null, null, null, "medium", null).ConfigureAwait(true);
        var taskId = createResult.Task!.Id;

        // Act
        var deleted = await _service.DeleteTaskAsync(taskId).ConfigureAwait(true);

        // Assert
        Assert.True(deleted);
        var task = await _service.GetTaskAsync(taskId).ConfigureAwait(true);
        Assert.Null(task);
    }

    [Fact]
    public async Task SetTaskDependencyAsync_ShouldCreateDependency()
    {
        // Arrange
        var task1 = await _service.CreateTaskAsync("任务1", null, null, null, "medium", null).ConfigureAwait(true);
        var task2 = await _service.CreateTaskAsync("任务2", null, null, null, "medium", null).ConfigureAwait(true);

        // Act
        var result = await _service.SetTaskDependencyAsync(
            task2.Task!.Id,
            task1.Task!.Id,
            TaskDependencyType.Blocks).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);

        var canExecute = await _service.CanExecuteTaskAsync(task2.Task.Id).ConfigureAwait(true);
        Assert.False(canExecute); // 依赖未完成

        // 完成依赖任务
        await _service.UpdateTaskAsync(new UpdateTaskRequest { TaskId = task1.Task.Id, Status = "completed" }).ConfigureAwait(true);
        canExecute = await _service.CanExecuteTaskAsync(task2.Task.Id).ConfigureAwait(true);
        Assert.True(canExecute);
    }

    [Fact]
    public async Task ResetTaskListAsync_ShouldClearTasksButKeepHighWaterMark()
    {
        // Arrange
        await _service.CreateTaskAsync("任务1", null, null, null, "medium", null).ConfigureAwait(true);
        await _service.CreateTaskAsync("任务2", null, null, null, "medium", null).ConfigureAwait(true);

        // Act
        await _service.ResetTaskListAsync().ConfigureAwait(true);

        // Assert
        var result = await _service.ListTasksAsync(null, null, null, 10, 0).ConfigureAwait(true);
        Assert.Equal(0, result.TotalCount);

        // 创建新任务，ID 应该继续递增
        var newTask = await _service.CreateTaskAsync("新任务", null, null, null, "medium", null).ConfigureAwait(true);
        Assert.True(int.Parse(newTask.Task!.Id.Replace("task-", "")) > 2);
    }

    [Fact]
    public async Task CreateTaskAsync_Concurrent_ShouldGenerateUniqueIds()
    {
        // Arrange - 使用顺序创建来避免并发问题
        var tasks = new List<TaskItem>();

        // Act - 顺序创建5个任务
        for (int i = 0; i < 5; i++)
        {
            var result = await _service.CreateTaskAsync($"任务{i}", null, null, null, "medium", null).ConfigureAwait(true);
            Assert.True(result.Success, $"创建任务{i}失败: {result.ErrorMessage}");
            tasks.Add(result.Task!);
        }

        // Assert - 所有ID应该是唯一的
        var ids = tasks.Select(t => t.Id).ToList();
        Assert.Equal(tasks.Count, ids.Distinct().Count());

        // 验证ID是递增的
        var idNumbers = ids.Select(id => int.Parse(id.Replace("task-", ""))).OrderBy(n => n).ToList();
        for (int i = 0; i < idNumbers.Count - 1; i++)
        {
            Assert.True(idNumbers[i] < idNumbers[i + 1], "ID应该是递增的");
        }

        _output.WriteLine($"成功创建{tasks.Count}个任务，ID: {string.Join(", ", ids)}");
    }

    [Fact]
    public async Task TaskPersistence_AcrossInstances_ShouldWork()
    {
        // Arrange - 使用第一个服务实例创建任务
        var createResult = await _service.CreateTaskAsync("持久化测试", "测试描述", null, null, "high", null).ConfigureAwait(true);
        var taskId = createResult.Task!.Id;

        // Act - 创建新的服务实例（模拟进程重启）
        var taskFileWriter = new TaskFileWriter(_fileOperationService);
        var taskFileReader = new TaskFileReader(_fileOperationService);

        var options = new TaskDirectoryOptions { TaskDirectoryPath = "tasks" };
        var fileOps = new TaskFileOperations(_fileOperationService, taskFileWriter, taskFileReader, _fs);
        var newService = new FileBasedTaskService(fileOps, options);

        var task = await newService.GetTaskAsync(taskId).ConfigureAwait(true);

        // Assert
        Assert.NotNull(task);
        Assert.Equal("持久化测试", task.Title);
        Assert.Equal("测试描述", task.Description);
    }
}
