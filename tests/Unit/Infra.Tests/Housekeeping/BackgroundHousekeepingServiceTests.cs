namespace Infra.Tests.Housekeeping;

using Infrastructure.Housekeeping;
using Infrastructure.Time;
using TestInMemFs = Testing.Common.Services.InMemoryFileSystem;

public sealed class BackgroundHousekeepingServiceTests
{
    [Fact]
    public async Task StartAsync_ShouldNotThrow()
    {
        var housekeeping = new Mock<IHousekeepingService>();
        housekeeping.Setup(h => h.RunAllCleanupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var fs = new TestInMemFs();
        var clock = new FakeClockService();
        var sut = new BackgroundHousekeepingService(housekeeping.Object, fs, clock);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.StartAsync(cts.Token);
        await sut.StopAsync(CancellationToken.None);

        sut.Should().NotBeNull();
    }

    [Fact]
    public async Task StopAsync_ShouldCompleteWithoutHanging()
    {
        var housekeeping = new Mock<IHousekeepingService>();
        housekeeping.Setup(h => h.RunAllCleanupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var fs = new TestInMemFs();
        var clock = new FakeClockService();
        var sut = new BackgroundHousekeepingService(housekeeping.Object, fs, clock);

        await sut.StartAsync(CancellationToken.None);
        await sut.StopAsync(CancellationToken.None);

        var ex = await Record.ExceptionAsync(() => sut.DisposeAsync().AsTask());
        ex.Should().BeNull();
    }

    [Fact]
    public async Task DisposeAsync_ShouldNotThrow()
    {
        var housekeeping = new Mock<IHousekeepingService>();
        var fs = new TestInMemFs();
        var clock = new FakeClockService();
        var sut = new BackgroundHousekeepingService(housekeeping.Object, fs, clock);

        await sut.DisposeAsync();
    }

    [Fact]
    public async Task RunAllCleanupAsync_ShouldBeCalledDirectly()
    {
        var housekeeping = new Mock<IHousekeepingService>();
        housekeeping.Setup(h => h.RunAllCleanupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        var fs = new TestInMemFs();
        var clock = new FakeClockService();

        var result = await housekeeping.Object.RunAllCleanupAsync();

        result.Should().Be(5);
        housekeeping.Verify(h => h.RunAllCleanupAsync(It.IsAny<CancellationToken>()), Times.Once());
    }
}
