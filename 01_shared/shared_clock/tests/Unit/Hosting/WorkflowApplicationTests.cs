
namespace Clock.Tests.Unit.Hosting;

public sealed class WorkflowApplicationTests
{
    [Fact]
    public async Task Constructor_CreatesMessageBusAndHost()
    {
        await using var app = new WorkflowApplication();

        Assert.NotNull(app.MessageBus);
        Assert.NotNull(app.ServiceHost);
    }

    [Fact]
    public async Task Initialize_WithoutCronTaskStore_DoesNotRegisterCronService()
    {
        await using var app = new WorkflowApplication();

        app.Initialize();

        Assert.Empty(app.ServiceHost.GetAllServiceStatuses());
    }

    [Fact]
    public async Task Initialize_WithCronTaskStore_RegistersCronService()
    {
        var taskStore = Mock.Of<ICronTaskStore>();
        await using var app = new WorkflowApplication(cronTaskStore: taskStore);

        app.Initialize();

        Assert.Single(app.ServiceHost.GetAllServiceStatuses());
        Assert.Equal(ServiceStatus.Stopped, app.ServiceHost.GetServiceStatus("CronScheduler"));
    }

    [Fact]
    public async Task StartAsync_StartsHostAndPublishesSystemStarted()
    {
        await using var app = new WorkflowApplication();
        app.Initialize();

        ServiceMessage? received = null;
        var tcs = new TaskCompletionSource();
        await app.MessageBus.SubscribeAsync(ServiceMessageType.SystemStarted.ToValue(), msg =>
        {
            received = msg;
            tcs.TrySetResult();
            return Task.CompletedTask;
        }).ConfigureAwait(true);

        await app.StartAsync().ConfigureAwait(true);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);

        Assert.NotNull(received);
        Assert.True(app.ServiceHost.IsRunning);

        await app.StopAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task StopAsync_PublishesSystemStoppedAndStopsHost()
    {
        await using var app = new WorkflowApplication();
        app.Initialize();
        await app.StartAsync().ConfigureAwait(true);

        ServiceMessage? received = null;
        var tcs = new TaskCompletionSource();
        await app.MessageBus.SubscribeAsync(ServiceMessageType.SystemStopped.ToValue(), msg =>
        {
            received = msg;
            tcs.TrySetResult();
            return Task.CompletedTask;
        }).ConfigureAwait(true);

        await app.StopAsync().ConfigureAwait(true);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);

        Assert.NotNull(received);
        Assert.False(app.ServiceHost.IsRunning);
    }

    [Fact]
    public async Task RunAsync_WaitsForCancellation()
    {
        await using var app = new WorkflowApplication();
        app.Initialize();

        using var cts = new CancellationTokenSource();
        var runTask = app.RunAsync(cts.Token);

        cts.CancelAfter(100);

        await runTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);

        Assert.False(app.ServiceHost.IsRunning);
    }

    [Fact]
    public async Task GetStatusReport_WhenNotRunning_ReturnsStatus()
    {
        await using var app = new WorkflowApplication();
        app.Initialize();

        var report = app.GetStatusReport();

        Assert.False(report.IsRunning);
        Assert.Equal(0, report.ServiceCount);
        Assert.Equal(0, report.RunningServices);
        Assert.Equal(0, report.FailedServices);
        Assert.Empty(report.ServiceStatuses);
    }

    [Fact]
    public async Task GetStatusReport_WhenRunning_ReturnsStatus()
    {
        var taskStore = Mock.Of<ICronTaskStore>();
        await using var app = new WorkflowApplication(cronTaskStore: taskStore);
        app.Initialize();

        await app.StartAsync().ConfigureAwait(true);

        var report = app.GetStatusReport();

        Assert.True(report.IsRunning);
        Assert.Equal(1, report.ServiceCount);
        Assert.Equal(1, report.RunningServices);
        Assert.Equal(0, report.FailedServices);
        Assert.Equal(ServiceStatus.Running.ToStatusName(), report.ServiceStatuses["CronScheduler"]);

        await app.StopAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ServiceStatusChanged_PublishesMessage()
    {
        var taskStore = Mock.Of<ICronTaskStore>();
        await using var app = new WorkflowApplication(cronTaskStore: taskStore);
        app.Initialize();

        ServiceMessage? received = null;
        var tcs = new TaskCompletionSource();
        await app.MessageBus.SubscribeAsync(ServiceMessageType.ServiceStatusChanged.ToValue(), msg =>
        {
            received = msg;
            tcs.TrySetResult();
            return Task.CompletedTask;
        }).ConfigureAwait(true);

        await app.StartAsync().ConfigureAwait(true);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);

        Assert.NotNull(received);
        Assert.Equal(ServiceMessageType.ServiceStatusChanged.ToValue(), received.MessageType);

        await app.StopAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task DisposeAsync_StopsApplication()
    {
        var taskStore = Mock.Of<ICronTaskStore>();
        var app = new WorkflowApplication(cronTaskStore: taskStore);
        app.Initialize();
        await app.StartAsync().ConfigureAwait(true);

        await app.DisposeAsync().ConfigureAwait(true);

        Assert.False(app.ServiceHost.IsRunning);
    }
}
