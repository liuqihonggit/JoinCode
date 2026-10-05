
namespace Core.DependencyInjection;

/// <summary>
/// 核心服务注册器 — 注册 Composition 层的核心服务（API 客户端、代码安全、提示、文件操作、工具、基础设施）。
/// <para>本类为 partial，与 <see cref="ServiceRegistration"/> 的其他 partial 定义（Kernel/NewServices/Brain/Bridge/CodeIndex/Mcp/Skills）共同组成完整的 DI 注册入口。</para>
/// </summary>
public static partial class ServiceRegistration {
    /// <summary>
    /// 注册核心服务：API 客户端、代码安全、提示服务、系统提示提供者等。
    /// <para>大部分服务通过 [Register] 特性自动注册，本方法仅补充未被自动注册覆盖的部分。</para>
    /// </summary>
    /// <param name="services">DI 容器。</param>
    /// <returns>已注册服务的 <see cref="IServiceCollection"/> 实例。</returns>
    public static IServiceCollection AddCoreServices(this IServiceCollection services) {
        // HttpClient — [Register] 自动注册（SharedHttpClient）

        // LspServiceDeps — [Register] 自动注册（构造函数参数均为可选 DI 接口）

        // QueryLoopServices — [Register] 自动注册（构造函数参数均为可选 DI 接口）

        // Chat/ChatAdmin/ChatInit 管道 — 由 [RegisterMiddleware] + [Register(IPipelineHook)] + 生成器自动注册

        // IStore<AppState> — [Register(typeof(IStore<AppState>), ServiceLifetime.Singleton)] 自动注册（AppStateStore）

        services.AddApiClientServices();

        // ICommandClassifier, IShellMiddleware, Shell 中间件管道 — [Register] + [RegisterMiddleware] 自动注册
        services.AddCodeSecurityServices();

        services.AddPromptServices();

        // ISystemPromptProvider — [Register] 自动注册（DefaultSystemPromptProvider）
        // SystemPromptProviderOptions — [Register] 自动注册（SyncSystemPromptProviderOptions）


        // MemdirOptions — 已移至 AddVaultServices 统一注册

        return services;
    }

    /// <summary>
    /// 注册文件操作服务：根据 <c>JCC_FILE_SYSTEM_MODE</c> 环境变量切换 IFileSystem 后端（默认 Physical，InMemory=纯内存），
    /// 并绑定 <see cref="FileOperationConfig"/> 配置（含验证）。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    /// <returns>已注册服务的 <see cref="IServiceCollection"/> 实例。</returns>
    public static IServiceCollection AddFileOperationServices(this IServiceCollection services) {
        // IFileSystem — 根据 JCC_FILE_SYSTEM_MODE 环境变量决定后端
        // 默认 Physical（真实磁盘），InMemory=纯内存0磁盘IO（调试/E2E测试用）
        // 注意: [Register] 自动注册的 IFileSystem 转发已在此处被覆盖（后注册 wins）
        services.AddEnvSwitch<IFileSystem>(
            JccEnvVar.FileSystemMode, "InMemory",
            _ => new InMemoryFileSystem(),
            sp => sp.GetRequiredService<PhysicalFileSystem>());

        services.AddOptions<FileOperationConfig>()
            .BindConfiguration("Workflow:FileOperation")
            .Validate(config => ServiceRegistrationConfigValidator.ValidateFileOperationConfig(config), L.T(StringKey.FileOperationConfigValidationFailed))
            .ValidateOnStart();

        // IKvStore — 根据 JCC_FILE_SYSTEM_MODE 环境变量决定后端
        // 默认 PithosKvStore（LSM-Tree 磁盘持久化），InMemory=纯内存（调试/E2E测试用）
        services.AddEnvSwitch<IKvStore>(
            JccEnvVar.FileSystemMode, "InMemory",
            _ => new InMemoryKvStore(),
            _ => new PithosKvStore(Path.Combine(AppDataConstants.Paths.JccDirectory, "kvstore")));

        // FileOperationConfig — 直接注册供 FileOperationService 构造函数使用
        // （FileOperationService 有手动构造函数，不使用 生成器）
        services.AddSingleton(sp => {
            var options = sp.GetRequiredService<IOptions<FileOperationConfig>>();
            return options.Value;
        });

        return services;
    }

