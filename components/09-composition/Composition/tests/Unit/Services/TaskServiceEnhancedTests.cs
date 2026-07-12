
namespace Core.Tests.Services;

public class TaskServiceEnhancedTests
{
    private readonly ITaskService _taskService;

    public TaskServiceEnhancedTests()
    {
#pragma warning disable CS0618 // 内存版TaskService用于测试
        _taskService = new TaskService();
#pragma warning restore CS0618
    }

    private async Task<TaskItem> CreateTestTask(string title, string description = "")
    {
        var result = await _taskService.CreateTaskAsync(title, description, null, null, "medium", null).ConfigureAwait(true);
        return result.Task!;
    }

    [Fact]
    public async Task SetTaskDependencyAsync_WithValidTasks_ShouldSetDependency()
    {
        // Arrange
        var task1 = await CreateTestTask("Task 1", "Description 1").ConfigureAwait(true);
        var task2 = await CreateTestTask("Task 2", "Description 2").ConfigureAwait(true);

        // Act
        var result = await _taskService.SetTaskDependencyAsync(
            task1.Id,
            task2.Id,
            TaskDependencyType.Blocks).ConfigureAwait(true);

        // Assert
        result.Success.Should().BeTrue();
        result.Task.Should().NotBeNull();
    }

    [Fact]
    public async Task SetTaskDependencyAsync_WithNonExistentTask_ShouldFail()
    {
        // Arrange
        var task = await CreateTestTask("Task 1", "Description 1").ConfigureAwait(true);

        // Act
        var result = await _taskService.SetTaskDependencyAsync(
            task.Id,
            "non-existent-id").ConfigureAwait(true);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("不存在");
    }

    [Fact]
    public async Task SetTaskDependencyAsync_DuplicateDependency_ShouldFail()
    {
        // Arrange
        var task1 = await CreateTestTask("Task 1", "Description 1").ConfigureAwait(true);
        var task2 = await CreateTestTask("Task 2", "Description 2").ConfigureAwait(true);

        await _taskService.SetTaskDependencyAsync(task1.Id, task2.Id).ConfigureAwait(true);

        // Act
        var result = await _taskService.SetTaskDependencyAsync(task1.Id, task2.Id).ConfigureAwait(true);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("已存在");
    }

    [Fact]
    public async Task SetTaskDependencyAsync_CircularDependency_ShouldFail()
    {
        // Arrange
        var task1 = await CreateTestTask("Task 1", "Description 1").ConfigureAwait(true);
        var task2 = await CreateTestTask("Task 2", "Description 2").ConfigureAwait(true);
        var task3 = await CreateTestTask("Task 3", "Description 3").ConfigureAwait(true);

        // task1 -> task2 -> task3
        await _taskService.SetTaskDependencyAsync(task1.Id, task2.Id).ConfigureAwait(true);
        await _taskService.SetTaskDependencyAsync(task2.Id, task3.Id).ConfigureAwait(true);

        // Act - 尝试创建 task3 -> task1 (循环依赖)
        var result = await _taskService.SetTaskDependencyAsync(task3.Id, task1.Id).ConfigureAwait(true);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("循环依赖");
    }

