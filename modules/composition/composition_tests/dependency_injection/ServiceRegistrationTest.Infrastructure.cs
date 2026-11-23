namespace Core.Tests.DependencyInjection;

/// <summary>
/// ServiceRegistration.AddInfrastructureServices 拆分后的 internal 子方法确定性测试。
/// <para>验证每个环境切换分支在对应环境变量下注册正确的服务实现，无时序依赖。</para>
/// <para>环境变量为进程级状态，测试通过 set/cleanup 保证确定性；ServiceProvider 用 await using 异步释放。</para>
/// </summary>
public sealed partial class ServiceRegistrationTest {
    /// <summary>
    /// 临时设置环境变量并在作用域结束时恢复原值。
    /// </summary>
    private sealed class EnvVarScope : IDisposable {
        private readonly string _varName;
        private readonly string? _originalValue;

        private EnvVarScope(string varName, string? newValue) {
            _varName = varName;
            _originalValue = Environment.GetEnvironmentVariable(varName);
            Environment.SetEnvironmentVariable(varName, newValue);
        }

        internal static EnvVarScope Set(JccEnvVar var, string? value) => new(var.ToValue(), value);

        public void Dispose() => Environment.SetEnvironmentVariable(_varName, _originalValue);
    }

    [Fact]
    public async Task RegisterHttpClientFactory_RegistersIHttpClientFactory() {
        var services = new ServiceCollection();
        CompositionServiceRegistration.RegisterHttpClientFactory(services);
        await using var sp = services.BuildServiceProvider();
        sp.GetService<IHttpClientFactory>().Should().NotBeNull("AddHttpClient 应注册 IHttpClientFactory");
    }

    [Fact]
    public async Task RegisterHttpClientProviderSwitch_MockMode_RegistersMockProvider() {
        using var scope = EnvVarScope.Set(JccEnvVar.HttpMode, "Mock");
        var services = new ServiceCollection();
        CompositionServiceRegistration.RegisterHttpClientProviderSwitch(services);
        await using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IHttpClientProvider>();
        provider.Should().BeOfType<MockHttpClientProvider>("JCC_HTTP_MODE=Mock 应注册 MockHttpClientProvider");
    }

    [Fact]
    public async Task RegisterHttpClientProviderSwitch_RealMode_RegistersDefaultProvider() {
        using var scope = EnvVarScope.Set(JccEnvVar.HttpMode, "Real");
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddSingleton<DefaultHttpClientProvider>();
        CompositionServiceRegistration.RegisterHttpClientProviderSwitch(services);
        await using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IHttpClientProvider>();
        provider.Should().BeOfType<DefaultHttpClientProvider>("JCC_HTTP_MODE=Real 应注册 DefaultHttpClientProvider");
    }

    [Fact]
    public async Task RegisterResilientHttpClientProviderSwitch_Enabled_RegistersResilientProvider() {
        using var scope = EnvVarScope.Set(JccEnvVar.ResilienceEnabled, "1");
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddSingleton<DefaultHttpClientProvider>();
        services.AddEnvSwitch<IHttpClientProvider>(
            JccEnvVar.HttpMode, "Mock",
            _ => new MockHttpClientProvider(),
            sp => sp.GetRequiredService<DefaultHttpClientProvider>());
        CompositionServiceRegistration.RegisterResilientHttpClientProviderSwitch(services);
        await using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IResilientHttpClientProvider>();
        provider.Should().BeOfType<ResilientHttpClientProvider>("JCC_RESILIENCE_ENABLED=1 应注册 ResilientHttpClientProvider");
    }

    [Fact]
    public async Task RegisterResilientHttpClientProviderSwitch_Disabled_RegistersResilientProviderWithDisabledPolicy() {
        using var scope = EnvVarScope.Set(JccEnvVar.ResilienceEnabled, "0");
        var services = new ServiceCollection();
        services.AddSingleton<DefaultHttpClientProvider>();
        services.AddEnvSwitch<IHttpClientProvider>(
            JccEnvVar.HttpMode, "Mock",
            _ => new MockHttpClientProvider(),
            sp => sp.GetRequiredService<DefaultHttpClientProvider>());
        CompositionServiceRegistration.RegisterResilientHttpClientProviderSwitch(services);
        await using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IResilientHttpClientProvider>();
        provider.Should().BeOfType<ResilientHttpClientProvider>("禁用韧性仍应注册 ResilientHttpClientProvider（带 disabled 策略）");
    }

