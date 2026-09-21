namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// WindowShakeCoordinator 单元测试 — 验证 1 秒去抖行为 + 配置开关联动
/// </summary>
public sealed class WindowShakeCoordinatorTests {
    [Fact]
    public async Task TryAcquireShakeSlot_FirstCall_ReturnsTrue() {
        await using var coordinator = new WindowShakeCoordinator();
        coordinator.TryAcquireShakeSlot().Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireShakeSlot_SecondCallWithin1s_ReturnsFalse() {
        await using var coordinator = new WindowShakeCoordinator();
        coordinator.TryAcquireShakeSlot().Should().BeTrue();
        coordinator.TryAcquireShakeSlot().Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquireShakeSlot_MultipleCallsWithin1s_OnlyFirstSucceeds() {
        await using var coordinator = new WindowShakeCoordinator();
        coordinator.TryAcquireShakeSlot().Should().BeTrue();
        coordinator.TryAcquireShakeSlot().Should().BeFalse();
        coordinator.TryAcquireShakeSlot().Should().BeFalse();
        coordinator.TryAcquireShakeSlot().Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquireShakeSlot_After1s_CanShakeAgain() {
        await using var coordinator = new WindowShakeCoordinator();
        coordinator.TryAcquireShakeSlot().Should().BeTrue();
        await Task.Delay(1100);
        coordinator.TryAcquireShakeSlot().Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireShakeSlot_DifferentInstances_Independent() {
        await using var c1 = new WindowShakeCoordinator();
        await using var c2 = new WindowShakeCoordinator();
        c1.TryAcquireShakeSlot().Should().BeTrue();
        c2.TryAcquireShakeSlot().Should().BeTrue();
    }

    [Fact]
    public async Task IsShakeEnabled_NoProvider_ReturnsTrue() {
        await using var coordinator = new WindowShakeCoordinator();
        coordinator.IsShakeEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task IsShakeEnabled_ProviderReturnsTrue_ReturnsTrue() {
        await using var coordinator = new WindowShakeCoordinator(shakeEnabledProvider: () => true);
        coordinator.IsShakeEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task IsShakeEnabled_ProviderReturnsFalse_ReturnsFalse() {
        await using var coordinator = new WindowShakeCoordinator(shakeEnabledProvider: () => false);
        coordinator.IsShakeEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task IsShakeEnabled_ProviderReturnsNull_FallsBackToConfigCache() {
        await using var coordinator = new WindowShakeCoordinator(shakeEnabledProvider: () => null);
        coordinator.IsShakeEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task IsShakeEnabled_ConfigServiceReturnsFalse_ReturnsFalse() {
        var configMock = new Mock<IConfigurationService>();
        configMock.Setup(x => x.GetAsync("windowShakeEnabled", It.IsAny<CancellationToken>()))
            .ReturnsAsync("false");

        await using var coordinator = new WindowShakeCoordinator(configService: configMock.Object);
        await Task.Delay(100);

        coordinator.IsShakeEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task IsShakeEnabled_ConfigServiceReturnsTrue_ReturnsTrue() {
        var configMock = new Mock<IConfigurationService>();
        configMock.Setup(x => x.GetAsync("windowShakeEnabled", It.IsAny<CancellationToken>()))
            .ReturnsAsync("true");

        await using var coordinator = new WindowShakeCoordinator(configService: configMock.Object);
        await Task.Delay(100);

        coordinator.IsShakeEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task IsShakeEnabled_ConfigServiceReturnsNull_ReturnsTrue() {
        var configMock = new Mock<IConfigurationService>();
        configMock.Setup(x => x.GetAsync("windowShakeEnabled", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        await using var coordinator = new WindowShakeCoordinator(configService: configMock.Object);
        await Task.Delay(100);

        coordinator.IsShakeEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task IsShakeEnabled_SettingChanged_UpdatesCache() {
        var configMock = new Mock<IConfigurationService>();
        configMock.Setup(x => x.GetAsync("windowShakeEnabled", It.IsAny<CancellationToken>()))
            .ReturnsAsync("true");
        await using var coordinator = new WindowShakeCoordinator(configService: configMock.Object);
        coordinator.IsShakeEnabled.Should().BeTrue();

        configMock.Raise(x => x.SettingChanged += null,
            new SettingChangeEventArgs { Key = "windowShakeEnabled", NewValue = "false" });

        coordinator.IsShakeEnabled.Should().BeFalse();

        configMock.Raise(x => x.SettingChanged += null,
            new SettingChangeEventArgs { Key = "windowShakeEnabled", NewValue = "true" });

        coordinator.IsShakeEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task IsShakeEnabled_ProviderOverridesConfig() {
        var configMock = new Mock<IConfigurationService>();
        configMock.Setup(x => x.GetAsync("windowShakeEnabled", It.IsAny<CancellationToken>()))
            .ReturnsAsync("false");
        await using var coordinator = new WindowShakeCoordinator(
            configService: configMock.Object,
            shakeEnabledProvider: () => true);

        coordinator.IsShakeEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Dispose_UnsubscribesSettingChanged() {
        var configMock = new Mock<IConfigurationService>();
        configMock.Setup(x => x.GetAsync("windowShakeEnabled", It.IsAny<CancellationToken>()))
            .ReturnsAsync("true");
        await using var coordinator = new WindowShakeCoordinator(configService: configMock.Object);
        coordinator.DisposeSafe();

        configMock.VerifyRemove(x => x.SettingChanged -= It.IsAny<EventHandler<SettingChangeEventArgs>>(), Times.Once);
    }
}