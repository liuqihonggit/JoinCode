namespace Core.Context;

public sealed class ConfigChangeStartMiddlewareTest
{
    [Fact]
    public async Task InvokeAsync_WithNotifier_StartsMonitoringWithWorkingDirectoryFromFileSystem()
    {
        var notifierMock = new Mock<IConfigChangeNotifier>();
        var fsMock = new Mock<IFileSystem>();
        fsMock.Setup(f => f.GetCurrentDirectory()).Returns("test-working-dir");
        var middleware = CreateMiddleware(fsMock.Object, configChangeNotifier: notifierMock.Object);

        await middleware.InvokeAsync(CreateContext(), (_, _) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        notifierMock.Verify(n => n.StartMonitoring("test-working-dir"), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WithNullNotifier_CallsNextWithoutThrowing()
    {
        var fsMock = new Mock<IFileSystem>();
        var middleware = CreateMiddleware(fsMock.Object, configChangeNotifier: null);
        var nextCalled = false;

        await middleware.InvokeAsync(
            CreateContext(),
            (_, _) => { nextCalled = true; return Task.CompletedTask; },
            CancellationToken.None).ConfigureAwait(true);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public void Constructor_DoesNotAcceptSystemReminderManager_RemovedDependency()
    {
        var constructors = typeof(ConfigChangeStartMiddleware).GetConstructors();
        var hasReminderParameter = constructors
            .SelectMany(c => c.GetParameters())
            .Any(p => p.ParameterType == typeof(ISystemReminderManager));

        hasReminderParameter.Should().BeFalse();
    }

    [Fact]
    public async Task OnConfigChanged_WithApplier_CallsApplySettingsChangeAsync()
    {
        var notifierMock = new Mock<IConfigChangeNotifier>();
        var applierMock = new Mock<ISettingsChangeApplier>();
        applierMock.Setup(a => a.ApplySettingsChangeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var middleware = CreateMiddleware(
            configChangeNotifier: notifierMock.Object,
            settingsChangeApplier: applierMock.Object);

        await middleware.InvokeAsync(CreateContext(), (_, _) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);
        notifierMock.Raise(n => n.ConfigChanged += null, CreateEventArgs());

        applierMock.Verify(a => a.ApplySettingsChangeAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnConfigChanged_WithApplier_DoesNotCallSystemReminderManagerAddReminder()
    {
        var notifierMock = new Mock<IConfigChangeNotifier>();
        var reminderMock = new Mock<ISystemReminderManager>();
        var applierMock = new Mock<ISettingsChangeApplier>();
        applierMock.Setup(a => a.ApplySettingsChangeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var middleware = CreateMiddleware(
            configChangeNotifier: notifierMock.Object,
            settingsChangeApplier: applierMock.Object);

        await middleware.InvokeAsync(CreateContext(), (_, _) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);
        notifierMock.Raise(n => n.ConfigChanged += null, CreateEventArgs());

        reminderMock.Verify(r => r.AddReminderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        applierMock.Verify(a => a.ApplySettingsChangeAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnConfigChanged_WithNullApplier_DoesNotThrow()
    {
        var notifierMock = new Mock<IConfigChangeNotifier>();
        var middleware = CreateMiddleware(
            configChangeNotifier: notifierMock.Object,
            settingsChangeApplier: null);

        await middleware.InvokeAsync(CreateContext(), (_, _) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);
        var act = () => notifierMock.Raise(n => n.ConfigChanged += null, CreateEventArgs());

        act.Should().NotThrow();
    }

    [Fact]
    public async Task OnConfigChanged_AfterDispose_DoesNotCallApplySettingsChangeAsync()
    {
        var notifierMock = new Mock<IConfigChangeNotifier>();
        var applierMock = new Mock<ISettingsChangeApplier>();
        applierMock.Setup(a => a.ApplySettingsChangeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var middleware = CreateMiddleware(
            configChangeNotifier: notifierMock.Object,
            settingsChangeApplier: applierMock.Object);

        await middleware.InvokeAsync(CreateContext(), (_, _) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);
        await middleware.DisposeAsync().ConfigureAwait(true);
        notifierMock.Raise(n => n.ConfigChanged += null, CreateEventArgs());

        applierMock.Verify(a => a.ApplySettingsChangeAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DisposeAsync_UnsubscribesAndStopsMonitoring()
    {
        var notifierMock = new Mock<IConfigChangeNotifier>();
        var applierMock = new Mock<ISettingsChangeApplier>();
        applierMock.Setup(a => a.ApplySettingsChangeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var middleware = CreateMiddleware(
            configChangeNotifier: notifierMock.Object,
            settingsChangeApplier: applierMock.Object);

        await middleware.InvokeAsync(CreateContext(), (_, _) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);
        await middleware.DisposeAsync().ConfigureAwait(true);
        notifierMock.Raise(n => n.ConfigChanged += null, CreateEventArgs());

        notifierMock.Verify(n => n.StopMonitoring(), Times.Once);
        applierMock.Verify(a => a.ApplySettingsChangeAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ConfigChangeStartMiddleware CreateMiddleware(
        IFileSystem? fs = null,
        IConfigChangeNotifier? configChangeNotifier = null,
        ISettingsChangeApplier? settingsChangeApplier = null,
        ILogger<ConfigChangeStartMiddleware>? logger = null)
    {
        return new ConfigChangeStartMiddleware(
            fs: fs ?? new Mock<IFileSystem>().Object,
            configChangeNotifier: configChangeNotifier,
            settingsChangeApplier: settingsChangeApplier,
            logger: logger);
    }

    private static ChatInitContext CreateContext() => new()
    {
        ToolUseContext = new ToolUseContext(),
        ContextManager = new Mock<IChatContextManager>().Object
    };

    private static ConfigChangeEventArgs CreateEventArgs() => new()
    {
        FilePath = "settings.json",
        ChangeType = "Changed",
        Timestamp = DateTimeOffset.Now
    };
}
