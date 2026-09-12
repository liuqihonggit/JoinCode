
namespace Hands.Shell.Tests;

/// <summary>
/// BackgroundTaskStateTransitions 单元测试 — 验证后台任务状态转换规则
/// </summary>
public sealed class BackgroundTaskStateTransitionsTests
{
    [Fact]
    public void CanCancel_ShouldReturnTrue_ForPendingAndRunning()
    {
        BackgroundTaskStateTransitions.CanCancel(TaskExecutionStatus.Pending).Should().BeTrue();
        BackgroundTaskStateTransitions.CanCancel(TaskExecutionStatus.Running).Should().BeTrue();
    }

    [Fact]
    public void CanCancel_ShouldReturnFalse_ForTerminalStates()
    {
        BackgroundTaskStateTransitions.CanCancel(TaskExecutionStatus.Completed).Should().BeFalse();
        BackgroundTaskStateTransitions.CanCancel(TaskExecutionStatus.Failed).Should().BeFalse();
        BackgroundTaskStateTransitions.CanCancel(TaskExecutionStatus.Cancelled).Should().BeFalse();
    }

    [Fact]
    public void IsTerminal_ShouldReturnTrue_ForCompletedFailedCancelled()
    {
        BackgroundTaskStateTransitions.IsTerminal(TaskExecutionStatus.Completed).Should().BeTrue();
        BackgroundTaskStateTransitions.IsTerminal(TaskExecutionStatus.Failed).Should().BeTrue();
        BackgroundTaskStateTransitions.IsTerminal(TaskExecutionStatus.Cancelled).Should().BeTrue();
    }

    [Fact]
    public void IsTerminal_ShouldReturnFalse_ForPendingAndRunning()
    {
        BackgroundTaskStateTransitions.IsTerminal(TaskExecutionStatus.Pending).Should().BeFalse();
        BackgroundTaskStateTransitions.IsTerminal(TaskExecutionStatus.Running).Should().BeFalse();
    }

    [Fact]
    public void CanComplete_ShouldReturnTrue_OnlyForRunning()
    {
        BackgroundTaskStateTransitions.CanComplete(TaskExecutionStatus.Running).Should().BeTrue();
        BackgroundTaskStateTransitions.CanComplete(TaskExecutionStatus.Pending).Should().BeFalse();
        BackgroundTaskStateTransitions.CanComplete(TaskExecutionStatus.Completed).Should().BeFalse();
    }
}