    [Fact]
    public async Task RegisterNotificationServiceSwitch_ConsoleMode_RegistersConsoleNotificationService() {
        using var scope = EnvVarScope.Set(JccEnvVar.NotificationMode, "Console");
        var services = new ServiceCollection();
        CompositionServiceRegistration.RegisterNotificationServiceSwitch(services);
        await using var sp = services.BuildServiceProvider();
        var notification = sp.GetRequiredService<INotificationService>();
        notification.Should().BeOfType<ConsoleNotificationService>("JCC_NOTIFICATION_MODE=Console 应注册 ConsoleNotificationService");
    }

    [Fact]
    public async Task RegisterNotificationServiceSwitch_WindowsMode_DoesNotRegisterConsole() {
        using var scope = EnvVarScope.Set(JccEnvVar.NotificationMode, "Windows");
        var services = new ServiceCollection();
        CompositionServiceRegistration.RegisterNotificationServiceSwitch(services);
        await using var sp = services.BuildServiceProvider();
        sp.GetService<INotificationService>().Should().BeNull("JCC_NOTIFICATION_MODE=Windows 不匹配 Console altMode，AddEnvSwitch 不注册");
    }

    [Fact]
    public async Task RegisterBrowserAutomationSwitch_DefaultMode_RegistersNoOp() {
        using var scope = EnvVarScope.Set(JccEnvVar.BrowserAutomation, "None");
        var services = new ServiceCollection();
        CompositionServiceRegistration.RegisterBrowserAutomationSwitch(services);
        await using var sp = services.BuildServiceProvider();
        var browser = sp.GetRequiredService<IBrowserAutomationService>();
        browser.Should().BeOfType<NoOpBrowserAutomationService>("默认模式应注册 NoOpBrowserAutomationService");
    }

    [Fact]
    public async Task RegisterBrowserAutomationSwitch_PuppeteerMode_DoesNotRegisterNoOp() {
        using var scope = EnvVarScope.Set(JccEnvVar.BrowserAutomation, "Puppeteer");
        var services = new ServiceCollection();
        CompositionServiceRegistration.RegisterBrowserAutomationSwitch(services);
        await using var sp = services.BuildServiceProvider();
        sp.GetService<IBrowserAutomationService>().Should().BeNull("Puppeteer 模式不应注册 NoOp（由其他注册器负责）");
    }

    [Fact]
    public async Task RegisterTaskServiceSwitch_MemoryMode_RegistersTaskService() {
        using var scope = EnvVarScope.Set(JccEnvVar.TaskServiceMode, "Memory");
        var services = new ServiceCollection();
        services.AddSingleton<TaskService>();
        CompositionServiceRegistration.RegisterTaskServiceSwitch(services);
        await using var sp = services.BuildServiceProvider();
        var taskService = sp.GetRequiredService<ITaskService>();
        taskService.Should().BeOfType<TaskService>("JCC_TASK_SERVICE_MODE=Memory 应转发到 TaskService");
    }

    [Fact]
    public async Task RegisterClockServiceSwitch_FakeMode_RegistersFakeClockService() {
        using var scope = EnvVarScope.Set(JccEnvVar.ClockMode, "Fake");
        var services = new ServiceCollection();
        CompositionServiceRegistration.RegisterClockServiceSwitch(services);
        await using var sp = services.BuildServiceProvider();
        var clock = sp.GetRequiredService<IClockService>();
        clock.Should().BeOfType<FakeClockService>("JCC_CLOCK_MODE=Fake 应注册 FakeClockService");
    }

    [Fact]
    public async Task RegisterClockServiceSwitch_PhysicalMode_RegistersPhysicalClockService() {
        using var scope = EnvVarScope.Set(JccEnvVar.ClockMode, "Physical");
        var services = new ServiceCollection();
        services.AddSingleton<PhysicalClockService>();
        CompositionServiceRegistration.RegisterClockServiceSwitch(services);
        await using var sp = services.BuildServiceProvider();
        var clock = sp.GetRequiredService<IClockService>();
        clock.Should().BeOfType<PhysicalClockService>("JCC_CLOCK_MODE=Physical 应注册 PhysicalClockService");
    }

