
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
        var fsMode = EnvHelper.Get(JccEnvVar.FileSystemMode);
        if (string.Equals(fsMode, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IFileSystem>(sp =>
            {
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] + IFileSystem (InMemory)");
                var svc = new InMemoryFileSystem();
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] - IFileSystem (InMemory)");
                return svc;
            });
        }
        else
        {
            // 默认 Physical — 覆盖 [Register] 自动注册的转发，直接解析 PhysicalFileSystem
            services.AddSingleton<IFileSystem>(sp =>
            {
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] + IFileSystem (Physical)");
                var svc = sp.GetRequiredService<PhysicalFileSystem>();
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] - IFileSystem (Physical)");
                return svc;
            });
        }

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
        var httpMode = EnvHelper.Get(JccEnvVar.HttpMode);
        if (string.Equals(httpMode, "Mock", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IHttpClientProvider>(sp =>
            {
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] + IHttpClientProvider (Mock)");
                var svc = new Infrastructure.Http.MockHttpClientProvider();
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] - IHttpClientProvider (Mock)");
                return svc;
            });
        }
        else
        {
            // 默认 Real — 覆盖 [Register] 自动注册，直接创建 DefaultHttpClientProvider
            services.AddSingleton<IHttpClientProvider>(sp =>
            {
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] + IHttpClientProvider (Real)");
                var svc = sp.GetRequiredService<Infrastructure.Http.DefaultHttpClientProvider>();
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] - IHttpClientProvider (Real)");
                return svc;
            });
        }

        // INotificationService — 根据 JCC_NOTIFICATION_MODE 环境变量决定后端
        // 默认 Windows（气泡通知），Console=纯日志输出（调试用）
        var notificationMode = EnvHelper.Get(JccEnvVar.NotificationMode);
        if (string.Equals(notificationMode, "Console", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<INotificationService>(sp =>
            {
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] + INotificationService (Console)");
                var svc = new ConsoleNotificationService();
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] - INotificationService (Console)");
                return svc;
            });
        }

        // IBrowserAutomationService — 根据 JCC_BROWSER_AUTOMATION 环境变量决定后端
        // 默认 None（NoOp），Puppeteer=启用浏览器自动化
        var browserMode = EnvHelper.Get(JccEnvVar.BrowserAutomation);
        if (!string.Equals(browserMode, "Puppeteer", StringComparison.OrdinalIgnoreCase))
        {
            // NoOp — 覆盖 [Register] 自动注册的 Puppeteer 实现
            services.AddSingleton<IBrowserAutomationService>(sp =>
            {
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] + IBrowserAutomationService (NoOp)");
                var svc = new NoOpBrowserAutomationService();
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] - IBrowserAutomationService (NoOp)");
                return svc;
            });
        }

        // ITaskService — 根据 JCC_TASK_SERVICE_MODE 环境变量决定后端
        // 默认 File（文件持久化），Memory=纯内存（调试/E2E测试用）
        var taskMode = EnvHelper.Get(JccEnvVar.TaskServiceMode);
        if (string.Equals(taskMode, "Memory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<ITaskService>(sp =>
            {
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] + ITaskService (Memory)");
                var svc = sp.GetRequiredService<TaskService>();
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] - ITaskService (Memory)");
                return svc;
            });
        }

        // IClockService — 根据 JCC_CLOCK_MODE 环境变量决定后端
        // 默认 Physical（真实系统时间），Fake=可控时间（调试/E2E测试用）
        // 注意: [Register] 自动注册的 PhysicalClockService 已在此处被覆盖（后注册 wins）
        var clockMode = EnvHelper.Get(JccEnvVar.ClockMode);
        if (string.Equals(clockMode, "Fake", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IClockService>(sp =>
            {
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] + IClockService (Fake)");
                var svc = new Infrastructure.Time.FakeClockService();
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] - IClockService (Fake)");
                return svc;
            });
        }
        else
        {
            services.AddSingleton<IClockService>(sp =>
            {
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] + IClockService (Physical)");
                var svc = sp.GetRequiredService<Infrastructure.Time.PhysicalClockService>();
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] - IClockService (Physical)");
                return svc;
            });
        }

        // IProcessService — 根据 JCC_PROCESS_MODE 环境变量决定后端
        // 默认 Physical（真实进程），NoOp=禁止所有进程操作（调试/E2E测试用）
        var processMode = EnvHelper.Get(JccEnvVar.ProcessMode);
        if (string.Equals(processMode, "NoOp", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IProcessService>(sp =>
            {
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] + IProcessService (NoOp)");
                var svc = new IO.ProcessService.NoOpProcessService();
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] - IProcessService (NoOp)");
                return svc;
            });
        }
        else
        {
            services.AddSingleton<IProcessService>(sp =>
            {
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] + IProcessService (Physical)");
                var logger = sp.GetService<ILogger<IO.ProcessService.PhysicalProcessService>>();
                var svc = new IO.ProcessService.PhysicalProcessService(logger);
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] - IProcessService (Physical)");
                return svc;
            });
        }

        // IConsoleOutput — 根据 JCC_CONSOLE_MODE 环境变量决定后端
        // 默认 Physical（真实控制台），NoOp=静默所有输出（E2E测试/CI用）
        // 注意: [Register] 自动注册的 PhysicalConsoleOutput 已在此处被覆盖（后注册 wins）
        var consoleMode = EnvHelper.Get(JccEnvVar.ConsoleMode);
        if (string.Equals(consoleMode, "NoOp", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IConsoleOutput>(sp =>
            {
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] + IConsoleOutput (NoOp)");
                var svc = new Infrastructure.IO.NoOpConsoleOutput();
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] - IConsoleOutput (NoOp)");
                return svc;
            });
        }
        else
        {
            services.AddSingleton<IConsoleOutput>(sp =>
            {
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] + IConsoleOutput (Physical)");
                var svc = sp.GetRequiredService<Infrastructure.IO.PhysicalConsoleOutput>();
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] - IConsoleOutput (Physical)");
                return svc;
            });
        }

        return services;
    }
}