    [Fact]
    public async Task GetTaskDependenciesAsync_WithNoDependencies_ShouldReturnEmpty()
    {
        // Arrange
        var task = await CreateTestTask("Task 1", "Description 1").ConfigureAwait(true);

        // Act
        var dependencies = await _taskService.GetTaskDependenciesAsync(task.Id).ConfigureAwait(true);

        // Assert
        dependencies.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTaskDependenciesAsync_WithDependencies_ShouldReturnList()
    {
        // Arrange
        var task1 = await CreateTestTask("Task 1", "Description 1").ConfigureAwait(true);
        var task2 = await CreateTestTask("Task 2", "Description 2").ConfigureAwait(true);
        var task3 = await CreateTestTask("Task 3", "Description 3").ConfigureAwait(true);

        await _taskService.SetTaskDependencyAsync(task1.Id, task2.Id, TaskDependencyType.Blocks).ConfigureAwait(true);
        await _taskService.SetTaskDependencyAsync(task1.Id, task3.Id, TaskDependencyType.Soft).ConfigureAwait(true);

        // Act
        var dependencies = await _taskService.GetTaskDependenciesAsync(task1.Id).ConfigureAwait(true);

        // Assert
        dependencies.Should().HaveCount(2);
        dependencies.Should().Contain(d => d.DependsOnTaskId == task2.Id && d.DependencyType == TaskDependencyType.Blocks);
        dependencies.Should().Contain(d => d.DependsOnTaskId == task3.Id && d.DependencyType == TaskDependencyType.Soft);
    }

    [Fact]
    public async Task RemoveTaskDependencyAsync_WithValidDependency_ShouldRemove()
    {
        // Arrange
        var task1 = await CreateTestTask("Task 1", "Description 1").ConfigureAwait(true);
        var task2 = await CreateTestTask("Task 2", "Description 2").ConfigureAwait(true);

        await _taskService.SetTaskDependencyAsync(task1.Id, task2.Id).ConfigureAwait(true);

        // Act
        var result = await _taskService.RemoveTaskDependencyAsync(task1.Id, task2.Id).ConfigureAwait(true);

        // Assert
        result.Success.Should().BeTrue();

        var dependencies = await _taskService.GetTaskDependenciesAsync(task1.Id).ConfigureAwait(true);
        dependencies.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveTaskDependencyAsync_WithNoDependencies_ShouldFail()
    {
        // Arrange
        var task1 = await CreateTestTask("Task 1", "Description 1").ConfigureAwait(true);
        var task2 = await CreateTestTask("Task 2", "Description 2").ConfigureAwait(true);

        // Act
        var result = await _taskService.RemoveTaskDependencyAsync(task1.Id, task2.Id).ConfigureAwait(true);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("不存在");
    }

    [Fact]
    public async Task CanExecuteTaskAsync_WithNoDependencies_ShouldReturnTrue()
    {
        // Arrange
        var task = await CreateTestTask("Task 1", "Description 1").ConfigureAwait(true);

        // Act
        var canExecute = await _taskService.CanExecuteTaskAsync(task.Id).ConfigureAwait(true);

        // Assert
        canExecute.Should().BeTrue();
    }

    [Fact]
    public async Task CanExecuteTaskAsync_WithCompletedDependency_ShouldReturnTrue()
    {
        // Arrange
        var task1 = await CreateTestTask("Task 1", "Description 1").ConfigureAwait(true);
        var task2 = await CreateTestTask("Task 2", "Description 2").ConfigureAwait(true);

        await _taskService.SetTaskDependencyAsync(task1.Id, task2.Id).ConfigureAwait(true);

        // 完成依赖任务
        await _taskService.UpdateTaskAsync(new UpdateTaskRequest { TaskId = task2.Id, Status = "completed" }).ConfigureAwait(true);

        // Act
        var canExecute = await _taskService.CanExecuteTaskAsync(task1.Id).ConfigureAwait(true);

        // Assert
        canExecute.Should().BeTrue();
    }

    [Fact]
    public async Task CanExecuteTaskAsync_WithPendingDependency_ShouldReturnFalse()
    {
        // Arrange
        var task1 = await CreateTestTask("Task 1", "Description 1").ConfigureAwait(true);
        var task2 = await CreateTestTask("Task 2", "Description 2").ConfigureAwait(true);

        await _taskService.SetTaskDependencyAsync(task1.Id, task2.Id).ConfigureAwait(true);

        // Act
        var canExecute = await _taskService.CanExecuteTaskAsync(task1.Id).ConfigureAwait(true);

        // Assert
        canExecute.Should().BeFalse();
    }

    [Fact]
    public async Task CanExecuteTaskAsync_WithNonExistentTask_ShouldReturnFalse()
    {
        // Act
        var canExecute = await _taskService.CanExecuteTaskAsync("non-existent-id").ConfigureAwait(true);

        // Assert
        canExecute.Should().BeFalse();
    }

    [Fact]
    public async Task CanExecuteTaskAsync_WithInProgressStatus_ShouldReturnFalse()
    {
        // Arrange
        var task = await CreateTestTask("Task 1", "Description 1").ConfigureAwait(true);
        await _taskService.UpdateTaskAsync(new UpdateTaskRequest { TaskId = task.Id, Status = "in_progress" }).ConfigureAwait(true);

        // Act
        var canExecute = await _taskService.CanExecuteTaskAsync(task.Id).ConfigureAwait(true);

        // Assert
        canExecute.Should().BeFalse();
    }

    [Fact]
    public async Task TaskDependency_WithDifferentTypes_ShouldWork()
    {
        // Arrange
        var task1 = await CreateTestTask("Task 1", "Description 1").ConfigureAwait(true);
        var task2 = await CreateTestTask("Task 2", "Description 2").ConfigureAwait(true);
        var task3 = await CreateTestTask("Task 3", "Description 3").ConfigureAwait(true);

        // Act
        await _taskService.SetTaskDependencyAsync(task1.Id, task2.Id, TaskDependencyType.Blocks).ConfigureAwait(true);
        await _taskService.SetTaskDependencyAsync(task1.Id, task3.Id, TaskDependencyType.Soft).ConfigureAwait(true);

        var dependencies = await _taskService.GetTaskDependenciesAsync(task1.Id).ConfigureAwait(true);

        // Assert
        dependencies.Should().HaveCount(2);
        dependencies.Should().Contain(d => d.DependencyType == TaskDependencyType.Blocks);
        dependencies.Should().Contain(d => d.DependencyType == TaskDependencyType.Soft);
    }

    [Fact]
    public async Task TaskDependency_ShouldHaveCreatedAtTimestamp()
    {
        // Arrange
        var before = DateTime.UtcNow.AddSeconds(-1);
        var task1 = await CreateTestTask("Task 1", "Description 1").ConfigureAwait(true);
        var task2 = await CreateTestTask("Task 2", "Description 2").ConfigureAwait(true);

        // Act
        await _taskService.SetTaskDependencyAsync(task1.Id, task2.Id).ConfigureAwait(true);
        var after = DateTime.UtcNow.AddSeconds(1);

        var dependencies = await _taskService.GetTaskDependenciesAsync(task1.Id).ConfigureAwait(true);

        // Assert
        dependencies.Should().HaveCount(1);
        dependencies.First().CreatedAt.Should().BeOnOrAfter(before);
        dependencies.First().CreatedAt.Should().BeOnOrBefore(after);
    }

    [Fact]
    public async Task RemoveTaskDependency_AndCanExecute_ShouldUpdate()
    {
        // Arrange
        var task1 = await CreateTestTask("Task 1", "Description 1").ConfigureAwait(true);
        var task2 = await CreateTestTask("Task 2", "Description 2").ConfigureAwait(true);

        await _taskService.SetTaskDependencyAsync(task1.Id, task2.Id).ConfigureAwait(true);

        // 最初不能执行
        var canExecuteBefore = await _taskService.CanExecuteTaskAsync(task1.Id).ConfigureAwait(true);
        canExecuteBefore.Should().BeFalse();

        // Act - 移除依赖
        await _taskService.RemoveTaskDependencyAsync(task1.Id, task2.Id).ConfigureAwait(true);

        // Assert - 现在可以执行
        var canExecuteAfter = await _taskService.CanExecuteTaskAsync(task1.Id).ConfigureAwait(true);
        canExecuteAfter.Should().BeTrue();
    }

    [Fact]
    public async Task MultipleDependencies_AllMustBeCompleted()
    {
        // Arrange
        var task1 = await CreateTestTask("Task 1", "Description 1").ConfigureAwait(true);
        var task2 = await CreateTestTask("Task 2", "Description 2").ConfigureAwait(true);
        var task3 = await CreateTestTask("Task 3", "Description 3").ConfigureAwait(true);

        await _taskService.SetTaskDependencyAsync(task1.Id, task2.Id).ConfigureAwait(true);
        await _taskService.SetTaskDependencyAsync(task1.Id, task3.Id).ConfigureAwait(true);

        // 只完成一个依赖
        await _taskService.UpdateTaskAsync(new UpdateTaskRequest { TaskId = task2.Id, Status = "completed" }).ConfigureAwait(true);

        // Act
        var canExecute = await _taskService.CanExecuteTaskAsync(task1.Id).ConfigureAwait(true);

        // Assert - 还有一个依赖未完成
        canExecute.Should().BeFalse();

        // 完成另一个依赖
        await _taskService.UpdateTaskAsync(new UpdateTaskRequest { TaskId = task3.Id, Status = "completed" }).ConfigureAwait(true);

        // Act again
        canExecute = await _taskService.CanExecuteTaskAsync(task1.Id).ConfigureAwait(true);

        // Assert - 现在可以执行
        canExecute.Should().BeTrue();
    }
}
