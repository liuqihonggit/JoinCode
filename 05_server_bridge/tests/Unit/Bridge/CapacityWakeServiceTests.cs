
namespace Bridge.Tests;

/// <summary>
/// CapacityWakeService 单元测试
/// 测试容量唤醒服务的默认容量、扩缩容、负载指标更新和事件触发
/// </summary>
public sealed class CapacityWakeServiceTests
{
    private static CapacityWakeService CreateSut(CapacityWakeOptions? options = null) =>
        new(options, logger: null);

    [Fact]
    public void Constructor_ShouldSetDefaultCapacity()
    {
        // Arrange & Act
        var sut = CreateSut();

        // Assert
        sut.GetCurrentCapacity().Should().Be(CapacityWakeOptions.DefaultMinInstances,
            "默认容量应等于 MinInstances");
    }

    [Fact]
    public async Task ScaleUpAsync_ShouldIncreaseCapacity()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.ScaleUpAsync().ConfigureAwait(true);

        // Assert
        sut.GetCurrentCapacity().Should().Be(CapacityWakeOptions.DefaultMinInstances + 1,
            "扩容后容量应增加 1");
    }

    [Fact]
    public async Task ScaleDownAsync_ShouldDecreaseCapacity()
    {
        // Arrange
        var options = new CapacityWakeOptions { MinInstances = 1, MaxInstances = 5 };
        var sut = CreateSut(options);
        await sut.ScaleUpAsync().ConfigureAwait(true); // 1 -> 2

        // Act
        await sut.ScaleDownAsync().ConfigureAwait(true); // 2 -> 1

        // Assert
        sut.GetCurrentCapacity().Should().Be(1, "缩容后容量应减少 1");
    }

    [Fact]
    public async Task ScaleUpAsync_ShouldNotExceedMaxCapacity()
    {
        // Arrange
        var options = new CapacityWakeOptions { MinInstances = 1, MaxInstances = 2 };
        var sut = CreateSut(options);
        await sut.ScaleUpAsync().ConfigureAwait(true); // 1 -> 2

        // Act
        await sut.ScaleUpAsync().ConfigureAwait(true); // 已达上限，不应再扩

        // Assert
        sut.GetCurrentCapacity().Should().Be(2, "不应超过 MaxInstances");
    }

    [Fact]
    public async Task ScaleDownAsync_ShouldNotGoBelowMinCapacity()
    {
        // Arrange
        var options = new CapacityWakeOptions { MinInstances = 1, MaxInstances = 5 };
        var sut = CreateSut(options);

        // Act
        await sut.ScaleDownAsync().ConfigureAwait(true); // 已达下限，不应再缩

        // Assert
        sut.GetCurrentCapacity().Should().Be(1, "不应低于 MinInstances");
    }

    [Fact]
    public void UpdateLoadMetrics_ShouldUpdateMetrics()
    {
        // Arrange
        var sut = CreateSut();
        var metrics = new LoadMetrics
        {
            ActiveConnections = 50,
            PendingRequests = 10,
            CpuUsagePercent = 65.0,
            MemoryUsagePercent = 70.0
        };

        // Act
        sut.UpdateLoadMetrics(metrics);

        // Assert
        var current = sut.GetLoadMetrics();
        current.ActiveConnections.Should().Be(50);
        current.PendingRequests.Should().Be(10);
        current.CpuUsagePercent.Should().Be(65.0);
        current.MemoryUsagePercent.Should().Be(70.0);
        current.CompositeLoadPercent.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CapacityChanged_ShouldFire_WhenScaling()
    {
        // Arrange
        var sut = CreateSut();
        CapacityChangedEventArgs? eventArgs = null;
        sut.CapacityChanged += (_, e) => eventArgs = e;

        // Act
        await sut.ScaleUpAsync().ConfigureAwait(true);

        // Assert
        eventArgs.Should().NotBeNull("扩容时应触发 CapacityChanged 事件");
        eventArgs!.OldInstanceCount.Should().Be(CapacityWakeOptions.DefaultMinInstances);
        eventArgs!.NewInstanceCount.Should().Be(CapacityWakeOptions.DefaultMinInstances + 1);
        eventArgs!.LoadMetrics.Should().NotBeNull();
    }
}
