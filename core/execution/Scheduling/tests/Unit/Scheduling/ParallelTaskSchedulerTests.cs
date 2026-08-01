
namespace Core.Tests.Scheduling;

public class ParallelTaskSchedulerTests
{
    private readonly ParallelTaskScheduler _scheduler;

    public ParallelTaskSchedulerTests()
    {
        _scheduler = new ParallelTaskScheduler();
    }

    [Fact]
    public void RegisterTask_ShouldAssignIdAndRaiseEvent()
    {
        TaskStatusChangedEventArgs? captured = null;
        _scheduler.TaskStatusChanged += (_, e) => captured = e;

        var task = _scheduler.RegisterTask("name", "desc", 2, TodoPriority.High);

        task.Id.Should().NotBeNullOrEmpty();
        task.Name.Should().Be("name");
        task.Description.Should().Be("desc");
        task.RequiredAgents.Should().Be(2);
        task.Priority.Should().Be(TodoPriority.High);
        task.Status.Should().Be(ScheduledTaskStatus.Pending);
        captured.Should().NotBeNull();
        captured!.Task.Id.Should().Be(task.Id);
        captured.OldStatus.Should().Be(ScheduledTaskStatus.Pending);
    }

    [Fact]
    public void GetAllTasks_ShouldReturnRegisteredTasks()
    {
        _scheduler.RegisterTask("a", "desc", 1, TodoPriority.Low);
        _scheduler.RegisterTask("b", "desc", 1, TodoPriority.Low);

        _scheduler.GetAllTasks().Should().HaveCount(2);
    }

    [Fact]
    public void GetTasksByStatus_ShouldFilterByStatus()
    {
        var t1 = _scheduler.RegisterTask("a", "desc", 1, TodoPriority.Low);
        _scheduler.RegisterTask("b", "desc", 1, TodoPriority.Low);

        _scheduler.UpdateTaskStatus(t1.Id, ScheduledTaskStatus.Completed);

        _scheduler.GetTasksByStatus(ScheduledTaskStatus.Completed).Should().HaveCount(1);
        _scheduler.GetTasksByStatus(ScheduledTaskStatus.Pending).Should().HaveCount(1);
    }

    [Fact]
    public void GetExecutableTasks_ShouldRespectDependencies()
    {
        var t1 = _scheduler.RegisterTask("a", "desc", 1, TodoPriority.Low);
        var t2 = _scheduler.RegisterTask("b", "desc", 1, TodoPriority.High, new List<string> { t1.Id });

        var executable = _scheduler.GetExecutableTasks();
        executable.Should().ContainSingle();
        executable[0].Id.Should().Be(t1.Id);

        _scheduler.UpdateTaskStatus(t1.Id, ScheduledTaskStatus.Completed);

        executable = _scheduler.GetExecutableTasks();
        executable.Should().ContainSingle();
        executable[0].Id.Should().Be(t2.Id);
    }

    [Fact]
    public void GetExecutableTasks_ShouldOrderByPriorityDescending()
    {
        var low = _scheduler.RegisterTask("low", "desc", 1, TodoPriority.Low);
        var high = _scheduler.RegisterTask("high", "desc", 1, TodoPriority.High);
        var critical = _scheduler.RegisterTask("critical", "desc", 1, TodoPriority.Critical);

        var executable = _scheduler.GetExecutableTasks();
        executable[0].Id.Should().Be(critical.Id);
        executable[1].Id.Should().Be(high.Id);
        executable[2].Id.Should().Be(low.Id);
    }

    [Fact]
    public void GetFirstWaveTasks_ShouldReturnTasksWithoutDependencies()
    {
        var t1 = _scheduler.RegisterTask("a", "desc", 1, TodoPriority.Low);
        _scheduler.RegisterTask("b", "desc", 1, TodoPriority.Low, new List<string> { t1.Id });

        var firstWave = _scheduler.GetFirstWaveTasks();
        firstWave.Should().ContainSingle();
        firstWave[0].Id.Should().Be(t1.Id);
    }

