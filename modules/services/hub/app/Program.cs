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
        settings ??= new SettingsJson();

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

        await using var watcher = fs.Watch(settingsDir, AppDataConstants.SettingsFileName);
        watcher.EnableRaisingEvents = true;

        using var reloadCts = new CancellationTokenSource();
        watcher.DebouncedChanged += async (_, _) => {
            try {
                await server.ReloadProvidersAsync(reloadCts.Token).ConfigureAwait(false);
            } catch (OperationCanceledException) {
            } catch (Exception ex) {
                logger.LogError(ex, "[jcchub] 热重载失败");
            }
        };

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
        await server.StopAsync(CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine("[jcchub] 服务器已停止");

        return 0;
    }
}
