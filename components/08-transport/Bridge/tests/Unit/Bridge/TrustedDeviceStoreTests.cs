
namespace Bridge.Tests;

/// <summary>
/// TrustedDeviceStore 单元测试
/// 测试受信任设备的添加、查询、撤销、移除和信任状态判断
/// </summary>
public sealed class TrustedDeviceStoreTests
{
    private static TrustedDeviceStore CreateSut() =>
        new(logger: null);

    private static TrustedDeviceEntry CreateEntry(
        string deviceId = "device-001",
        string deviceName = "Test Device",
        string publicKeyFingerprint = "sha256:abcdef1234567890",
        DeviceTrustLevel trustLevel = DeviceTrustLevel.Basic)
    {
        return new TrustedDeviceEntry
        {
            DeviceId = deviceId,
            DeviceName = deviceName,
            PublicKeyFingerprint = publicKeyFingerprint,
            TrustLevel = trustLevel
        };
    }

    [Fact]
    public async Task AddAsync_ShouldStoreDevice()
    {
        // Arrange
        var sut = CreateSut();
        var entry = CreateEntry();

        // Act
        var result = await sut.AddAsync(entry).ConfigureAwait(true);

        // Assert
        result.Should().BeSameAs(entry);
        result.DeviceId.Should().Be("device-001");
        result.DeviceName.Should().Be("Test Device");
    }

    [Fact]
    public async Task GetAsync_ShouldReturnDevice_WhenExists()
    {
        // Arrange
        var sut = CreateSut();
        var entry = CreateEntry();
        await sut.AddAsync(entry).ConfigureAwait(true);

        // Act
        var result = await sut.GetAsync("device-001").ConfigureAwait(true);

        // Assert
        result.Should().NotBeNull();
        result!.DeviceId.Should().Be("device-001");
        result.DeviceName.Should().Be("Test Device");
    }

    [Fact]
    public async Task GetAsync_ShouldReturnNull_WhenNotExists()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.GetAsync("nonexistent").ConfigureAwait(true);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task IsTrustedAsync_ShouldReturnTrue_WhenDeviceIsTrusted()
    {
        // Arrange
        var sut = CreateSut();
        var entry = CreateEntry(trustLevel: DeviceTrustLevel.Full);
        await sut.AddAsync(entry).ConfigureAwait(true);

        // Act
        var result = await sut.IsTrustedAsync("device-001").ConfigureAwait(true);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsTrustedAsync_ShouldReturnFalse_WhenDeviceIsRevoked()
    {
        // Arrange
        var sut = CreateSut();
        var entry = CreateEntry(trustLevel: DeviceTrustLevel.Full);
        await sut.AddAsync(entry).ConfigureAwait(true);
        await sut.RevokeAsync("device-001").ConfigureAwait(true);

        // Act
        var result = await sut.IsTrustedAsync("device-001").ConfigureAwait(true);

        // Assert
        result.Should().BeFalse("已撤销的设备不应被视为受信任");
    }

    [Fact]
    public async Task IsTrustedAsync_ShouldReturnFalse_WhenDeviceNotExists()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.IsTrustedAsync("nonexistent").ConfigureAwait(true);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeAsync_ShouldMarkDeviceAsRevoked()
    {
        // Arrange
        var sut = CreateSut();
        var entry = CreateEntry();
        await sut.AddAsync(entry).ConfigureAwait(true);

        // Act
        var result = await sut.RevokeAsync("device-001").ConfigureAwait(true);

        // Assert
        result.Should().BeTrue();
        var stored = await sut.GetAsync("device-001").ConfigureAwait(true);
        stored!.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task RemoveAsync_ShouldRemoveDevice()
    {
        // Arrange
        var sut = CreateSut();
        var entry = CreateEntry();
        await sut.AddAsync(entry).ConfigureAwait(true);

        // Act
        var removed = await sut.RemoveAsync("device-001").ConfigureAwait(true);

        // Assert
        removed.Should().BeTrue();
        var result = await sut.GetAsync("device-001").ConfigureAwait(true);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnAllDevices()
    {
        // Arrange
        var sut = CreateSut();
        await sut.AddAsync(CreateEntry(deviceId: "device-001")).ConfigureAwait(true);
        await sut.AddAsync(CreateEntry(deviceId: "device-002", deviceName: "Device 2")).ConfigureAwait(true);
        await sut.AddAsync(CreateEntry(deviceId: "device-003", deviceName: "Device 3")).ConfigureAwait(true);

        // Act
        var result = await sut.GetAllAsync().ConfigureAwait(true);

        // Assert
        result.Should().HaveCount(3);
        result.Select(d => d.DeviceId).Should().Contain(["device-001", "device-002", "device-003"]);
    }
}
