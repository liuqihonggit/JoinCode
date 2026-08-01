
namespace Clock.Tests.Unit.Hosting;

public sealed class CronSchedulerServiceTests
{
    private static (Mock<ICronTaskStore> taskStore, ServiceMessageBus messageBus, CronSchedulerService service) CreateService(
        INotificationService? notificationService = null,
        ILogger<CronSchedulerService>? logger = null)
    {
        var taskStore = new Mock<ICronTaskStore>();
        var messageBus = new ServiceMessageBus();
        var service = new CronSchedulerService(taskStore.Object, messageBus, notificationService, logger);
        return (taskStore, messageBus, service);
    }

    [Fact]
    public void Constructor_NullTaskStore_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CronSchedulerService(null!, new ServiceMessageBus()));
    }

    [Fact]
    public void Constructor_NullMessageBus_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CronSchedulerService(Mock.Of<ICronTaskStore>(), null!));
    }

    [Fact]
    public void ServiceName_IsCronScheduler()
    {
        var (_, _, service) = CreateService();

        Assert.Equal("CronScheduler", service.ServiceName);
    }

    [Fact]
    public async Task StartAsync_WhenStopped_StartsService()
    {
        var (_, _, service) = CreateService();

        Assert.Equal(ServiceStatus.Stopped, service.Status);

        await service.StartAsync().ConfigureAwait(true);

        Assert.Equal(ServiceStatus.Running, service.Status);

        await service.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task StartAsync_WhenAlreadyRunning_DoesNothing()
    {
        var (_, _, service) = CreateService();

        await service.StartAsync().ConfigureAwait(true);
        await service.StartAsync().ConfigureAwait(true);

        Assert.Equal(ServiceStatus.Running, service.Status);

        await service.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task StopAsync_WhenRunning_StopsService()
    {
        var (_, _, service) = CreateService();

        await service.StartAsync().ConfigureAwait(true);
        await service.StopAsync().ConfigureAwait(true);

        Assert.Equal(ServiceStatus.Stopped, service.Status);
    }

    [Fact]
    public async Task StopAsync_WhenNotRunning_DoesNothing()
    {
        var (_, _, service) = CreateService();

        await service.StopAsync().ConfigureAwait(true);

        Assert.Equal(ServiceStatus.Stopped, service.Status);
    }

    [Fact]
    public async Task StartAsync_PublishesCronTaskFiredMessage()
    {
        var (taskStore, messageBus, service) = CreateService();
        ServiceMessage? received = null;

        var tcs = new TaskCompletionSource();
        await messageBus.SubscribeAsync(ServiceMessageType.CronTaskFired.ToValue(), msg =>
        {
            received = msg;
            tcs.TrySetResult();
            return Task.CompletedTask;
        }).ConfigureAwait(true);

        taskStore.Setup(t => t.GetAllTasksAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CronTask>
        {
            new()
            {
                Id = "t1",
                CronExpression = "* * * * *",
                Prompt = "test",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds(),
                IsRecurring = true,
                IsPermanent = true
            }
        });

        await service.StartAsync().ConfigureAwait(true);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);

        Assert.NotNull(received);
        Assert.Equal(ServiceMessageType.CronTaskFired.ToValue(), received.MessageType);

        await service.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task StartAsync_WithNotificationService_Notifies()
    {
        var notificationService = new Mock<INotificationService>();
        notificationService.Setup(n => n.NotifyAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);

        var (taskStore, _, service) = CreateService(notificationService.Object);

        var tcs = new TaskCompletionSource();
        taskStore.Setup(t => t.GetAllTasksAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CronTask>
        {
            new()
            {
                Id = "t1",
                CronExpression = "* * * * *",
                Prompt = "test",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds(),
                IsRecurring = true,
                IsPermanent = true
            }
        });

        await service.StartAsync().ConfigureAwait(true);

        // Give scheduler time to fire
        await Task.Delay(500).ConfigureAwait(true);

        notificationService.Verify(n => n.NotifyAsync(It.IsAny<string>(), It.Is<string>(s => s.Contains("t1"))), Times.AtLeastOnce);

        await service.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task DisposeAsync_WhenRunning_StopsService()
    {
        var (_, _, service) = CreateService();

        await service.StartAsync().ConfigureAwait(true);
        await service.DisposeAsync().ConfigureAwait(true);

        Assert.Equal(ServiceStatus.Stopped, service.Status);
    }

    [Fact]
    public async Task DisposeAsync_WhenStopped_DoesNotThrow()
    {
        var (_, _, service) = CreateService();

        await service.DisposeAsync().ConfigureAwait(true);

        Assert.Equal(ServiceStatus.Stopped, service.Status);
    }
}
