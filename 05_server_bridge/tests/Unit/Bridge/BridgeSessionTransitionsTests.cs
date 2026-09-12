
namespace Bridge.Tests;

/// <summary>
/// BridgeSessionTransitions 单元测试 — 验证会话状态转换规则集中定义的正确性
/// </summary>
public sealed class BridgeSessionTransitionsTests
{
    [Fact]
    public void IsTerminal_ShouldReturnTrue_OnlyForClosed()
    {
        BridgeSessionTransitions.IsTerminal(BridgeSessionStatus.Closed).Should().BeTrue();
        BridgeSessionTransitions.IsTerminal(BridgeSessionStatus.Active).Should().BeFalse();
        BridgeSessionTransitions.IsTerminal(BridgeSessionStatus.Idle).Should().BeFalse();
        BridgeSessionTransitions.IsTerminal(BridgeSessionStatus.Suspended).Should().BeFalse();
    }

    [Fact]
    public void CanSuspend_ShouldReturnTrue_ForActiveAndIdle()
    {
        BridgeSessionTransitions.CanSuspend(BridgeSessionStatus.Active).Should().BeTrue();
        BridgeSessionTransitions.CanSuspend(BridgeSessionStatus.Idle).Should().BeTrue();
    }

    [Fact]
    public void CanSuspend_ShouldReturnFalse_ForSuspendedAndClosed()
    {
        BridgeSessionTransitions.CanSuspend(BridgeSessionStatus.Suspended).Should().BeFalse();
        BridgeSessionTransitions.CanSuspend(BridgeSessionStatus.Closed).Should().BeFalse();
    }

    [Fact]
    public void CanResume_ShouldReturnTrue_OnlyForSuspended()
    {
        BridgeSessionTransitions.CanResume(BridgeSessionStatus.Suspended).Should().BeTrue();
        BridgeSessionTransitions.CanResume(BridgeSessionStatus.Active).Should().BeFalse();
        BridgeSessionTransitions.CanResume(BridgeSessionStatus.Idle).Should().BeFalse();
        BridgeSessionTransitions.CanResume(BridgeSessionStatus.Closed).Should().BeFalse();
    }

    [Fact]
    public void CanKeepAlive_ShouldReturnTrue_ForAllNonTerminalStates()
    {
        BridgeSessionTransitions.CanKeepAlive(BridgeSessionStatus.Active).Should().BeTrue();
        BridgeSessionTransitions.CanKeepAlive(BridgeSessionStatus.Idle).Should().BeTrue();
        BridgeSessionTransitions.CanKeepAlive(BridgeSessionStatus.Suspended).Should().BeTrue();
    }

    [Fact]
    public void CanKeepAlive_ShouldReturnFalse_ForClosed()
    {
        BridgeSessionTransitions.CanKeepAlive(BridgeSessionStatus.Closed).Should().BeFalse();
    }

    [Fact]
    public void CanStop_ShouldReturnTrue_ForAllNonTerminalStates()
    {
        BridgeSessionTransitions.CanStop(BridgeSessionStatus.Active).Should().BeTrue();
        BridgeSessionTransitions.CanStop(BridgeSessionStatus.Idle).Should().BeTrue();
        BridgeSessionTransitions.CanStop(BridgeSessionStatus.Suspended).Should().BeTrue();
    }

    [Fact]
    public void CanStop_ShouldReturnFalse_ForClosed()
    {
        BridgeSessionTransitions.CanStop(BridgeSessionStatus.Closed).Should().BeFalse();
    }

    [Fact]
    public void CanRestore_ShouldReturnFalse_ForClosed()
    {
        BridgeSessionTransitions.CanRestore(BridgeSessionStatus.Closed).Should().BeFalse();
    }

    [Fact]
    public void CanRestore_ShouldReturnTrue_ForAllNonTerminalStates()
    {
        BridgeSessionTransitions.CanRestore(BridgeSessionStatus.Active).Should().BeTrue();
        BridgeSessionTransitions.CanRestore(BridgeSessionStatus.Idle).Should().BeTrue();
        BridgeSessionTransitions.CanRestore(BridgeSessionStatus.Suspended).Should().BeTrue();
    }
}
