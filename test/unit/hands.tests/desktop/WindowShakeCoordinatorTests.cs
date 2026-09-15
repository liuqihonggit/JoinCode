namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// WindowShakeCoordinator 单元测试 — 验证 1 秒去抖行为
/// </summary>
public sealed class WindowShakeCoordinatorTests
{
    [Fact]
    public void TryAcquireShakeSlot_FirstCall_ReturnsTrue()
    {
        var coordinator = new WindowShakeCoordinator();
        coordinator.TryAcquireShakeSlot().Should().BeTrue();
    }

    [Fact]
    public void TryAcquireShakeSlot_SecondCallWithin1s_ReturnsFalse()
    {
        var coordinator = new WindowShakeCoordinator();
        coordinator.TryAcquireShakeSlot().Should().BeTrue();
        coordinator.TryAcquireShakeSlot().Should().BeFalse();
    }

    [Fact]
    public void TryAcquireShakeSlot_MultipleCallsWithin1s_OnlyFirstSucceeds()
    {
        var coordinator = new WindowShakeCoordinator();
        coordinator.TryAcquireShakeSlot().Should().BeTrue();
        coordinator.TryAcquireShakeSlot().Should().BeFalse();
        coordinator.TryAcquireShakeSlot().Should().BeFalse();
        coordinator.TryAcquireShakeSlot().Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquireShakeSlot_After1s_CanShakeAgain()
    {
        var coordinator = new WindowShakeCoordinator();
        coordinator.TryAcquireShakeSlot().Should().BeTrue();
        await Task.Delay(1100);
        coordinator.TryAcquireShakeSlot().Should().BeTrue();
    }

    [Fact]
    public void TryAcquireShakeSlot_DifferentInstances_Independent()
    {
        var c1 = new WindowShakeCoordinator();
        var c2 = new WindowShakeCoordinator();
        c1.TryAcquireShakeSlot().Should().BeTrue();
        c2.TryAcquireShakeSlot().Should().BeTrue();
    }
}
