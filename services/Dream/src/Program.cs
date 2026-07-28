
namespace JoinCode.Dream;

/// <summary>
/// Dream 记忆整合插件独立运行入口（仅当 Dream 作为独立 exe 发布时使用）
/// 当前 Dream 以 Library 形式被 JoinCode 引用，此入口不再生效
/// </summary>
internal static class DreamEntryPoint
{
    /// <summary>
    /// 独立运行入口点
    /// </summary>
    internal static async Task<int> RunAsync(string[] args)
    {
        var parseResult = DreamCliArgParser.Parse(args);

        if (args.Length == 0 || parseResult.Help)
        {
            PrintUsage();
            return 0;
        }

        var fs = new IO.FileSystem.PhysicalFileSystem();
        var command = args[0];
        var projectDir = parseResult.Project ?? fs.GetCurrentDirectory();
        var force = parseResult.Force;

        var hostBuilder = Host.CreateDefaultBuilder(args);

        hostBuilder.ConfigureServices((context, services) =>
        {
            var config = new ProviderConfig();
            var envProvider = Environment.GetEnvironmentVariable(JccEnvVar.Provider.ToValue());
            var envApiKey = Environment.GetEnvironmentVariable(JccEnvVar.ApiKey.ToValue())
                ?? Environment.GetEnvironmentVariable(ProviderEnvVar.OpenAiApiKey.ToValue());
            var envModelId = Environment.GetEnvironmentVariable(JccEnvVar.ModelId.ToValue());
            var envEndpoint = Environment.GetEnvironmentVariable(JccEnvVar.Endpoint.ToValue());

            if (!string.IsNullOrEmpty(envProvider)) config.Provider = envProvider;
            if (!string.IsNullOrEmpty(envApiKey)) config.ApiKey = envApiKey;
            if (!string.IsNullOrEmpty(envModelId)) config.ModelId = envModelId;
            if (!string.IsNullOrEmpty(envEndpoint)) config.Endpoint = envEndpoint;

            services.AddLlmServices(config);
            services.AddSingleton<IFileOperationService>(sp =>
            {
                var fs = sp.GetRequiredService<IFileSystem>();
                var logger = sp.GetService<ILogger<IO.FileOperationService>>();
                return new IO.FileOperationService(fs, new FileOperationConfig(), logger);
            });

            // 注册配置（AutoDreamConfig 无 [Register]，需手动注册）
            services.AddSingleton<AutoDreamConfig>(sp =>
            {
                return AutoDreamConfigBuilder.Create()
                    .WithProjectDir(projectDir)
                    .WithMinSessions(2)
                    .Build();
            });

            // 以下服务已通过 [Register] 特性自动注册（在 Sync 项目编译时），
            // 但此独立 exe 不引用源码生成器，需手动注册：
            services.AddSingleton<IChatCompletionClient>(sp =>
            {
                var kernel = sp.GetRequiredService<IChatClient>();
                return new ChatCompletionClient(kernel);
            });

            services.AddSingleton<ISessionScanner>(sp =>
            {
                var cfg = sp.GetRequiredService<AutoDreamConfig>();
                var fs = sp.GetRequiredService<IFileSystem>();
                return new DefaultSessionScanner(cfg, fs);
            });

            services.AddSingleton<IDreamTaskPersistence>(sp =>
            {
                var cfg = sp.GetRequiredService<AutoDreamConfig>();
                var fileOp = sp.GetRequiredService<IFileOperationService>();
                var logger = sp.GetService<ILogger<JsonFileDreamTaskPersistence>>();
                return new JsonFileDreamTaskPersistence(cfg, fileOp, logger);
            });

            services.AddSingleton<IDreamTaskRegistry, PersistentDreamTaskRegistry>();
            services.AddSingleton<IDreamFeature, DreamFeature>();
        });

        using var host = hostBuilder.Build();

        await host.StartAsync().ConfigureAwait(false);

        var dreamFeature = host.Services.GetRequiredService<IDreamFeature>();

        try
        {
            switch (command)
            {
                case "run":
                    {
                        var request = new DreamRequest(Force: force);
                        var result = await dreamFeature.ExecuteAsync(request).ConfigureAwait(false);
                        Console.WriteLine(result.IsSuccess ? result.Content : $"Error: {result.Content}");
                        return result.IsSuccess ? 0 : 1;
                    }
                case "tasks":
                    {
                        var tasks = await dreamFeature.ListTasksAsync().ConfigureAwait(false);
                        foreach (var (id, task) in tasks)
                        {
                            Console.WriteLine($"  {id}  {task.Status}  {task.Description}  {task.StartTime:yyyy-MM-dd HH:mm}");
                        }
                        return 0;
                    }
                case "status" when args.Length > 1:
                    {
                        var taskId = args[1];
                        var task = await dreamFeature.GetTaskStatusAsync(taskId).ConfigureAwait(false);
                        if (task == null)
                        {
                            Console.WriteLine($"Task not found: {taskId}");
                            return 1;
                        }
                        Console.WriteLine($"  ID:     {task.Id}");
                        Console.WriteLine($"  Status: {task.Status}");
                        Console.WriteLine($"  Phase:  {task.Phase}");
                        Console.WriteLine($"  Start:  {task.StartTime:yyyy-MM-dd HH:mm}");
                        return 0;
                    }
                case "kill" when args.Length > 1:
                    {
                        await dreamFeature.KillTaskAsync(args[1]).ConfigureAwait(false);
                        Console.WriteLine($"Task {args[1]} killed.");
                        return 0;
                    }
                default:
                    Console.WriteLine($"Unknown command: {command}");
                    PrintUsage();
                    return 1;
            }
        }
        finally
        {
            await host.StopAsync().ConfigureAwait(false);
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("dream - JoinCode 记忆整合插件");
        Console.WriteLine();
        Console.WriteLine("Usage: dream <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  run     Execute dream consolidation");
        Console.WriteLine("  tasks   List all dream tasks");
        Console.WriteLine("  status  Show task status <taskId>");
        Console.WriteLine("  kill    Kill a running task <taskId>");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --project, -p <path>  Project directory");
        Console.WriteLine("  --force, -f           Force execution");
        Console.WriteLine();
        Console.WriteLine("Environment Variables:");
        Console.WriteLine("  JCC_PROVIDER    LLM provider (openai/azure/anthropic)");
        Console.WriteLine("  JCC_API_KEY     API key");
        Console.WriteLine("  JCC_MODEL_ID    Model ID");
        Console.WriteLine("  JCC_ENDPOINT    API endpoint");
    }
}