    /// <summary>
    /// 注册工具服务：绑定 <see cref="ShellExecutionConfig"/> 配置（含验证），支持环境变量覆盖超时参数，
    /// 并注册 <see cref="LongRunningTaskRegistry"/>（超时续期任务注册表）。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    /// <returns>已注册服务的 <see cref="IServiceCollection"/> 实例。</returns>
    public static IServiceCollection AddToolServices(this IServiceCollection services) {
        services.AddOptions<ShellExecutionConfig>()
            .BindConfiguration("Workflow:ShellExecution")
            .Validate(config => ServiceRegistrationConfigValidator.ValidateShellExecutionConfig(config), "ShellExecutionConfig 验证失败")
            .ValidateOnStart();

        // ShellExecutionConfig — 直接注册供 SystemActuatorBase 构造函数使用
        // （SystemActuatorBase 构造函数取 ShellExecutionConfig 而非 IOptions<>）
        // 支持环境变量覆盖: JCC_ABSOLUTE_TIMEOUT_SECONDS, JCC_RESUME_TIMEOUT_SECONDS
        services.AddSingleton(sp => {
            var options = sp.GetRequiredService<IOptions<ShellExecutionConfig>>();
            var config = options.Value;

            ServiceRegistrationConfigValidator.ApplyEnvOverrides(
                config,
                Environment.GetEnvironmentVariable(JccEnvVar.AbsoluteTimeoutSeconds.ToValue()),
                Environment.GetEnvironmentVariable(JccEnvVar.ResumeTimeoutSeconds.ToValue()));

            return config;
        });

        // SystemActuatorBase — 已改为静态缓存，不再 DI 注册
        // SystemActuatorRegistry 在 SystemActuatorInitializer 中初始化（替代原 SystemActuatorRegistry + SystemActuatorRegistry）

        // LongRunningTaskRegistry — 超时续期任务注册表（单例，跟踪 resume/continue/stop 任务）
        services.AddSingleton<LongRunningTaskRegistry>();

        return services;
    }

