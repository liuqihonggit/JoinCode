namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// WindowShakeCoordinator 单元测试 — 验证 1 秒去抖行为 + 配置开关联动
/// </summary>
public sealed class WindowShakeCoordinatorTests {
    [Fact]
    public void TryAcquireShakeSlot_FirstCall_ReturnsTrue() {
        using var coordinator = new WindowShakeCoordinator();
        coordinator.TryAcquireShakeSlot().Should().BeTrue();
    }

    [Fact]
    public void TryAcquireShakeSlot_SecondCallWithin1s_ReturnsFalse() {
        using var coordinator = new WindowShakeCoordinator();
        coordinator.TryAcquireShakeSlot().Should().BeTrue();
        coordinator.TryAcquireShakeSlot().Should().BeFalse();
    }

    [Fact]
    public void TryAcquireShakeSlot_MultipleCallsWithin1s_OnlyFirstSucceeds() {
        using var coordinator = new WindowShakeCoordinator();
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
    public void TryAcquireShakeSlot_DifferentInstances_Independent() {
        using var c1 = new WindowShakeCoordinator();
        using var c2 = new WindowShakeCoordinator();
        c1.TryAcquireShakeSlot().Should().BeTrue();
        c2.TryAcquireShakeSlot().Should().BeTrue();
    }

    [Fact]
    public void IsShakeEnabled_NoProvider_ReturnsTrue() {
        using var coordinator = new WindowShakeCoordinator();
        coordinator.IsShakeEnabled.Should().BeTrue();
    }

    [Fact]
    public void IsShakeEnabled_ProviderReturnsTrue_ReturnsTrue() {
        using var coordinator = new WindowShakeCoordinator(shakeEnabledProvider: () => true);
        coordinator.IsShakeEnabled.Should().BeTrue();
    }

    [Fact]
    public void IsShakeEnabled_ProviderReturnsFalse_ReturnsFalse() {
        using var coordinator = new WindowShakeCoordinator(shakeEnabledProvider: () => false);
        coordinator.IsShakeEnabled.Should().BeFalse();
    }

    [Fact]
    public void IsShakeEnabled_ProviderReturnsNull_FallsBackToConfigCache() {
        using var coordinator = new WindowShakeCoordinator(shakeEnabledProvider: () => null);
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
    public void IsShakeEnabled_SettingChanged_UpdatesCache() {
        var configMock = new Mock<IConfigurationService>();
        configMock.Setup(x => x.GetAsync("windowShakeEnabled", It.IsAny<CancellationToken>()))
            .ReturnsAsync("true");

        using var coordinator = new WindowShakeCoordinator(configService: configMock.Object);
        coordinator.IsShakeEnabled.Should().BeTrue();

        configMock.Raise(x => x.SettingChanged += null,
            new SettingChangeEventArgs { Key = "windowShakeEnabled", NewValue = "false" });

        coordinator.IsShakeEnabled.Should().BeFalse();

        configMock.Raise(x => x.SettingChanged += null,
            new SettingChangeEventArgs { Key = "windowShakeEnabled", NewValue = "true" });

        coordinator.IsShakeEnabled.Should().BeTrue();
    }

    [Fact]
    public void IsShakeEnabled_ProviderOverridesConfig() {
        var configMock = new Mock<IConfigurationService>();
        configMock.Setup(x => x.GetAsync("windowShakeEnabled", It.IsAny<CancellationToken>()))
            .ReturnsAsync("false");

        using var coordinator = new WindowShakeCoordinator(
            configService: configMock.Object,
            shakeEnabledProvider: () => true);

        coordinator.IsShakeEnabled.Should().BeTrue();
    }

    [Fact]
    public void Dispose_UnsubscribesSettingChanged() {
        var configMock = new Mock<IConfigurationService>();
        configMock.Setup(x => x.GetAsync("windowShakeEnabled", It.IsAny<CancellationToken>()))
            .ReturnsAsync("true");

        using var coordinator = new WindowShakeCoordinator(configService: configMock.Object);
        coordinator.DisposeSafe();

        configMock.VerifyRemove(x => x.SettingChanged -= It.IsAny<EventHandler<SettingChangeEventArgs>>(), Times.Once);
    }
}