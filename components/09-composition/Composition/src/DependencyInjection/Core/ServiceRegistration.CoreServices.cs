
namespace Core.DependencyInjection;

public static partial class ServiceRegistration
{
    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        // HttpClient — [Register] 自动注册（SharedHttpClient）

        // LspServiceDeps — [Register] 自动注册（构造函数参数均为可选 DI 接口）

        // QueryLoopServices — [Register] 自动注册（构造函数参数均为可选 DI 接口）

        // Chat/ChatAdmin/ChatInit 管道 — 由 [RegisterMiddleware] + [Register(IPipelineHook)] + 生成器自动注册

        // IStore<AppState> — [Register(typeof(IStore<AppState>))] 自动注册（AppStateStore）

        services.AddApiClientServices();

        // ICommandClassifier, IShellMiddleware, Shell 中间件管道 — [Register] + [RegisterMiddleware] 自动注册
        services.AddCodeSecurityServices();

        services.AddPromptServices();

        // ISystemPromptProvider — [Register] 自动注册（DefaultSystemPromptProvider）
        // SystemPromptProviderOptions — [Register] 自动注册（SyncSystemPromptProviderOptions）


        // MemdirOptions — 已移至 AddVaultServices 统一注册

        return services;
    }

    public static IServiceCollection AddFileOperationServices(this IServiceCollection services)
    {
        // IFileSystem — 根据 JCC_FILE_SYSTEM_MODE 环境变量决定后端
        // 默认 Physical（真实磁盘），InMemory=纯内存0磁盘IO（调试/E2E测试用）
        // 注意: [Register] 自动注册的 IFileSystem 转发已在此处被覆盖（后注册 wins）
        services.AddEnvSwitch<IFileSystem>(
            JccEnvVar.FileSystemMode, "InMemory",
            _ => new InMemoryFileSystem(),
            sp => sp.GetRequiredService<PhysicalFileSystem>());

        services.AddOptions<FileOperationConfig>()
            .BindConfiguration("Workflow:FileOperation")
            .Validate(config =>
            {
                if (config.MaxReadSize < 1024 || config.MaxReadSize > 1024L * 1024 * 1024) return false;
                if (config.MaxWriteSize < 1024 || config.MaxWriteSize > 1024 * 1024 * 1024) return false;
                if (config.BufferSize < 512 || config.BufferSize > 1024 * 1024) return false;
                if (config.BinaryDetectionBufferSize < 1024 || config.BinaryDetectionBufferSize > 64 * 1024) return false;
                return true;
            }, L.T(StringKey.FileOperationConfigValidationFailed))
            .ValidateOnStart();

        // FileOperationConfig — 直接注册供 FileOperationService 构造函数使用
        // （FileOperationService 有手动构造函数，不使用 [Inject] 生成器）
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<FileOperationConfig>>();
            return options.Value;
        });

        return services;
    }

    public static IServiceCollection AddToolServices(this IServiceCollection services)
    {
        services.AddOptions<ShellExecutionConfig>()
            .BindConfiguration("Workflow:ShellExecution")
            .Validate(config =>
            {
                if (config.MaxOutputBytes < 1024 || config.MaxOutputBytes > 1024 * 1024) return false;
                if (config.DefaultTimeoutSeconds < 1 || config.DefaultTimeoutSeconds > 3600) return false;
                return true;
            }, "ShellExecutionConfig 验证失败")
            .ValidateOnStart();

        // ShellExecutionConfig — 直接注册供 ShellExecutionService 构造函数使用
        // （ShellExecutionService 构造函数取 ShellExecutionConfig 而非 IOptions<>）
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ShellExecutionConfig>>();
            return options.Value;
        });

        // IShellProvider — BashShellProvider / PowerShellShellProvider 由 [Register] 自动注册
        // ShellExecutionService 构造函数直接取具体类型（非 IShellProvider 接口），避免多实现歧义

        return services;
    }

    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        // TelemetryConfig — [Register] 自动注册（无参构造函数从环境变量初始化）

        // P1-5 推广 HttpClientFactory — 启用 IHttpClientFactory（已在 P1-3 卫星项目 aot-httpclientfactory-test 验证 NativeAOT 兼容）
        // 决策: 主程序通过 services.AddHttpClient() 启用 IHttpClientFactory
        // 优势: HttpMessageHandler 生命周期由 IHttpClientFactory 池化管理，避免 socket 耗尽
        // 影响范围: DefaultHttpClientProvider（Real 路径）通过 DI 自动注入 IHttpClientFactory
        services.AddHttpClient();

        // IHttpClientProvider — 根据 JCC_HTTP_MODE 环境变量决定后端
        // 默认 Real（真实网络），Mock=拦截请求返回预设响应（调试/E2E测试用）
        // 注意: [Register] 自动注册的 DefaultHttpClientProvider 已在此处被覆盖（后注册 wins）
        services.AddEnvSwitch<IHttpClientProvider>(
            JccEnvVar.HttpMode, "Mock",
            _ => new Infrastructure.Http.MockHttpClientProvider(),
            sp => sp.GetRequiredService<Infrastructure.Http.DefaultHttpClientProvider>());

        // INotificationService — 根据 JCC_NOTIFICATION_MODE 环境变量决定后端
        // 默认 Windows（气泡通知），Console=纯日志输出（调试用）
        services.AddEnvSwitch<INotificationService>(
            JccEnvVar.NotificationMode, "Console",
            _ => new ConsoleNotificationService());

        // IBrowserAutomationService — 根据 JCC_BROWSER_AUTOMATION 环境变量决定后端
        // 默认 None（NoOp），Puppeteer=启用浏览器自动化
        var browserMode = EnvHelper.Get(JccEnvVar.BrowserAutomation);
        if (!string.Equals(browserMode, "Puppeteer", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IBrowserAutomationService>(sp =>
                EnvSwitchRegistrar.TraceFactory(_ => new NoOpBrowserAutomationService(), "IBrowserAutomationService", "NoOp", sp));
        }

        // ITaskService — 根据 JCC_TASK_SERVICE_MODE 环境变量决定后端
        // 默认 File（文件持久化），Memory=纯内存（调试/E2E测试用）
        services.AddEnvSwitch<ITaskService>(
            JccEnvVar.TaskServiceMode, "Memory",
            sp => sp.GetRequiredService<TaskService>());

        // IClockService — 根据 JCC_CLOCK_MODE 环境变量决定后端
        // 默认 Physical（真实系统时间），Fake=可控时间（调试/E2E测试用）
        // 注意: [Register] 自动注册的 PhysicalClockService 已在此处被覆盖（后注册 wins）
        services.AddEnvSwitch<IClockService>(
            JccEnvVar.ClockMode, "Fake",
            _ => new Infrastructure.Time.FakeClockService(),
            sp => sp.GetRequiredService<Infrastructure.Time.PhysicalClockService>());

        // IProcessService — 根据 JCC_PROCESS_MODE 环境变量决定后端
        // 默认 Physical（真实进程），NoOp=禁止所有进程操作（调试/E2E测试用）
        services.AddEnvSwitch<IProcessService>(
            JccEnvVar.ProcessMode, "NoOp",
            _ => new IO.ProcessService.NoOpProcessService(),
            sp => new IO.ProcessService.PhysicalProcessService(sp.GetService<ILogger<IO.ProcessService.PhysicalProcessService>>()));

        // IConsoleOutput — 根据 JCC_CONSOLE_MODE 环境变量决定后端
        // 默认 Physical（真实控制台），NoOp=静默所有输出（E2E测试/CI用）
        // 注意: [Register] 自动注册的 PhysicalConsoleOutput 已在此处被覆盖（后注册 wins）
        services.AddEnvSwitch<IConsoleOutput>(
            JccEnvVar.ConsoleMode, "NoOp",
            _ => new Infrastructure.IO.NoOpConsoleOutput(),
            sp => sp.GetRequiredService<Infrastructure.IO.PhysicalConsoleOutput>());

        return services;
    }
}
