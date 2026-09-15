namespace Mcp.Tests.Management;

/// <summary>
/// ShakeWindowToolHandlers 单元测试 — 验证震动/闪烁/进程信息/去抖/配置开关
/// </summary>
public sealed class ShakeWindowToolHandlersTests
{
    private static Mock<IWindowShakeCoordinator> CreateCoordinator(bool enabled = true, bool canShake = true)
    {
        var mock = new Mock<IWindowShakeCoordinator>();
        mock.SetupGet(x => x.IsShakeEnabled).Returns(enabled);
        mock.Setup(x => x.TryAcquireShakeSlot()).Returns(canShake);
        return mock;
    }

    [Fact]
    public async Task ShakeWindowAsync_ShakeDisabled_ReturnsDisabledMessage()
    {
        var coordinator = CreateCoordinator(enabled: false);
        var handler = new ShakeWindowToolHandlers(coordinator.Object);
        var result = await handler.ShakeWindowAsync(null, default);
        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("已关闭");
    }

    [Fact]
    public async Task ShakeWindowAsync_ShakeEnabled_FirstCall_ReturnsSuccess()
    {
        var coordinator = CreateCoordinator(enabled: true, canShake: true);
        var shakeService = new Mock<IWindowShakeService>();
        var handler = new ShakeWindowToolHandlers(coordinator.Object, shakeService.Object);
        var result = await handler.ShakeWindowAsync("test", default);
        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("已震动");
        shakeService.Verify(x => x.ShakeWindowAsync(default), Times.Once);
    }

    [Fact]
    public async Task ShakeWindowAsync_SecondCallWithin1s_ReturnsSkipped()
    {
        var coordinator = CreateCoordinator(enabled: true, canShake: false);
        var handler = new ShakeWindowToolHandlers(coordinator.Object);
        var result = await handler.ShakeWindowAsync(null, default);
        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("跳过");
    }

    [Fact]
    public async Task ShakeWindowAsync_NoShakeService_ReturnsSuccess()
    {
        var coordinator = CreateCoordinator(enabled: true, canShake: true);
        var handler = new ShakeWindowToolHandlers(coordinator.Object, shakeService: null);
        var result = await handler.ShakeWindowAsync(null, default);
        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("已震动");
    }

    [Fact]
    public async Task FlashTaskbarAsync_ShakeDisabled_ReturnsDisabledMessage()
    {
        var coordinator = CreateCoordinator(enabled: false);
        var handler = new ShakeWindowToolHandlers(coordinator.Object);
        var result = await handler.FlashTaskbarAsync(default);
        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("已关闭");
    }

    [Fact]
    public async Task FlashTaskbarAsync_Normal_ReturnsSuccess()
    {
        var coordinator = CreateCoordinator(enabled: true, canShake: true);
        var shakeService = new Mock<IWindowShakeService>();
        var handler = new ShakeWindowToolHandlers(coordinator.Object, shakeService.Object);
        var result = await handler.FlashTaskbarAsync(default);
        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("已闪烁");
        shakeService.Verify(x => x.FlashTaskbarAsync(default), Times.Once);
    }

    [Fact]
    public async Task GetProcessInfoAsync_ReturnsMachineAndPid()
    {
        var coordinator = CreateCoordinator();
        var handler = new ShakeWindowToolHandlers(coordinator.Object);
        var result = await handler.GetProcessInfoAsync(default);
        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("电脑名:");
        text.Should().Contain("进程PID:");
        text.Should().Contain(Environment.MachineName);
        text.Should().Contain(Environment.ProcessId.ToString());
    }
}