    [Fact]
    public void UpdateTaskStatus_WithMessage_ShouldUpdateAndRaiseEvent()
    {
        var task = _scheduler.RegisterTask("a", "desc", 1, TodoPriority.Low);

        TaskStatusChangedEventArgs? captured = null;
        _scheduler.TaskStatusChanged += (_, e) => captured = e;

        var result = _scheduler.UpdateTaskStatus(task.Id, ScheduledTaskStatus.InProgress, "started");

        result.Should().BeTrue();
        _scheduler.GetTasksByStatus(ScheduledTaskStatus.InProgress)[0].Status.Should().Be(ScheduledTaskStatus.InProgress);
        captured.Should().NotBeNull();
        captured!.OldStatus.Should().Be(ScheduledTaskStatus.Pending);
        captured.Message.Should().Be("started");
    }

    [Fact]
    public void UpdateTaskStatus_ToCompleted_ShouldSetCompletedAt()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var task = _scheduler.RegisterTask("a", "desc", 1, TodoPriority.Low);

        _scheduler.UpdateTaskStatus(task.Id, ScheduledTaskStatus.Completed);

        var completed = _scheduler.GetTasksByStatus(ScheduledTaskStatus.Completed)[0];
        completed.CompletedAt.Should().BeAfter(before);
    }

    [Fact]
    public void UpdateTaskStatus_NonExistent_ShouldReturnFalse()
    {
        _scheduler.UpdateTaskStatus("missing", ScheduledTaskStatus.Completed).Should().BeFalse();
    }

    [Fact]
    public void AreDependenciesMet_MissingDependencyTask_ShouldReturnFalse()
    {
        var task = _scheduler.RegisterTask("a", "desc", 1, TodoPriority.Low, new List<string> { "missing" });

        _scheduler.AreDependenciesMet(task.Id).Should().BeFalse();
    }

    [Fact]
    public void GetDependentTasks_ShouldReturnReverseDependencies()
    {
        var t1 = _scheduler.RegisterTask("a", "desc", 1, TodoPriority.Low);
        var t2 = _scheduler.RegisterTask("b", "desc", 1, TodoPriority.Low, new List<string> { t1.Id });
        var t3 = _scheduler.RegisterTask("c", "desc", 1, TodoPriority.Low, new List<string> { t1.Id });

        var dependents = _scheduler.GetDependentTasks(t1.Id);
        dependents.Should().HaveCount(2);
        dependents.Select(t => t.Id).Should().Contain(t2.Id).And.Contain(t3.Id);
    }

    [Fact]
    public void GetReport_ShouldCalculateCounts()
    {
        var t1 = _scheduler.RegisterTask("a", "desc", 1, TodoPriority.Low);
        var t2 = _scheduler.RegisterTask("b", "desc", 1, TodoPriority.Low);
        _scheduler.UpdateTaskStatus(t1.Id, ScheduledTaskStatus.Completed);
        _scheduler.UpdateTaskStatus(t2.Id, ScheduledTaskStatus.Failed);

        var report = _scheduler.GetReport();
        report.TotalTasks.Should().Be(2);
        report.CompletedCount.Should().Be(1);
        report.FailedCount.Should().Be(1);
        report.IsComplete.Should().BeTrue();
        report.CompletionPercentage.Should().Be(50);
    }

    [Fact]
    public void GetReport_WhenEmpty_ShouldReturnZeroPercentage()
    {
        var report = _scheduler.GetReport();
        report.TotalTasks.Should().Be(0);
        report.CompletionPercentage.Should().Be(0);
    }

    [Fact]
    public async Task WaitForTaskAsync_ShouldCompleteWhenTaskFinished()
    {
        var task = _scheduler.RegisterTask("a", "desc", 1, TodoPriority.Low);

        var waitTask = _scheduler.WaitForTaskAsync(task.Id, CancellationToken.None);
        _scheduler.UpdateTaskStatus(task.Id, ScheduledTaskStatus.Completed);

        await waitTask.WaitAsync(TimeSpan.FromSeconds(2));
        waitTask.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task WaitForAllAsync_ShouldCompleteWhenAllFinished()
    {
        var t1 = _scheduler.RegisterTask("a", "desc", 1, TodoPriority.Low);
        var t2 = _scheduler.RegisterTask("b", "desc", 1, TodoPriority.Low);

        var waitTask = _scheduler.WaitForAllAsync(CancellationToken.None);
        _scheduler.UpdateTaskStatus(t1.Id, ScheduledTaskStatus.Completed);
        _scheduler.UpdateTaskStatus(t2.Id, ScheduledTaskStatus.Failed);

        await waitTask.WaitAsync(TimeSpan.FromSeconds(2));
        waitTask.IsCompletedSuccessfully.Should().BeTrue();
    }
}
