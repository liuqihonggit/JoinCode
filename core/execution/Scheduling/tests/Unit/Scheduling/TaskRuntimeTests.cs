
namespace Core.Tests.Scheduling;

public class TaskRuntimeTests : IDisposable
{
    private readonly TaskRuntime _runtime;

    public TaskRuntimeTests()
    {
        _runtime = new TaskRuntime();
    }

    public void Dispose()
    {
        _runtime.Dispose();
    }

    [Fact]
    public async Task CreateTaskAsync_ShouldReturnTaskWithId()
    {
        var result = await _runtime.CreateTaskAsync(new RuntimeTaskInput { Description = "test" });

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().StartWith("rtask_");
        result.Data.Description.Should().Be("test");
    }

    [Fact]
    public async Task CreateTaskAsync_NullInput_ShouldThrow()
    {
        var act = () => _runtime.CreateTaskAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task CreateTaskAsync_WithDependencies_ShouldBuildDag()
    {
        var a = await _runtime.CreateTaskAsync(new RuntimeTaskInput { Description = "a" });
        var b = await _runtime.CreateTaskAsync(new RuntimeTaskInput { Description = "b", Dependencies = new List<string> { a.Data!.Id } });

        b.Data!.Dependencies.Should().Contain(a.Data.Id);
        (await _runtime.CanExecuteTaskAsync(b.Data.Id)).Should().BeFalse();
        await _runtime.UpdateTaskAsync(a.Data.Id, new RuntimeTaskUpdate { Status = TaskExecutionStatus.Completed });
        (await _runtime.CanExecuteTaskAsync(b.Data.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task UpdateTaskAsync_NonExistent_ShouldFail()
    {
        var result = await _runtime.UpdateTaskAsync("missing", new RuntimeTaskUpdate { Status = TaskExecutionStatus.Completed });
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateTaskAsync_ShouldSetStartedAt_WhenRunning()
    {
        var created = await _runtime.CreateTaskAsync(new RuntimeTaskInput { Description = "x" });

        var result = await _runtime.UpdateTaskAsync(created.Data!.Id, new RuntimeTaskUpdate { Status = TaskExecutionStatus.Running });

        result.Success.Should().BeTrue();
        result.Data!.StartedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateTaskAsync_ShouldSetCompletedAt_WhenTerminal()
    {
        var created = await _runtime.CreateTaskAsync(new RuntimeTaskInput { Description = "x" });

        var result = await _runtime.UpdateTaskAsync(created.Data!.Id, new RuntimeTaskUpdate { Status = TaskExecutionStatus.Completed });

        result.Success.Should().BeTrue();
        result.Data!.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetTaskAsync_NonExistent_ShouldFail()
    {
        var result = await _runtime.GetTaskAsync("missing");
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ListTasksAsync_ShouldFilterByStatus()
    {
        var a = await _runtime.CreateTaskAsync(new RuntimeTaskInput { Description = "a" });
        await _runtime.CreateTaskAsync(new RuntimeTaskInput { Description = "b" });
        await _runtime.UpdateTaskAsync(a.Data!.Id, new RuntimeTaskUpdate { Status = TaskExecutionStatus.Running });

        var result = await _runtime.ListTasksAsync(new RuntimeTaskQuery { Status = TaskExecutionStatus.Running });

        result.Success.Should().BeTrue();
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task ListTasksAsync_IncludeCompletedFalse_ShouldExcludeCompleted()
    {
        var a = await _runtime.CreateTaskAsync(new RuntimeTaskInput { Description = "a" });
        var b = await _runtime.CreateTaskAsync(new RuntimeTaskInput { Description = "b" });
        await _runtime.UpdateTaskAsync(a.Data!.Id, new RuntimeTaskUpdate { Status = TaskExecutionStatus.Completed });

        var result = await _runtime.ListTasksAsync(new RuntimeTaskQuery { IncludeCompleted = false });

        result.TotalCount.Should().Be(1);
        result.Tasks[0].Id.Should().Be(b.Data!.Id);
    }

    [Fact]
    public async Task ListTasksAsync_ShouldPage()
    {
        for (int i = 0; i < 5; i++)
        {
            await _runtime.CreateTaskAsync(new RuntimeTaskInput { Description = $"task-{i}" });
        }

        var result = await _runtime.ListTasksAsync(new RuntimeTaskQuery { Limit = 2, Offset = 0 });
        result.TotalCount.Should().Be(5);
        result.Tasks.Should().HaveCount(2);
    }

    [Fact]
    public async Task SetDependencyAsync_Cycle_ShouldFail()
    {
        var a = await _runtime.CreateTaskAsync(new RuntimeTaskInput { Description = "a" });
        var b = await _runtime.CreateTaskAsync(new RuntimeTaskInput { Description = "b", Dependencies = new List<string> { a.Data!.Id } });

        var result = await _runtime.SetDependencyAsync(a.Data.Id, b.Data!.Id);
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task SetDependencyAsync_NonExistentTask_ShouldFail()
    {
        var a = await _runtime.CreateTaskAsync(new RuntimeTaskInput { Description = "a" });

        var result = await _runtime.SetDependencyAsync(a.Data!.Id, "missing");
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task SetDependencyAsync_Duplicate_ShouldBeAllowed()
    {
        var a = await _runtime.CreateTaskAsync(new RuntimeTaskInput { Description = "a" });
        var b = await _runtime.CreateTaskAsync(new RuntimeTaskInput { Description = "b" });
        await _runtime.SetDependencyAsync(b.Data!.Id, a.Data!.Id);

        var result = await _runtime.SetDependencyAsync(b.Data.Id, a.Data.Id);
        result.Success.Should().BeTrue();
        b.Data.Dependencies.Should().Contain(a.Data.Id);
    }

    [Fact]
    public async Task RemoveDependencyAsync_ShouldWork()
    {
        var a = await _runtime.CreateTaskAsync(new RuntimeTaskInput { Description = "a" });
        var b = await _runtime.CreateTaskAsync(new RuntimeTaskInput { Description = "b" });
        await _runtime.SetDependencyAsync(b.Data!.Id, a.Data!.Id);

        var result = await _runtime.RemoveDependencyAsync(b.Data.Id, a.Data.Id);
        result.Success.Should().BeTrue();
        (await _runtime.CanExecuteTaskAsync(b.Data.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task CanExecuteTaskAsync_NonExistent_ShouldReturnFalse()
    {
        (await _runtime.CanExecuteTaskAsync("missing")).Should().BeFalse();
    }

    [Fact]
    public async Task CanExecuteTaskAsync_NonPendingOrReady_ShouldReturnFalse()
    {
        var task = await _runtime.CreateTaskAsync(new RuntimeTaskInput { Description = "x" });
        await _runtime.UpdateTaskAsync(task.Data!.Id, new RuntimeTaskUpdate { Status = TaskExecutionStatus.Running });

        (await _runtime.CanExecuteTaskAsync(task.Data.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task DequeueReadyTasksAsync_ShouldReturnReadyTasks()
    {
        var a = await _runtime.CreateTaskAsync(new RuntimeTaskInput { Description = "a" });
        var b = await _runtime.CreateTaskAsync(new RuntimeTaskInput { Description = "b", Dependencies = new List<string> { a.Data!.Id } });
        var lightweight = await _runtime.CreateTaskAsync(new RuntimeTaskInput { Description = "lw", IsLightweight = true });

        var ready = await _runtime.DequeueReadyTasksAsync();
        ready.Select(t => t.Id).Should().Contain(a.Data.Id).And.Contain(lightweight.Data!.Id);
        ready.Select(t => t.Id).Should().NotContain(b.Data!.Id);

        await _runtime.UpdateTaskAsync(a.Data.Id, new RuntimeTaskUpdate { Status = TaskExecutionStatus.Completed });
        ready = await _runtime.DequeueReadyTasksAsync();
        ready.Select(t => t.Id).Should().Contain(b.Data.Id);
    }

    [Fact]
    public async Task Clear_ShouldRemoveAllTasks()
    {
        await _runtime.CreateTaskAsync(new RuntimeTaskInput { Description = "x" });
        _runtime.Clear();

        var result = await _runtime.ListTasksAsync(new RuntimeTaskQuery());
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteRemoteAgentTaskAsync_WithoutExecutor_ShouldThrow()
    {
        var act = () => _runtime.ExecuteRemoteAgentTaskAsync(new RemoteAgentTaskDefinition { TaskId = "x", Endpoint = "http://x", TaskDescription = "d" });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ExecuteWorkflowTaskAsync_WithoutExecutor_ShouldThrow()
    {
        var act = () => _runtime.ExecuteWorkflowTaskAsync(new WorkflowDefinition { WorkflowId = "x", Steps = new List<WorkflowStep>() });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StartMcpMonitoringAsync_WithoutExecutor_ShouldThrow()
    {
        var act = () => _runtime.StartMcpMonitoringAsync(new McpMonitorConfig { ServerName = "x" });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ExecuteLocalShellTaskAsync_WithoutExecutor_ShouldThrow()
    {
        var act = () => _runtime.ExecuteLocalShellTaskAsync(new LocalShellTaskDefinition { TaskId = "x", Command = "echo" });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ExecuteInProcessTeammateAsync_WithoutExecutor_ShouldThrow()
    {
        var act = () => _runtime.ExecuteInProcessTeammateAsync(new InProcessTeammateDefinition { TaskId = "x", TeammateId = "t", Task = "task" });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}

/// <summary>
/// TaskRuntime 持久化恢复的纵深防御测试
/// </summary>
public sealed class TaskRuntimeRecoveryTests : IDisposable
{
    private readonly InMemoryFileOperationService _fileOperationService;

    public TaskRuntimeRecoveryTests()
    {
        _fileOperationService = new InMemoryFileOperationService();
    }

    public void Dispose()
    {
        _fileOperationService.Dispose();
    }

    private TaskRuntime CreateRuntime()
    {
        return new TaskRuntime(new TaskRuntimeDeps(
            FileOperationService: _fileOperationService,
            PersistenceDirectory: "/test/runtime-tasks"));
    }

    [Fact]
    public async Task RecoverTasksAsync_CorruptFile_ShouldNotThrow()
    {
        var runtime = CreateRuntime();
        _fileOperationService.FileSystem.CreateDirectory("/test/runtime-tasks");
        _fileOperationService.FileSystem.WriteAllText("/test/runtime-tasks/runtime-tasks.json", "{not valid json");

        var act = async () => await runtime.RecoverTasksAsync();
        await act.Should().NotThrowAsync();
        runtime.Dispose();
    }

    [Fact]
    public async Task RecoverTasksAsync_CorruptFile_ShouldReturnEmpty()
    {
        var runtime = CreateRuntime();
        _fileOperationService.FileSystem.CreateDirectory("/test/runtime-tasks");
        _fileOperationService.FileSystem.WriteAllText("/test/runtime-tasks/runtime-tasks.json", "{not valid json");

        var recovered = await runtime.RecoverTasksAsync();
        recovered.Should().BeEmpty();
        runtime.Dispose();
    }

    [Fact]
    public async Task RecoverTasksAsync_CorruptFile_ShouldQuarantineCorruptFile()
    {
        var runtime = CreateRuntime();
        _fileOperationService.FileSystem.CreateDirectory("/test/runtime-tasks");
        var originalPath = "/test/runtime-tasks/runtime-tasks.json";
        _fileOperationService.FileSystem.WriteAllText(originalPath, "{not valid json");

        await runtime.RecoverTasksAsync();

        _fileOperationService.FileExists(originalPath).Should().BeFalse();
        _fileOperationService.FileSystem.EnumerateFiles("/test/runtime-tasks", "*.corrupt", SearchOption.TopDirectoryOnly)
            .Should().NotBeEmpty();
        runtime.Dispose();
    }

    [Fact]
    public async Task RecoverTasksAsync_ValidFile_ShouldRestoreTasks()
    {
        var runtime = CreateRuntime();
        var created = await runtime.CreateTaskAsync(new RuntimeTaskInput { Description = "persist", IsDurable = true });
        await runtime.PersistAsync();

        var recoveredRuntime = CreateRuntime();
        var recovered = await recoveredRuntime.RecoverTasksAsync();
        recovered.Should().Contain(t => t.Id == created.Data!.Id);
        recoveredRuntime.Dispose();
        runtime.Dispose();
    }
}
