namespace Mcp.Tests.Management;

/// <summary>
/// ShakeWindowToolHandlers 单元测试 — 验证震动/闪烁/进程信息/去抖/配置开关
/// </summary>
public sealed class ShakeWindowToolHandlersTests {
    private sealed class FakeCoordinator(bool enabled = true, bool canShake = true) : IWindowShakeCoordinator {
        public bool IsShakeEnabled { get; } = enabled;
        public bool TryAcquireShakeSlot() => canShake;
    }

    private sealed class FakeShakeService : IWindowShakeService {
        public int ShakeWindowCalls;
        public int FlashTaskbarCalls;
        public ShakeResult? ShakeResult { get; set; }

        public Task<ShakeResult?> ShakeWindowAsync(CancellationToken cancellationToken = default) {
            ShakeWindowCalls++;
            return Task.FromResult(ShakeResult);
        }

        public Task<ShakeResult?> FlashTaskbarAsync(CancellationToken cancellationToken = default) {
            FlashTaskbarCalls++;
            return Task.FromResult<ShakeResult?>(new ShakeResult("flash", "flash", "flash"));
        }

        public string GetWindowInfo() => "fake-window-info";
    }

    [Fact]
    public async Task ShakeWindowAsync_ShakeDisabled_ReturnsDisabledMessage() {
        var coordinator = new FakeCoordinator(enabled: false);
        var handler = new ShakeWindowToolHandlers(coordinator);
        var result = await handler.ShakeWindowAsync(null, default);
        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("已关闭");
    }

    [Fact]
    public async Task ShakeWindowAsync_ShakeEnabled_FirstCall_ReturnsSuccess() {
        var coordinator = new FakeCoordinator(enabled: true, canShake: true);
        var shakeService = new FakeShakeService { ShakeResult = new ShakeResult("test", "test", "test") };
        var handler = new ShakeWindowToolHandlers(coordinator, shakeService);
        var result = await handler.ShakeWindowAsync("test", default);
        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("已震动");
        shakeService.ShakeWindowCalls.Should().Be(1);
    }

    [Fact]
    public async Task ShakeWindowAsync_SecondCallWithin1s_ReturnsSkipped() {
        var coordinator = new FakeCoordinator(enabled: true, canShake: false);
        var handler = new ShakeWindowToolHandlers(coordinator);
        var result = await handler.ShakeWindowAsync(null, default);
        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("跳过");
    }

    [Fact]
    public async Task ShakeWindowAsync_NoShakeService_ReturnsSuccess() {
        var coordinator = new FakeCoordinator(enabled: true, canShake: true);
        var handler = new ShakeWindowToolHandlers(coordinator, shakeService: null);
        var result = await handler.ShakeWindowAsync(null, default);
        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("已震动");
    }

    [Fact]
    public async Task FlashTaskbarAsync_ShakeDisabled_ReturnsDisabledMessage() {
        var coordinator = new FakeCoordinator(enabled: false);
        var handler = new ShakeWindowToolHandlers(coordinator);
        var result = await handler.FlashTaskbarAsync(default);
        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("已关闭");
    }

    [Fact]
    public async Task FlashTaskbarAsync_Normal_ReturnsSuccess() {
        var coordinator = new FakeCoordinator(enabled: true, canShake: true);
        var shakeService = new FakeShakeService();
        var handler = new ShakeWindowToolHandlers(coordinator, shakeService);
        var result = await handler.FlashTaskbarAsync(default);
        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("已闪烁");
        shakeService.FlashTaskbarCalls.Should().Be(1);
    }

    [Fact]
    public async Task GetProcessInfoAsync_ReturnsMachineAndPid() {
        var coordinator = new FakeCoordinator();
        var handler = new ShakeWindowToolHandlers(coordinator);
        var result = await handler.GetProcessInfoAsync(default);
        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("电脑名:");
        text.Should().Contain("进程PID:");
        text.Should().Contain(Environment.MachineName);
        text.Should().Contain(Environment.ProcessId.ToString());
    }
}
