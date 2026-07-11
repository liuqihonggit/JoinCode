using JoinCode.Abstractions.Models.Policy;

namespace JoinCode.ChatCommands.Bridge;

/// <summary>
/// remote-control 子命令入口 — 对齐 TS 端 cli.tsx 快速路径
/// 用法: jcc remote-control [options]
/// 别名: jcc rc [options], jcc remote [options], jcc bridge [options]
/// </summary>
public sealed class BridgeMainCommand
{
    private const string PolicyActionAllowRemoteControl = "allow_remote_control";
    private const string ConfigKeyRemoteDialogSeen = "remoteDialogSeen";
    private const string TokenProviderAnthropic = "anthropic";
    private const string EnvJccApiKey = "JCC_API_KEY";
    private const string EnvOAuthToken = "CLAUDE_CODE_OAUTH_TOKEN";
    private const string EnvSessionAccessToken = "CLAUDE_CODE_SESSION_ACCESS_TOKEN";

    private readonly IServiceProvider? _services;
    private readonly IFileSystem _fs;
    private readonly IProcessService _processService;
    private readonly IRemotePolicyService? _policyService;
    private readonly ITokenStorage? _tokenStorage;
    private readonly IConfigurationService? _configService;
    private readonly ILogger<BridgeMainCommand>? _logger;

    public BridgeMainCommand(
        IServiceProvider? services,
        IFileSystem fs,
        IProcessService processService,
        IRemotePolicyService? policyService = null,
        ITokenStorage? tokenStorage = null,
        IConfigurationService? configService = null,
        ILogger<BridgeMainCommand>? logger = null)
    {
        _services = services;
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _policyService = policyService;
        _tokenStorage = tokenStorage;
        _configService = configService;
        _logger = logger;
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
        if (!await IsPolicyAllowedAsync(ct).ConfigureAwait(false))
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
    /// fail-open: 无策略服务或异常时允许
    /// 决策: 服务不可达视为允许（避免阻塞合法用户）
    /// </summary>
    internal async Task<bool> IsPolicyAllowedAsync(CancellationToken ct = default)
    {
        if (_policyService is null)
        {
            _logger?.LogDebug("PolicyService 未注入，fail-open 允许远程控制。");
            return true;
        }

        try
        {
            var result = await _policyService
                .EvaluateAsync(PolicyActionAllowRemoteControl, context: null, ct)
                .ConfigureAwait(false);
            if (!result.Allowed)
            {
                _logger?.LogInformation(
                    "策略 {Action} 被拒绝: {Reason}",
                    PolicyActionAllowRemoteControl,
                    result.Reason);
            }
            return result.Allowed;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "策略服务评估失败，fail-open 允许远程控制。");
            return true;
        }
    }

    /// <summary>
    /// 构建 BridgeMainDeps — 从 DI 容器和配置中获取依赖
    /// </summary>
    private BridgeMainDeps? BuildDeps(BridgeMainArgs args)
    {
        // 获取访问令牌（同步上下文桥接异步方法 — BridgeMainDeps 要求 Func<string?>）
        var accessToken = GetAccessTokenAsync().GetAwaiter().GetResult();
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
            GetAccessToken = () => GetAccessTokenAsync().GetAwaiter().GetResult(),
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
            GetAccessToken = () => GetAccessTokenAsync().GetAwaiter().GetResult(),
            GetBaseUrl = () => baseUrl,
            CheckRemoteDialogAccepted = () => CheckRemoteDialogAcceptedAsync().GetAwaiter().GetResult(),
            MarkRemoteDialogSeen = () => MarkRemoteDialogSeenAsync().GetAwaiter().GetResult(),
            PermissionMode = args.PermissionMode,
        };
    }

    /// <summary>
    /// 获取访问令牌 — 对齐 TS 端: getBridgeAccessToken()
    /// 优先级: JCC_API_KEY env → OAuth Token Storage(未过期) → CLAUDE_CODE_OAUTH_TOKEN env → CLAUDE_CODE_SESSION_ACCESS_TOKEN env
    /// 决策: Token 过期不自动刷新，回退到环境变量（避免在命令入口触发 OAuth 流程）
    /// </summary>
    internal async Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
    {
        // 1. 环境变量 JCC_API_KEY
        var envToken = Environment.GetEnvironmentVariable(EnvJccApiKey);
        if (!string.IsNullOrEmpty(envToken)) return envToken;

        // 2. OAuth Token Storage（未过期）
        if (_tokenStorage is not null)
        {
            try
            {
                var token = await _tokenStorage
                    .LoadTokenAsync(TokenProviderAnthropic, ct)
                    .ConfigureAwait(false);
                if (token is not null)
                {
                    if (!token.IsExpired)
                    {
                        return token.AccessToken;
                    }
                    _logger?.LogDebug("OAuth Token 已过期，回退到环境变量。");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "加载 OAuth Token 失败，回退到环境变量。");
            }
        }

        // 3. CLAUDE_CODE_OAUTH_TOKEN env
        var oauthToken = Environment.GetEnvironmentVariable(EnvOAuthToken);
        if (!string.IsNullOrEmpty(oauthToken)) return oauthToken;

        // 4. CLAUDE_CODE_SESSION_ACCESS_TOKEN env
        var sessionToken = Environment.GetEnvironmentVariable(EnvSessionAccessToken);
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
    /// 决策: 使用 IConfigurationService 本地配置服务（与 settings.json 对齐）
    /// </summary>
    internal async Task<bool> CheckRemoteDialogAcceptedAsync(CancellationToken ct = default)
    {
        if (_configService is null)
        {
            _logger?.LogDebug("ConfigService 未注入，视为未接受远程对话框。");
            return false;
        }

        try
        {
            var value = await _configService
                .GetAsync(ConfigKeyRemoteDialogSeen, ct)
                .ConfigureAwait(false);
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "读取 remoteDialogSeen 失败，视为未接受。");
            return false;
        }
    }

    /// <summary>
    /// 标记远程控制对话框已接受 — 对齐 TS 端: saveGlobalConfig({remoteDialogSeen: true})
    /// 决策: 通过 IConfigurationService 持久化到 settings.json
    /// </summary>
    internal async Task MarkRemoteDialogSeenAsync(CancellationToken ct = default)
    {
        if (_configService is null)
        {
            _logger?.LogDebug("ConfigService 未注入，跳过标记 remoteDialogSeen。");
            return;
        }

        try
        {
            await _configService
                .SetAsync(ConfigKeyRemoteDialogSeen, "true", ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "保存 remoteDialogSeen 失败。");
        }
    }

    /// <summary>
    /// 暴露 BuildDeps 供单元测试调用 — 仅测试用
    /// </summary>
    internal BridgeMainDeps? BuildDepsForTest(BridgeMainArgs args) => BuildDeps(args);
}