    /// <summary>
    /// 注册基础设施服务：HttpClientFactory、IHttpClientProvider（含 Mock/Real 切换）、
    /// 韧性层（ResilientHttpClientProvider）、INotificationService、IBrowserAutomationService、
    /// ITaskService、IClockService、IProcessService、IConsoleOutput 等环境切换服务。
    /// <para>本方法为编排入口，具体环境切换分支拆分到 internal 子方法，便于确定性单元测试覆盖。</para>
    /// </summary>
    /// <param name="services">DI 容器。</param>
    /// <returns>已注册服务的 <see cref="IServiceCollection"/> 实例。</returns>
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services) {
        RegisterHttpClientFactory(services);
        RegisterHttpClientProviderSwitch(services);
        RegisterResilientHttpClientProviderSwitch(services);
        RegisterNotificationServiceSwitch(services);
        RegisterBrowserAutomationSwitch(services);
        RegisterTaskServiceSwitch(services);
        RegisterClockServiceSwitch(services);
        RegisterProcessEncodingServices(services);
        RegisterProcessServiceSwitch(services);
        RegisterConsoleOutputSwitch(services);
        return services;
    }

    /// <summary>
    /// 启用 IHttpClientFactory — HttpMessageHandler 生命周期由 IHttpClientFactory 池化管理，避免 socket 耗尽。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    internal static void RegisterHttpClientFactory(IServiceCollection services) {
        services.AddHttpClient();
    }

    /// <summary>
    /// 注册 IHttpClientProvider 环境切换 — JCC_HTTP_MODE=Mock 用 MockHttpClientProvider，否则 DefaultHttpClientProvider。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    internal static void RegisterHttpClientProviderSwitch(IServiceCollection services) {
        services.AddEnvSwitch<IHttpClientProvider>(
            JccEnvVar.HttpMode, "Mock",
            _ => new Infrastructure.Http.MockHttpClientProvider(),
            sp => sp.GetRequiredService<Infrastructure.Http.DefaultHttpClientProvider>());
    }

    /// <summary>
    /// 注册 IResilientHttpClientProvider 韧性层切换 — JCC_RESILIENCE_ENABLED≠0 启用超时+重试+熔断，否则禁用韧性策略。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    internal static void RegisterResilientHttpClientProviderSwitch(IServiceCollection services) {
        var resilienceEnabled = EnvHelper.Get(JccEnvVar.ResilienceEnabled) is not "0";
        if (resilienceEnabled) {
            services.AddSingleton<IResilientHttpClientProvider>(sp => {
                var inner = sp.GetRequiredService<IHttpClientProvider>();
                var logger = sp.GetService<ILogger<Infrastructure.Http.ResilientHttpClientProvider>>();
                return new Infrastructure.Http.ResilientHttpClientProvider(inner, logger: logger);
            });
        } else {
            services.AddSingleton<IResilientHttpClientProvider>(sp => {
                var inner = sp.GetRequiredService<IHttpClientProvider>();
                return new Infrastructure.Http.ResilientHttpClientProvider(inner,
                    policy: new Infrastructure.Utils.Resilience.ResiliencePolicy {
                        Name = "disabled",
                        Retry = new Infrastructure.Utils.Resilience.RetryConfig { MaxRetries = 0 },
                    });
            });
        }
    }

    /// <summary>
    /// 注册 INotificationService 环境切换 — JCC_NOTIFICATION_MODE=Console 用 ConsoleNotificationService，否则默认 Windows 气泡通知。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    internal static void RegisterNotificationServiceSwitch(IServiceCollection services) {
        services.AddEnvSwitch<INotificationService>(
            JccEnvVar.NotificationMode, "Console",
            _ => new ConsoleNotificationService());
    }

    /// <summary>
    /// 注册 IBrowserAutomationService 环境切换 — JCC_BROWSER_AUTOMATION≠Puppeteer 时用 NoOpBrowserAutomationService。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    internal static void RegisterBrowserAutomationSwitch(IServiceCollection services) {
        var browserMode = EnvHelper.Get(JccEnvVar.BrowserAutomation);
        if (!string.Equals(browserMode, "Puppeteer", StringComparison.OrdinalIgnoreCase)) {
            services.AddSingleton<IBrowserAutomationService>(sp =>
                EnvSwitchRegistrar.TraceFactory(_ => new NoOpBrowserAutomationService(), "IBrowserAutomationService", "NoOp", sp));
        }
    }

    /// <summary>
    /// 注册 ITaskService 环境切换 — JCC_TASK_SERVICE_MODE=Memory 用内存实现，否则默认 TaskService（文件持久化）。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    internal static void RegisterTaskServiceSwitch(IServiceCollection services) {
        services.AddEnvSwitch<ITaskService>(
            JccEnvVar.TaskServiceMode, "Memory",
            sp => sp.GetRequiredService<TaskService>());
    }

    /// <summary>
    /// 注册 IClockService 环境切换 — JCC_CLOCK_MODE=Fake 用 FakeClockService，否则 PhysicalClockService。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    internal static void RegisterClockServiceSwitch(IServiceCollection services) {
        services.AddEnvSwitch<IClockService>(
            JccEnvVar.ClockMode, "Fake",
            _ => new Infrastructure.Time.FakeClockService(),
            sp => sp.GetRequiredService<Infrastructure.Time.PhysicalClockService>());
    }

    /// <summary>
    /// 注册进程编码与启动信息构建器 — IProcessEncodingProvider + ProcessStartInfoBuilder（统一编码 + 三道防线）。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    internal static void RegisterProcessEncodingServices(IServiceCollection services) {
        services.TryAddSingleton<IO.ProcessService.ProcessEncodingProvider>();
        services.TryAddSingleton<IProcessEncodingProvider>(sp => sp.GetRequiredService<IO.ProcessService.ProcessEncodingProvider>());

        services.TryAddSingleton<IO.ProcessService.ProcessStartInfoBuilder>();
        services.TryAddSingleton<IProcessStartInfoBuilder>(sp => sp.GetRequiredService<IO.ProcessService.ProcessStartInfoBuilder>());
    }

    /// <summary>
    /// 注册 IProcessService 环境切换 — JCC_PROCESS_MODE=NoOp 用 NoOpProcessService，否则 PhysicalProcessService。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    internal static void RegisterProcessServiceSwitch(IServiceCollection services) {
        services.AddEnvSwitch<IProcessService>(
            JccEnvVar.ProcessMode, "NoOp",
            _ => new IO.ProcessService.NoOpProcessService(),
            sp => new IO.ProcessService.PhysicalProcessService(
                sp.GetRequiredService<IO.ProcessService.ProcessStartInfoBuilder>(),
                sp.GetService<ILogger<IO.ProcessService.PhysicalProcessService>>()));
    }

    /// <summary>
    /// 注册 IConsoleOutput 环境切换 — JCC_CONSOLE_MODE=NoOp 用 NoOpConsoleOutput，否则 PhysicalConsoleOutput。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    internal static void RegisterConsoleOutputSwitch(IServiceCollection services) {
        services.AddEnvSwitch<IConsoleOutput>(
            JccEnvVar.ConsoleMode, "NoOp",
            _ => new Infrastructure.IO.NoOpConsoleOutput(),
            sp => sp.GetRequiredService<Infrastructure.IO.PhysicalConsoleOutput>());
    }
}

