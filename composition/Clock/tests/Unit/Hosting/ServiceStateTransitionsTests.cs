namespace Clock.Tests.Unit.Hosting;

/// <summary>
/// ServiceStateTransitions 单元测试 — 验证服务状态转换规则正确性
/// </summary>
public sealed class ServiceStateTransitionsTests
{
    [Fact]
    public void CanTransitionTo_ShouldAllowStoppedToStarting()
    {
        ServiceStateTransitions.CanTransitionTo(ServiceStatus.Stopped, ServiceStatus.Starting).Should().BeTrue();
    }

    [Fact]
    public void CanTransitionTo_ShouldAllowStartingToRunningFailed()
    {
        ServiceStateTransitions.CanTransitionTo(ServiceStatus.Starting, ServiceStatus.Running).Should().BeTrue();
        ServiceStateTransitions.CanTransitionTo(ServiceStatus.Starting, ServiceStatus.Failed).Should().BeTrue();
    }

    [Fact]
    public void CanTransitionTo_ShouldAllowRunningToStoppingFailed()
    {
        ServiceStateTransitions.CanTransitionTo(ServiceStatus.Running, ServiceStatus.Stopping).Should().BeTrue();
        ServiceStateTransitions.CanTransitionTo(ServiceStatus.Running, ServiceStatus.Failed).Should().BeTrue();
    }

    [Fact]
    public void CanTransitionTo_ShouldAllowStoppingToStoppedFailed()
    {
        ServiceStateTransitions.CanTransitionTo(ServiceStatus.Stopping, ServiceStatus.Stopped).Should().BeTrue();
        ServiceStateTransitions.CanTransitionTo(ServiceStatus.Stopping, ServiceStatus.Failed).Should().BeTrue();
    }

    [Fact]
    public void CanTransitionTo_ShouldAllowFailedToStartingStopped()
    {
        ServiceStateTransitions.CanTransitionTo(ServiceStatus.Failed, ServiceStatus.Starting).Should().BeTrue();
        ServiceStateTransitions.CanTransitionTo(ServiceStatus.Failed, ServiceStatus.Stopped).Should().BeTrue();
    }

    [Fact]
    public void CanTransitionTo_ShouldDenyStoppedToRunningOrStoppingOrFailed()
    {
        ServiceStateTransitions.CanTransitionTo(ServiceStatus.Stopped, ServiceStatus.Running).Should().BeFalse();
        ServiceStateTransitions.CanTransitionTo(ServiceStatus.Stopped, ServiceStatus.Stopping).Should().BeFalse();
        ServiceStateTransitions.CanTransitionTo(ServiceStatus.Stopped, ServiceStatus.Failed).Should().BeFalse();
    }

    [Fact]
    public void CanTransitionTo_ShouldDenyRunningToStartingOrStopped()
    {
        ServiceStateTransitions.CanTransitionTo(ServiceStatus.Running, ServiceStatus.Starting).Should().BeFalse();
        ServiceStateTransitions.CanTransitionTo(ServiceStatus.Running, ServiceStatus.Stopped).Should().BeFalse();
    }

    [Fact]
    public void CanTransitionTo_ShouldAllowSelfLoop()
    {
        foreach (var state in Enum.GetValues<ServiceStatus>())
        {
            ServiceStateTransitions.CanTransitionTo(state, state).Should().BeTrue();
        }
    }

    [Fact]
    public void IsTerminal_ShouldReturnTrue_OnlyForStopped()
    {
        ServiceStateTransitions.IsTerminal(ServiceStatus.Stopped).Should().BeTrue();
        ServiceStateTransitions.IsTerminal(ServiceStatus.Starting).Should().BeFalse();
        ServiceStateTransitions.IsTerminal(ServiceStatus.Running).Should().BeFalse();
        ServiceStateTransitions.IsTerminal(ServiceStatus.Stopping).Should().BeFalse();
        ServiceStateTransitions.IsTerminal(ServiceStatus.Failed).Should().BeFalse();
    }
}
