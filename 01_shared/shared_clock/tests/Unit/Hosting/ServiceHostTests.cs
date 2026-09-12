
namespace Clock.Tests.Unit.Hosting;

public sealed class ServiceHostTests
{
    private static IWorkflowService CreateService(string name, bool throwOnStart = false, bool throwOnStop = false)
    {
        var service = new Mock<IWorkflowService>();
        service.Setup(s => s.ServiceName).Returns(name);
        service.Setup(s => s.Status).Returns(ServiceStatus.Stopped);
        if (throwOnStart)
        {
            service.Setup(s => s.StartAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("start fail"));
        }
        else
        {
            service.Setup(s => s.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        }

        if (throwOnStop)
        {
            service.Setup(s => s.StopAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("stop fail"));
        }
        else
        {
            service.Setup(s => s.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        }

        return service.Object;
    }

    [Fact]
    public void RegisterService_Null_Throws()
    {
        var host = new ServiceHost();

        Assert.Throws<ArgumentNullException>(() => host.RegisterService(null!));
    }

    [Fact]
    public void RegisterService_Duplicate_Throws()
    {
        var host = new ServiceHost();
        var service = CreateService("svc");

        host.RegisterService(service);

        Assert.Throws<InvalidOperationException>(() => host.RegisterService(service));
    }

    [Fact]
    public void RegisterService_AddsToStatuses()
    {
        var host = new ServiceHost();
        var service = CreateService("svc");

        host.RegisterService(service);

        Assert.Equal(ServiceStatus.Stopped, host.GetServiceStatus("svc"));
    }

    [Fact]
    public async Task StartAsync_StartsAllServices()
    {
        var host = new ServiceHost();
        var service1 = CreateService("svc1");
        var service2 = CreateService("svc2");

        host.RegisterService(service1);
        host.RegisterService(service2);

        await host.StartAsync().ConfigureAwait(true);

        Assert.True(host.IsRunning);
        Assert.Equal(ServiceStatus.Running, host.GetServiceStatus("svc1"));
        Assert.Equal(ServiceStatus.Running, host.GetServiceStatus("svc2"));

        await host.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task StartAsync_WhenAlreadyRunning_Returns()
    {
        var host = new ServiceHost();
        var service = CreateService("svc");
        host.RegisterService(service);

        await host.StartAsync().ConfigureAwait(true);
        await host.StartAsync().ConfigureAwait(true);

        Assert.True(host.IsRunning);

        await host.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task StartAsync_WhenServiceThrows_MarksFailedAndThrows()
    {
        var host = new ServiceHost();
        var service = CreateService("svc", throwOnStart: true);
        host.RegisterService(service);

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync()).ConfigureAwait(true);

        Assert.Equal(ServiceStatus.Failed, host.GetServiceStatus("svc"));

        await host.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task StopAsync_StopsAllServices()
    {
        var host = new ServiceHost();
        var service = CreateService("svc");
        host.RegisterService(service);

        await host.StartAsync().ConfigureAwait(true);
        await host.StopAsync().ConfigureAwait(true);

        Assert.False(host.IsRunning);
        Assert.Equal(ServiceStatus.Stopped, host.GetServiceStatus("svc"));
    }

    [Fact]
    public async Task StopAsync_WhenNotRunning_Returns()
    {
        var host = new ServiceHost();

        await host.StopAsync().ConfigureAwait(true);

        Assert.False(host.IsRunning);
    }

    [Fact]
    public async Task StopAsync_WhenServiceThrows_LogsAndContinues()
    {
        var host = new ServiceHost();
        var service = CreateService("svc", throwOnStop: true);
        host.RegisterService(service);

        await host.StartAsync().ConfigureAwait(true);
        await host.StopAsync().ConfigureAwait(true);

        Assert.False(host.IsRunning);
    }

    [Fact]
    public async Task StartServiceAsync_ByName_ReturnsTrue()
    {
        var host = new ServiceHost();
        var service = CreateService("svc");
        host.RegisterService(service);

        var result = await host.StartServiceAsync("svc").ConfigureAwait(true);

        Assert.True(result);
        Assert.Equal(ServiceStatus.Running, host.GetServiceStatus("svc"));

        await host.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task StartServiceAsync_ByName_NotFound_ReturnsFalse()
    {
        var host = new ServiceHost();

        var result = await host.StartServiceAsync("missing").ConfigureAwait(true);

        Assert.False(result);
    }

    [Fact]
    public async Task StopServiceAsync_ByName_ReturnsTrue()
    {
        var host = new ServiceHost();
        var service = CreateService("svc");
        host.RegisterService(service);

        await host.StartServiceAsync("svc").ConfigureAwait(true);
        var result = await host.StopServiceAsync("svc").ConfigureAwait(true);

        Assert.True(result);
        Assert.Equal(ServiceStatus.Stopped, host.GetServiceStatus("svc"));

        await host.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task StopServiceAsync_ByName_NotFound_ReturnsFalse()
    {
        var host = new ServiceHost();

        var result = await host.StopServiceAsync("missing").ConfigureAwait(true);

        Assert.False(result);
    }

    [Fact]
    public void GetServiceStatus_Unknown_ReturnsNull()
    {
        var host = new ServiceHost();

        Assert.Null(host.GetServiceStatus("missing"));
    }

    [Fact]
    public void GetAllServiceStatuses_ReturnsAll()
    {
        var host = new ServiceHost();
        host.RegisterService(CreateService("svc1"));
        host.RegisterService(CreateService("svc2"));

        var statuses = host.GetAllServiceStatuses();

        Assert.Equal(2, statuses.Count);
        Assert.Equal(ServiceStatus.Stopped, statuses["svc1"]);
        Assert.Equal(ServiceStatus.Stopped, statuses["svc2"]);
    }

    [Fact]
    public async Task ServiceStatusChanged_RaisedOnStartAndStop()
    {
        var host = new ServiceHost();
        var service = CreateService("svc");
        host.RegisterService(service);

        var events = new List<ServiceEventArgs>();
        host.ServiceStatusChanged += (_, e) => events.Add(e);

        await host.StartAsync().ConfigureAwait(true);
        await host.StopAsync().ConfigureAwait(true);

        Assert.Contains(events, e => e.ServiceName == "svc" && e.NewStatus == ServiceStatus.Running);
        Assert.Contains(events, e => e.ServiceName == "svc" && e.NewStatus == ServiceStatus.Stopped);

        await host.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ServiceStatusChanged_RaisedOnFailure()
    {
        var host = new ServiceHost();
        var service = CreateService("svc", throwOnStart: true);
        host.RegisterService(service);

        var eventArgs = (ServiceEventArgs?)null;
        host.ServiceStatusChanged += (_, e) => eventArgs = e;

        try
        {
            await host.StartAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[ServiceHostTests] Expected start failure: {ex.Message}");
        }

        Assert.NotNull(eventArgs);
        Assert.Equal("svc", eventArgs.ServiceName);
        Assert.Equal(ServiceStatus.Failed, eventArgs.NewStatus);
        Assert.NotNull(eventArgs.Exception);

        await host.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task DisposeAsync_StopsServices()
    {
        var host = new ServiceHost();
        var service = CreateService("svc");
        host.RegisterService(service);

        await host.StartAsync().ConfigureAwait(true);
        await host.DisposeAsync().ConfigureAwait(true);

        Assert.False(host.IsRunning);
    }
}