/// <summary>
/// ServiceRegistration 配置验证器 — 纯计算验证方法，独立类避免跨程序集 ServiceRegistration partial 类型冲突。
/// </summary>
public static class ServiceRegistrationConfigValidator {
    /// <summary>
    /// 验证 FileOperationConfig 的 4 个范围约束 — 纯计算，供确定性测试覆盖。
    /// </summary>
    internal static bool ValidateFileOperationConfig(FileOperationConfig config) {
        if (config.MaxReadSize < 1024 || config.MaxReadSize > 1024L * 1024 * 1024) return false;
        if (config.MaxWriteSize < 1024 || config.MaxWriteSize > 1024 * 1024 * 1024) return false;
        if (config.BufferSize < 512 || config.BufferSize > 1024 * 1024) return false;
        if (config.BinaryDetectionBufferSize < 1024 || config.BinaryDetectionBufferSize > 64 * 1024) return false;
        return true;
    }

    /// <summary>
    /// 验证 ShellExecutionConfig 的 2 个范围约束 — 纯计算，供确定性测试覆盖。
    /// </summary>
    internal static bool ValidateShellExecutionConfig(ShellExecutionConfig config) {
        if (config.MaxOutputBytes < 1024 || config.MaxOutputBytes > 1024 * 1024) return false;
        if (config.DefaultTimeoutSeconds < 1 || config.DefaultTimeoutSeconds > 3600) return false;
        return true;
    }

    /// <summary>
    /// 应用环境变量覆盖到 ShellExecutionConfig — 纯计算（env 值以参数传入），供确定性测试覆盖。
    /// <para>JCC_ABSOLUTE_TIMEOUT_SECONDS: TryParse + ≥0 校验</para>
    /// <para>JCC_RESUME_TIMEOUT_SECONDS: TryParse + ≥60 校验</para>
    /// </summary>
    internal static ShellExecutionConfig ApplyEnvOverrides(ShellExecutionConfig config, string? envAbsolute, string? envResume) {
        var newAbsolute = int.TryParse(envAbsolute, out var absSeconds) && absSeconds >= 0 ? absSeconds : config.AbsoluteTimeoutSeconds;
        var newResume = int.TryParse(envResume, out var resumeSeconds) && resumeSeconds >= 60 ? resumeSeconds : config.ResumeTimeoutSeconds;
        return config with { AbsoluteTimeoutSeconds = newAbsolute, ResumeTimeoutSeconds = newResume };
    }
}