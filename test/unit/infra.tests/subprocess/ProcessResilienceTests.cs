namespace Infra.Tests.Subprocess;

public sealed class ProcessHealthMonitorTests
{
    [Fact]
    public void InitialState_IsHealthy()
    {
        var process = CreateMockProcess(false);
        var monitor = new ProcessHealthMonitor(process, new HealthCheckConfig { Interval = TimeSpan.FromHours(1) });

        monitor.IsHealthy.Should().BeTrue();
        monitor.ConsecutiveFailures.Should().Be(0);

        monitor.Dispose();
    }

    [Fact]
    public async Task DetectsExitedProcess_BecomesUnhealthy()
    {
        var process = CreateMockProcess(true);
        var config = new HealthCheckConfig
        {
            Interval = TimeSpan.FromMilliseconds(50),
            FailureThreshold = 1,
        };

        using var monitor = new ProcessHealthMonitor(process, config);
        var unhealthyEvent = new TaskCompletionSource<ProcessUnhealthyEventArgs>();

        monitor.Unhealthy += (_, e) => unhealthyEvent.TrySetResult(e);

        var evt = await unhealthyEvent.Task.WaitAsync(TimeSpan.FromSeconds(5));

        monitor.IsHealthy.Should().BeFalse();
        evt.ProcessId.Should().Be(123);
        evt.Reason.Should().Be("Process has exited");
    }

    [Fact]
    public async Task HealthyProcess_StaysHealthy()
    {
        var process = CreateMockProcess(false);
        var config = new HealthCheckConfig
        {
            Interval = TimeSpan.FromMilliseconds(50),
        };

        using var monitor = new ProcessHealthMonitor(process, config);

        await Task.Delay(200);

        monitor.IsHealthy.Should().BeTrue();
        monitor.ConsecutiveFailures.Should().Be(0);
    }

    private static IInteractiveProcess CreateMockProcess(bool hasExited)
    {
        var mock = new Mock<IInteractiveProcess>();
        mock.SetupGet(p => p.Id).Returns(123);
        mock.SetupGet(p => p.HasExited).Returns(hasExited);
        mock.SetupGet(p => p.StandardInput).Returns(new StreamWriter(Stream.Null));
        mock.SetupGet(p => p.StandardOutput).Returns(new StreamReader(Stream.Null));
        mock.Setup(p => p.WaitForExitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mock.Setup(p => p.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return mock.Object;
    }
}

public sealed class ProcessRestartManagerTests
{
    [Fact]
    public void InitialState_CanRestart()
    {
        var manager = new ProcessRestartManager(3);
        manager.RestartCount.Should().Be(0);
        manager.CanRestart.Should().BeTrue();
    }

    [Fact]
    public void MaxRestarts_Reached_CannotRestart()
    {
        var manager = new ProcessRestartManager(0);
        manager.CanRestart.Should().BeFalse();
    }

    [Fact]
    public async Task RestartAsync_IncrementsCount()
    {
        var manager = new ProcessRestartManager(3);
        var oldProcess = CreateMockProcess(1);
        var newProcess = CreateMockProcess(2);

        var result = await manager.RestartAsync(
            oldProcess,
            _ => Task.FromResult(newProcess),
            CancellationToken.None);

        result.Should().BeSameAs(newProcess);
        manager.RestartCount.Should().Be(1);
        manager.CanRestart.Should().BeTrue();
    }

    [Fact]
    public async Task RestartAsync_ExceedsMax_Throws()
    {
        var manager = new ProcessRestartManager(1);
        var oldProcess = CreateMockProcess(1);
        var newProcess = CreateMockProcess(2);

        await manager.RestartAsync(oldProcess, _ => Task.FromResult(newProcess), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.RestartAsync(newProcess, _ => Task.FromResult(CreateMockProcess(3)), CancellationToken.None));
    }

    [Fact]
    public void Reset_ClearsCount()
    {
        var manager = new ProcessRestartManager(3);
        manager.Reset();
        manager.RestartCount.Should().Be(0);
        manager.CanRestart.Should().BeTrue();
    }

    private static IInteractiveProcess CreateMockProcess(int pid)
    {
        var mock = new Mock<IInteractiveProcess>();
        mock.SetupGet(p => p.Id).Returns(pid);
        mock.SetupGet(p => p.HasExited).Returns(false);
        mock.SetupGet(p => p.StandardInput).Returns(new StreamWriter(Stream.Null));
        mock.SetupGet(p => p.StandardOutput).Returns(new StreamReader(Stream.Null));
        mock.Setup(p => p.WaitForExitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mock.Setup(p => p.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return mock.Object;
    }
}
