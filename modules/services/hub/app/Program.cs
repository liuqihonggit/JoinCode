namespace JoinCode.Hub;

/// <summary>
/// jcchub.exe 入口点 — 多 Agent 中央服务器独立进程。
/// 加载 settings.json 配置、初始化 Provider 注册表、启动 AgentHubServer。
/// 支持 settings.json 热重载 — IFileSystemWatcher 监听变更后自动重新加载 Provider 配置。
/// </summary>
internal static class Program {
    private const int DefaultPort = 3457;
    private const string DefaultHost = "localhost";

    private static async Task<int> Main(string[] args) {
        var port = DefaultPort;
        var host = DefaultHost;

        for (var i = 0; i < args.Length; i++) {
            if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[i + 1], out var p)) {
                port = p;
                i++;
            } else if (args[i] == "--host" && i + 1 < args.Length) {
                host = args[i + 1];
                i++;
            }
        }

        var fs = FileSystemFactory.Create();
        var settings = await ConfigLoader.LoadSettingsJsonAsync(fs).ConfigureAwait(false);

        // 如果 settings.json 不存在或没有 vendor 配置，从嵌入资源加载默认供应商配置
        if (settings is null || settings.Vendor.Count == 0) {
            settings = LoadDefaultVendorsFromEmbeddedResource();
            Console.WriteLine($"[jcchub] settings.json 无供应商配置，已从嵌入资源加载 {settings.Vendor.Count} 个默认供应商");
        }

        var providerRegistry = ProviderDefinitionRegistry.Create(
            new ModelConfigLoader(),
            fs);

        using var loggerFactory = LoggerFactory.Create(builder => {
            builder.AddSimpleConsole(options => {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
        });
        var logger = loggerFactory.CreateLogger<AgentHubServer>();

        await using var server = new AgentHubServer(
            settings,
            providerRegistry,
            port,
            host,
            logger);

        server.Start();

        Console.WriteLine($"[jcchub] AgentHubServer 已启动 — http://{host}:{port}");
        Console.WriteLine($"[jcchub] Providers: {settings.Vendor.Count}, Agents: 0");
        Console.WriteLine("[jcchub] 按 Ctrl+C 停止服务器");

        // 配置热重载 — IFileSystemWatcher 监听 settings.json 变更
        var settingsPath = Path.Combine(
            AppDataConstants.Paths.JccDirectory,
            AppDataConstants.SettingsFileName);
        var settingsDir = Path.GetDirectoryName(settingsPath) ?? AppDataConstants.Paths.JccDirectory;

        IFileSystemWatcher? watcher = null;
        try {
            if (!fs.DirectoryExists(settingsDir))
                fs.CreateDirectory(settingsDir);
            watcher = fs.Watch(settingsDir, AppDataConstants.SettingsFileName);
            watcher.EnableRaisingEvents = true;
        } catch (Exception ex) {
            logger.LogWarning(ex, "[jcchub] 热重载监听初始化失败，跳过热重载");
        }

        using var reloadCts = new CancellationTokenSource();
        if (watcher is not null) {
            watcher.DebouncedChanged += async (_, _) => {
                try {
                    await server.ReloadProvidersAsync(reloadCts.Token).ConfigureAwait(false);
                } catch (OperationCanceledException) {
                } catch (Exception ex) {
                    logger.LogError(ex, "[jcchub] 热重载失败");
                }
            };
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => {
            e.Cancel = true;
            cts.Cancel();
            reloadCts.Cancel();
        };

        try {
            await Task.Delay(Timeout.Infinite, cts.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) {
        }

        Console.WriteLine("[jcchub] 正在停止服务器...");
        if (watcher is not null)
            await watcher.DisposeAsync().ConfigureAwait(false);
        await server.StopAsync(CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine("[jcchub] 服务器已停止");

        return 0;
    }

    /// <summary>
    /// 从嵌入资源 default_vendors.json 加载默认供应商配置
    /// </summary>
    private static SettingsJson LoadDefaultVendorsFromEmbeddedResource() {
        var assembly = typeof(Program).Assembly;
        var resourceName = "JoinCode.Hub.app.default_vendors.json";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null) {
            Console.WriteLine($"[jcchub] 嵌入资源 {resourceName} 未找到");
            return new SettingsJson();
        }
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var json = reader.ReadToEnd();
        var settings = RelaxedJsonSerializer.Deserialize(json, ConfigJsonContext.Default.SettingsJson);
        return settings ?? new SettingsJson();
    }
}
