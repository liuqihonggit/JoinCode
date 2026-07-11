
namespace JoinCode.ChatCommands.Bridge;

/// <summary>
/// remote-control 子命令入口 — 对齐 TS 端 cli.tsx 快速路径
/// 用法: jcc remote-control [options]
/// 别名: jcc rc [options], jcc remote [options], jcc bridge [options]
/// </summary>
public sealed class BridgeMainCommand
{
    private readonly IServiceProvider? _services;
    private readonly IFileSystem _fs;
    private readonly IProcessService _processService;

    public BridgeMainCommand(IServiceProvider? services, IFileSystem fs, IProcessService processService)
    {
        _services = services;
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
    }

    /// <summary>
    /// 执行 remote-control 命令 — 对齐 TS 端 cli.tsx bridge 快速路径
    /// 流程: feature gate → 版本检查 → 策略检查 → bridgeMain(args)
    /// </summary>
    public async Task<int> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        // 1. 解析参数
        var parsed = BridgeMainArgsParser.Parse(args);

        if (parsed.Help)
        {
            TerminalHelper.WriteLine(BridgeMainArgsParser.GetHelpText());
            return 0;
        }

        if (parsed.HasError)
        {
            TerminalHelper.WriteLine($"错误: {parsed.Error}");
            TerminalHelper.NewLine();
            TerminalHelper.WriteLine(BridgeMainArgsParser.GetHelpText());
            return 1;
        }

        // 2. 检查 Bridge 功能是否启用 — 对齐 TS 端: feature('BRIDGE_MODE')
        var bridgeEnabled = IsBridgeEnabled();
        if (!bridgeEnabled)
        {
            TerminalHelper.WriteLine("Bridge 功能未启用。请设置 JCC_BRIDGE_MODE=1 环境变量。");
            return 1;
        }

        // 3. 检查组织策略 — 对齐 TS 端: isPolicyAllowed('allow_remote_control')
        if (!IsPolicyAllowed())
        {
            TerminalHelper.WriteLine("远程控制已被组织策略禁用。");
            return 1;
        }

        // 4. 构建 BridgeMainDeps
        var deps = BuildDeps(parsed);
        if (deps is null)
        {
            TerminalHelper.WriteLine("无法初始化 Bridge 依赖。请确认已登录。");
            return 1;
        }

        // 5. 运行 BridgeMain
        await using var bridgeMain = new BridgeMain(deps);

        // 6. 注册信号处理 — 对齐 TS 端: SIGINT/SIGTERM
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        System.Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            var result = await bridgeMain.RunAsync(parsed, cts.Token).ConfigureAwait(false);

            if (result.HelpText is not null)
            {
                TerminalHelper.WriteLine(result.HelpText);
                return 0;
            }

            if (result.HasError)
            {
                TerminalHelper.WriteLine($"Bridge 错误: {result.Error}");
                return 1;
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            TerminalHelper.WriteLine("Bridge 已取消。");
            await bridgeMain.ShutdownAsync().ConfigureAwait(false);
            return 0;
        }
        catch (BridgeFatalError ex)
        {
            TerminalHelper.WriteLine($"Bridge 致命错误: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// 检查 Bridge 功能是否启用 — 对齐 TS 端: feature('BRIDGE_MODE')
    /// 环境变量 JCC_BRIDGE_MODE=1 启用
    /// </summary>
    private static bool IsBridgeEnabled()
    {
        var envValue = Environment.GetEnvironmentVariable("JCC_BRIDGE_MODE");
        return !string.IsNullOrEmpty(envValue)
            && !envValue.Equals("0", StringComparison.OrdinalIgnoreCase)
            && !envValue.Equals("false", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 检查组织策略 — 对齐 TS 端: isPolicyAllowed('allow_remote_control')
    /// 默认 fail-open: 无策略配置时允许
    /// </summary>
    private static bool IsPolicyAllowed()
    {
        // TODO: 集成 Guard 子系统的策略检查
        // 当前 fail-open: 无策略配置时允许
        return true;
    }

    /// <summary>
    /// 构建 BridgeMainDeps — 从 DI 容器和配置中获取依赖
    /// </summary>
    private BridgeMainDeps? BuildDeps(BridgeMainArgs args)
    {
        // 获取访问令牌
        var accessToken = GetAccessToken();
        if (string.IsNullOrEmpty(accessToken))
        {
            return null;
        }

        // 获取 API 基础 URL
        var baseUrl = GetBaseUrl();

        // 创建 HTTP 客户端
        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        // 创建 API 客户端
        var apiClient = new BridgeApiClient(httpClient, new BridgeApiOptions
        {
            BaseUrl = baseUrl,
            ApiKey = accessToken,
            GetAccessToken = () => GetAccessToken(),
        });

        // 创建子进程生成器
        var spawner = new BridgeSubprocessSpawner(_fs, _processService)
        {
            ExecPath = GetExecPath(),
            WorkingDirectory = Environment.CurrentDirectory,
            Verbose = args.Verbose,
        };

        // 创建指针服务
        var pointerService = new BridgePointerService(_fs);

        return new BridgeMainDeps
        {
            ApiClient = apiClient,
            Spawner = spawner,
            FileSystem = _fs,
            PointerService = pointerService,
            WorkingDirectory = Environment.CurrentDirectory,
            GetAccessToken = () => GetAccessToken(),
            GetBaseUrl = () => baseUrl,
            CheckRemoteDialogAccepted = CheckRemoteDialogAccepted,
            PermissionMode = args.PermissionMode,
        };
    }

    /// <summary>
    /// 获取访问令牌 — 对齐 TS 端: getBridgeAccessToken()
    /// 优先级: 环境变量 → OAuth token → API key
    /// </summary>
    private static string? GetAccessToken()
    {
        // 1. 环境变量
        var envToken = Environment.GetEnvironmentVariable("JCC_API_KEY");
        if (!string.IsNullOrEmpty(envToken)) return envToken;

        // 2. OAuth token (TODO: 集成 Guard 子系统)
        var oauthToken = Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN");
        if (!string.IsNullOrEmpty(oauthToken)) return oauthToken;

        // 3. Session access token
        var sessionToken = Environment.GetEnvironmentVariable("CLAUDE_CODE_SESSION_ACCESS_TOKEN");
        if (!string.IsNullOrEmpty(sessionToken)) return sessionToken;

        return null;
    }

    /// <summary>
    /// 获取 API 基础 URL
    /// </summary>
    private static string GetBaseUrl()
    {
        return Environment.GetEnvironmentVariable("JCC_API_BASE_URL")
            ?? "https://api.anthropic.com";
    }

    /// <summary>
    /// 获取可执行文件路径 — 对齐 TS 端: process.execPath
    /// </summary>
    private static string GetExecPath()
    {
        return Environment.GetEnvironmentVariable("JCC_EXEC_PATH")
            ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
            ?? "jcc";
    }

    /// <summary>
    /// 检查远程控制对话框是否已被接受 — 对齐 TS 端: remoteDialogSeen
    /// </summary>
    private static bool CheckRemoteDialogAccepted()
    {
        // TODO: 集成配置系统检查 remoteDialogSeen
        // 当前默认: 已接受（首次运行时由 BridgeMain.RunAsync 的确认对话框处理）
        return true;
    }
}
