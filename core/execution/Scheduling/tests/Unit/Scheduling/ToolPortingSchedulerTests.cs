
namespace Core.Tests.Scheduling;

public class ToolPortingSchedulerTests
{
    private readonly ToolPortingScheduler _scheduler;

    public ToolPortingSchedulerTests()
    {
        _scheduler = new ToolPortingScheduler();
    }

    [Fact]
    public void InitializeTasks_ShouldCreateTwelveTasks()
    {
        _scheduler.InitializeTasks();

        _scheduler.GetAllTasks().Should().HaveCount(12);
    }

    [Fact]
    public void GetFirstWaveTasks_ShouldReturnNineTasks()
    {
        _scheduler.InitializeTasks();

        _scheduler.GetFirstWaveTasks().Should().HaveCount(9);
    }

    [Fact]
    public void StartTask_ShouldSetInProgress()
    {
        _scheduler.InitializeTasks();
        var first = _scheduler.GetFirstWaveTasks().First();

        var result = _scheduler.StartTask(first.Id);

        result.Should().BeTrue();
        _scheduler.GetTask(first.Id)!.Status.Should().Be(ScheduledTaskStatus.InProgress);
    }

    [Fact]
    public void CompleteTask_ShouldSetCompletedAndRaiseDependencyEvent()
    {
        _scheduler.InitializeTasks();
        var first = _scheduler.GetFirstWaveTasks().First();
        var dependents = _scheduler.GetAllTasks().Where(t => t.Dependencies.Contains(first.Id)).ToList();

        DependencyMetEventArgs? captured = null;
        _scheduler.OnDependencyMet += (_, e) => captured = e;

        _scheduler.StartTask(first.Id);
        var result = _scheduler.CompleteTask(first.Id, "done");

        result.Should().BeTrue();
        _scheduler.GetTask(first.Id)!.Status.Should().Be(ScheduledTaskStatus.Completed);
        if (dependents.Count > 0)
        {
            captured.Should().NotBeNull();
            captured!.CompletedDependencyId.Should().Be(first.Id);
        }
    }

    [Fact]
    public void CompleteTask_NonExistent_ShouldReturnFalse()
    {
        _scheduler.InitializeTasks();
        _scheduler.CompleteTask("missing", "done").Should().BeFalse();
    }

    [Fact]
    public void FailTask_ShouldSetFailed()
    {
        _scheduler.InitializeTasks();
        var first = _scheduler.GetFirstWaveTasks().First();

        _scheduler.StartTask(first.Id);
        _scheduler.FailTask(first.Id, "error").Should().BeTrue();
        _scheduler.GetTask(first.Id)!.Status.Should().Be(ScheduledTaskStatus.Failed);
    }

    [Fact]
    public void GetReport_ShouldReflectStatus()
    {
        _scheduler.InitializeTasks();
        var firstWave = _scheduler.GetFirstWaveTasks();
        foreach (var task in firstWave)
        {
            _scheduler.StartTask(task.Id);
            _scheduler.CompleteTask(task.Id);
        }

        var report = _scheduler.GetReport();
        report.TotalTasks.Should().Be(12);
        report.CompletedCount.Should().Be(9);
    }

    [Fact]
    public void GetTaskNameToIdMap_ShouldContainAllTasks()
    {
        _scheduler.InitializeTasks();
        var map = _scheduler.GetTaskNameToIdMap();

        map.Should().ContainKey("Task-01-Agent-Core");
        map.Count.Should().Be(12);
    }

    [Fact]
    public void TaskStatusChanged_ShouldBeObservable()
    {
        _scheduler.InitializeTasks();
        var triggered = false;
        _scheduler.TaskStatusChanged += (_, _) => triggered = true;

        var first = _scheduler.GetFirstWaveTasks().First();
        _scheduler.StartTask(first.Id);

        triggered.Should().BeTrue();
    }
}