    [Fact]
    public async Task RegisterProcessEncodingServices_RegistersBothInterfaces() {
        var services = new ServiceCollection();
        CompositionServiceRegistration.RegisterProcessEncodingServices(services);
        await using var sp = services.BuildServiceProvider();
        sp.GetService<IProcessEncodingProvider>().Should().NotBeNull("应注册 IProcessEncodingProvider");
        sp.GetService<IProcessStartInfoBuilder>().Should().NotBeNull("应注册 IProcessStartInfoBuilder");
    }

    [Fact]
    public async Task RegisterProcessServiceSwitch_NoOpMode_RegistersNoOpProcessService() {
        using var scope = EnvVarScope.Set(JccEnvVar.ProcessMode, "NoOp");
        var services = new ServiceCollection();
        CompositionServiceRegistration.RegisterProcessServiceSwitch(services);
        await using var sp = services.BuildServiceProvider();
        var process = sp.GetRequiredService<IProcessService>();
        process.Should().BeOfType<IO.ProcessService.NoOpProcessService>("JCC_PROCESS_MODE=NoOp 应注册 NoOpProcessService");
    }

    [Fact]
    public async Task RegisterConsoleOutputSwitch_NoOpMode_RegistersNoOpConsoleOutput() {
        using var scope = EnvVarScope.Set(JccEnvVar.ConsoleMode, "NoOp");
        var services = new ServiceCollection();
        CompositionServiceRegistration.RegisterConsoleOutputSwitch(services);
        await using var sp = services.BuildServiceProvider();
        var console = sp.GetRequiredService<IConsoleOutput>();
        console.Should().BeOfType<NoOpConsoleOutput>("JCC_CONSOLE_MODE=NoOp 应注册 NoOpConsoleOutput");
    }

    [Fact]
    public async Task AddInfrastructureServices_OrchestratesAllTenSubMethods() {
        using var httpScope = EnvVarScope.Set(JccEnvVar.HttpMode, "Mock");
        using var resilienceScope = EnvVarScope.Set(JccEnvVar.ResilienceEnabled, "0");
        using var notificationScope = EnvVarScope.Set(JccEnvVar.NotificationMode, "Console");
        using var browserScope = EnvVarScope.Set(JccEnvVar.BrowserAutomation, "None");
        using var taskScope = EnvVarScope.Set(JccEnvVar.TaskServiceMode, "Memory");
        using var clockScope = EnvVarScope.Set(JccEnvVar.ClockMode, "Fake");
        using var processScope = EnvVarScope.Set(JccEnvVar.ProcessMode, "NoOp");
        using var consoleScope = EnvVarScope.Set(JccEnvVar.ConsoleMode, "NoOp");

        var services = new ServiceCollection();
        services.AddSingleton<TaskService>();
        CompositionServiceRegistration.AddInfrastructureServices(services);

        await using var sp = services.BuildServiceProvider();
        sp.GetService<IHttpClientFactory>().Should().NotBeNull();
        sp.GetService<IHttpClientProvider>().Should().BeOfType<MockHttpClientProvider>();
        sp.GetService<IResilientHttpClientProvider>().Should().NotBeNull();
        sp.GetService<INotificationService>().Should().BeOfType<ConsoleNotificationService>();
        sp.GetService<IBrowserAutomationService>().Should().BeOfType<NoOpBrowserAutomationService>();
        sp.GetService<ITaskService>().Should().NotBeNull();
        sp.GetService<IClockService>().Should().BeOfType<FakeClockService>();
        sp.GetService<IProcessEncodingProvider>().Should().NotBeNull();
        sp.GetService<IProcessStartInfoBuilder>().Should().NotBeNull();
        sp.GetService<IProcessService>().Should().BeOfType<IO.ProcessService.NoOpProcessService>();
        sp.GetService<IConsoleOutput>().Should().BeOfType<NoOpConsoleOutput>();
    }
}
