
namespace Core.Bridge;

/// <summary>
/// Bridge 独立进程编排器 — 对齐 TS 端 bridgeMain.ts
/// 核心职责: 参数解析 → OAuth认证 → 环境注册 → 工作轮询 → 子进程管理 → 优雅关闭
/// </summary>
public sealed partial class BridgeMain : ServiceEntity
{
    internal static readonly FrozenSet<string> ValidPermissionModes = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, "default", "plan", "auto-accept", "bubble");

    internal readonly BridgeMainDeps _deps;
    internal readonly ILogger? _logger;
    internal readonly IFileSystem _fs;
    internal readonly IClockService _clock;

    // 活跃会话跟踪 — 对齐 TS 端 runBridgeLoop 的 7 个 Map + 3 个 Set
    internal readonly BridgeSessionTracker _tracker = new();
    internal readonly List<Task> _pendingCleanups = new(); // 待清理任务 — 对齐 TS 端 pendingCleanups
    internal readonly AsyncLock _cleanupLock = new(); // 替代 lock — JCC4001 分析器要求

    // 退避状态 — 对齐 TS 端 BackoffConfig + 双轨退避
    internal readonly BridgeBackoffStrategy _backoff;

    // 崩溃恢复指针管理
    internal readonly BridgePointerManager _pointerManager;

    // 工作 API 封装
    internal readonly BridgeWorkApiClient _workApi;

    // 生命周期
    internal CancellationTokenSource? _loopCts;
    internal Task? _loopTask;
    internal int _asyncDisposed;
    internal int _isShuttingDown;
    internal bool _isResuming; // 对齐 TS 端: resume 模式标记 — 可恢复关闭时跳过 archive+deregister
    internal bool _fatalExit; // 对齐 TS 端: fatalExit — 致命错误后跳过 resume 提示

    // Token 刷新调度器 — 对齐 TS 端 createTokenRefreshScheduler + v1/v2 分支
    internal BridgeTokenRefreshScheduler? _tokenRefresh;

    // 遥测 — 对齐 TS 端 logEvent/logEventAsync (tengu_bridge_*)
    internal readonly ITelemetryService? _telemetry;
    internal DateTime _loopStartTime; // 主循环启动时间 — 用于计算 loop_duration_ms
    internal readonly MiddlewarePipeline<HandleWorkContext>? _handleWorkPipeline;
    internal readonly MiddlewarePipeline<ShutdownContext>? _shutdownPipeline;
    internal readonly MiddlewarePipeline<BridgeRunContext>? _runPipeline;

    /// <summary>当前活跃会话数</summary>
    public int ActiveSessionCount => _tracker.Sessions.Count;

    /// <summary>是否正在运行</summary>
    public bool IsRunning => _loopTask is { IsCompleted: false };

    /// <summary>环境 ID</summary>
    public string? EnvironmentId { get; internal set; }

    /// <summary>获取环境 ID，未注册时抛出异常</summary>
    internal string GetEnvironmentId() =>
        EnvironmentId ?? throw new InvalidOperationException("Environment not registered yet. Call RegisterEnvironmentAsync first.");

    /// <summary>环境密钥</summary>
    public string? EnvironmentSecret { get; internal set; }

    internal readonly INetworkConnectivityService? _networkService;

    // 职责类
    private readonly BridgeShutdownHandler _shutdownHandler;
    private readonly BridgeEnvironmentRegistrar _environmentRegistrar;
    private readonly BridgeLoopRunner _loopRunner;
    private readonly BridgeWorkHandler _workHandler;
    private readonly BridgeSessionMonitor _sessionMonitor;

    /// <summary>
    /// 构造 Bridge 主编排器
    /// </summary>
    /// <param name="deps">BridgeMain 依赖包</param>
    /// <param name="handleWorkPipeline">工作处理中间件管道（可选）</param>
    /// <param name="shutdownPipeline">关闭中间件管道（可选）</param>
    /// <param name="runPipeline">运行中间件管道（可选）</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <param name="clock">时钟服务（可选，默认系统时钟）</param>
    /// <param name="networkService">网络连通性服务（可选）</param>
    /// <param name="giveUpThreshold">退避放弃阈值（可选）</param>
    public BridgeMain(
        BridgeMainDeps deps,
        MiddlewarePipeline<HandleWorkContext>? handleWorkPipeline = null,
        MiddlewarePipeline<ShutdownContext>? shutdownPipeline = null,
        MiddlewarePipeline<BridgeRunContext>? runPipeline = null,
        ILogger? logger = null,
        IClockService? clock = null,
        INetworkConnectivityService? networkService = null,
        TimeSpan? giveUpThreshold = null)
        : base(nameof(BridgeMain))
    {
        _deps = deps ?? throw new ArgumentNullException(nameof(deps));
        _logger = logger;
        _fs = deps.FileSystem;
        _telemetry = deps.TelemetryService;
        _clock = clock ?? SystemClockService.Instance;
        _backoff = new BridgeBackoffStrategy(_clock, _logger, giveUpThreshold);
        _pointerManager = new BridgePointerManager(deps.PointerService, _logger);
        _workApi = new BridgeWorkApiClient(deps.ApiClient, _logger);
        _handleWorkPipeline = handleWorkPipeline;
        _shutdownPipeline = shutdownPipeline;
        _runPipeline = runPipeline;
        _networkService = networkService;
        _shutdownHandler = new BridgeShutdownHandler(this);
        _environmentRegistrar = new BridgeEnvironmentRegistrar(this);
        _loopRunner = new BridgeLoopRunner(this);
        _workHandler = new BridgeWorkHandler(this);
        _sessionMonitor = new BridgeSessionMonitor(this);
    }

    /// <summary>
    /// 验证 HTTPS URL — RunAsync/RunHeadlessAsync 共享
    /// </summary>
    /// <returns>null 表示通过，否则返回错误消息</returns>
    private static string? ValidateHttpsUrl(string baseUrl)
    {
        if (!baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !baseUrl.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) &&
            !baseUrl.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            return "Bridge requires HTTPS (or localhost).";
        }
        return null;
    }

    /// <summary>
    /// 验证访问令牌 — RunAsync/RunHeadlessAsync 共享
    /// </summary>
    /// <returns>访问令牌；null 表示无可用令牌</returns>
    private string? GetValidAccessToken()
    {
        return _deps.GetAccessToken();
    }

    /// <summary>
    /// 注册 Bridge 环境 — 委托给 BridgeEnvironmentRegistrar
    /// </summary>
    private async Task<BridgeEnvironmentRegistrationResponse> RegisterEnvironmentAsync(
        BridgeConfig config, CancellationToken ct)
        => await _environmentRegistrar.RegisterEnvironmentAsync(config, ct).ConfigureAwait(false);

    /// <summary>
    /// 尝试创建初始会话 — 委托给 BridgeEnvironmentRegistrar
    /// </summary>
    private async Task<string?> TryCreateInitialSessionAsync(
        string? name, string? permissionMode, BridgeConfig config, CancellationToken ct)
        => await _environmentRegistrar.TryCreateInitialSessionAsync(name, permissionMode, config, ct).ConfigureAwait(false);

    /// <summary>
    /// 启动 Bridge 主循环 — 对齐 TS 端 bridgeMain()
    /// 流程: 参数验证 → OAuth → 环境注册 → 进入 runBridgeLoop
    /// </summary>
    public async Task<BridgeMainResult> RunAsync(BridgeMainArgs args, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (_runPipeline is not null)
        {
            var ctx = new BridgeRunContext
            {
                Args = args,
                CancellationToken = ct,
            };
            await _runPipeline.ExecuteAsync(ctx, ct).ConfigureAwait(false);

            if (ctx.EarlyResult is not null)
            {
                return ctx.EarlyResult;
            }

            return await RunBridgeFromContextAsync(ctx, ct).ConfigureAwait(false);
        }

        return await RunDirectAsync(args, ct).ConfigureAwait(false);
    }

    private async Task<BridgeMainResult> RunBridgeFromContextAsync(BridgeRunContext ctx, CancellationToken ct)
    {
        _isResuming = ctx.IsResuming;
        _pointerManager.ResumePointerDir = ctx.ResumePointerDir;

        var config = BuildConfig(ctx.Args, ctx.BaseUrl ?? throw new InvalidOperationException("BaseUrl is required"), ctx.ReuseEnvironmentId, ctx.EffectiveSpawnMode, ctx.IsResuming, ctx.SpawnModeSource);

        try
        {
            await RegisterEnvironmentAsync(config, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return HandleRegistrationError(ex);
        }

        _logger?.LogInformation("BridgeMain: environment registered, ID={EnvId}", EnvironmentId);

        TelemetryCount("tengu_bridge_started", new Dictionary<string, string>
        {
            ["max_sessions"] = (ctx.Args.Capacity ?? (ctx.EffectiveSpawnMode == BridgeSpawnMode.SingleSession ? 1 : 5)).ToString(),
            ["has_debug_file"] = (ctx.Args.DebugFile is not null).ToString(),
            ["sandbox"] = ctx.Args.Sandbox.ToString(),
            ["debuglog"] = ctx.Args.DebugLog.ToString(),
            ["heartbeat_interval_ms"] = (_deps.PollConfig?.HeartbeatIntervalMs ?? 30000).ToString(),
            ["spawn_mode"] = ctx.EffectiveSpawnMode?.ToValue() ?? "single-session",
            ["spawn_mode_source"] = ctx.SpawnModeSource.ToValue(),
            ["worktree_available"] = (_deps.IsWorktreeAvailable?.Invoke() ?? false).ToString(),
        });

        string? initialSessionId = ctx.ResumeSessionId;
        if (initialSessionId is null)
        {
            var createdSessionId = await TryCreateInitialSessionAsync(
                ctx.Args.Name, _deps.PermissionMode, config, ct).ConfigureAwait(false);
            if (createdSessionId is not null)
            {
                initialSessionId = createdSessionId;
            }
        }

        if (_deps.RegisterKeyboardListener is not null)
        {
            _deps.RegisterKeyboardListener(OnKeyboardInputAsync);
        }

        if (_deps.GetAccessToken is not null)
        {
            _tokenRefresh = new BridgeTokenRefreshScheduler(
                new TokenRefreshOptions
                {
                    GetAccessToken = _deps.GetAccessToken,
                    OnRefresh = (sessionId, oauthToken) =>
                    {
                        if (_tracker.Sessions.IsV2(sessionId))
                        {
                            _logger?.LogDebug("BridgeMain: refreshing v2 session {SessionId} via reconnectSession", sessionId);
                            ReconnectV2SessionFireAndForget(sessionId);
                        }
                        else
                        {
                            UpdateV1SessionTokenFireAndForget(sessionId, oauthToken);
                        }
                    },
                    Label = "bridge",
                    Logger = _logger,
                });
        }
        else
        {
            _tokenRefresh = _deps.TokenRefreshScheduler;
        }

        _deps.BridgeLogger?.PrintBanner(config, GetEnvironmentId());
        _deps.BridgeLogger?.UpdateSessionCount(0, config.MaxSessions, config.SpawnMode);
        if (initialSessionId is not null)
        {
            var compatId = GetCompatId(initialSessionId);
            _deps.BridgeLogger?.SetAttached(compatId);
        }
        if (config.GitRepoUrl is not null || config.Branch is not null)
        {
            var repoName = ExtractRepoName(config.GitRepoUrl, _deps.WorkingDirectory);
            _deps.BridgeLogger?.SetRepoInfo(repoName, config.Branch ?? "");
        }

        using var statusTimer = new Timer(_ => UpdateStatusDisplay(config), null,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loopTask = RunBridgeLoopAsync(config, initialSessionId, _loopCts.Token);
        try
        {
            await _loopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger?.LogInformation("BridgeMain: loop cancelled");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BridgeMain: loop failed");
            return new BridgeMainResult { Error = $"Loop failed: {ex.Message}" };
        }

        return new BridgeMainResult { Completed = true };
    }

    private async Task<BridgeMainResult> RunDirectAsync(BridgeMainArgs args, CancellationToken ct)
    {
        // 1. 帮助检查
        if (args.Help)
        {
            return new BridgeMainResult { HelpText = BridgeMainArgsParser.GetHelpText() };
        }

        // 2. 参数错误检查
        if (args.HasError)
        {
            return new BridgeMainResult { Error = args.Error };
        }

        // 2.5 permissionMode 早期验证 — 对齐 TS 端: PERMISSION_MODES 校验
        if (_deps.PermissionMode is not null)
        {
            if (!ValidPermissionModes.Contains(_deps.PermissionMode))
            {
                return new BridgeMainResult { Error = $"Invalid permission mode '{_deps.PermissionMode}'. Valid modes: default, plan, auto-accept, bubble" };
            }
        }

        // 3. OAuth 认证 — 对齐 TS 端: if (!getBridgeAccessToken())
        var accessToken = GetValidAccessToken();
        if (accessToken is null)
        {
            _logger?.LogDebug("BridgeMain: no access token — skipping");
            return new BridgeMainResult { Error = "No access token available. Please login first." };
        }

        // 4. 首次远程确认 — 对齐 TS 端: remoteDialogSeen 检查 + readline y/n 对话框
        var remoteDialogSeen = _deps.CheckRemoteDialogAccepted?.Invoke() ?? true;
        if (!remoteDialogSeen)
        {
            // 对齐 TS 端: if (!getGlobalConfig().remoteDialogSeen) → 弹出 readline 对话框
            if (_deps.RemoteControlDialog is not null)
            {
                var accepted = await _deps.RemoteControlDialog(ct).ConfigureAwait(false);
                // 无论用户回答什么，都保存 remoteDialogSeen=true 防止下次再问
                _deps.MarkRemoteDialogSeen?.Invoke();
                if (!accepted)
                {
                    _logger?.LogDebug("BridgeMain: remote control declined by user");
                    return new BridgeMainResult { Error = "Remote control not accepted." };
                }
            }
            else
            {
                // 无对话框回调（非交互模式）: 直接拒绝
                _logger?.LogDebug("BridgeMain: remote control not accepted — skipping");
                return new BridgeMainResult { Error = "Remote control not accepted." };
            }
        }

        // 5. HTTPS 检查 — 对齐 TS 端: 非localhost必须HTTPS
        var baseUrl = _deps.GetBaseUrl();
        var httpsError = ValidateHttpsUrl(baseUrl);
        if (httpsError is not null)
        {
            _logger?.LogDebug("BridgeMain: non-HTTPS URL — skipping");
            return new BridgeMainResult { Error = httpsError };
        }

        // 6. --continue 恢复 — 对齐 TS 端: readBridgePointerAcrossWorktrees
        string? resumeSessionId = null;
        string? reuseEnvironmentId = null;
        if (args.ContinueSession)
        {
            var found = await _deps.PointerService.ReadAcrossWorktreesAsync(
                _deps.WorkingDirectory, ct).ConfigureAwait(false);
            if (found is not null)
            {
                var (pointerWithAge, pointerDir) = found.Value;
                resumeSessionId = pointerWithAge.Pointer.SessionId;
                reuseEnvironmentId = pointerWithAge.Pointer.EnvironmentId;
                _pointerManager.ResumePointerDir = pointerDir; // 记录指针来源目录 — 恢复失败时清除正确的指针
                var ageMin = Math.Round(pointerWithAge.AgeMs / 60_000.0);
                var ageStr = ageMin < 60 ? $"{ageMin}m" : $"{Math.Round(ageMin / 60.0)}h";
                var fromWt = pointerDir != _deps.WorkingDirectory ? $" from worktree {pointerDir}" : "";
                _logger?.LogInformation("BridgeMain: resuming session {SessionId} ({Age} ago){FromWt}",
                    resumeSessionId, ageStr, fromWt);
            }
            else
            {
                _logger?.LogDebug("BridgeMain: --continue but no valid pointer found in this directory or its worktrees");
            }
        }
        else if (args.SessionId is not null)
        {
            resumeSessionId = args.SessionId;
            // 对齐 TS 端: getBridgeSession → reuseEnvironmentId
            // 通过 API 获取 session 的 environment_id，用于 idempotent 注册
            try
            {
                var envId = await _deps.ApiClient.GetBridgeSessionEnvironmentIdAsync(
                    resumeSessionId, ct).ConfigureAwait(false);
                if (envId is not null)
                {
                    reuseEnvironmentId = envId;
                    _logger?.LogInformation("BridgeMain: resuming session {SessionId} on environment {EnvId}",
                        resumeSessionId, envId);
                }
                else
                {
                    _logger?.LogDebug("BridgeMain: session {SessionId} has no environment_id, will register fresh", resumeSessionId);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "BridgeMain: getBridgeSession failed for {SessionId} (non-fatal)", resumeSessionId);
            }
        }

        // 7. Spawn 模式选择 — 对齐 TS 端: spawnMode + spawnModeSource 优先级链
        // 优先级: resume > flag > saved > gate_default
        // 对齐 TS 端: GrowthBook gate tengu_ccr_bridge_multi_session
        var multiSessionEnabled = _deps.IsMultiSessionSpawnEnabled?.Invoke() ?? false;
        var effectiveSpawnMode = args.SpawnMode;
        var spawnModeSource = BridgeSpawnModeSource.GateDefault; // 默认兜底

        if (resumeSessionId is not null)
        {
            // 优先级1: resume — 恢复会话强制 single-session
            effectiveSpawnMode = BridgeSpawnMode.SingleSession;
            spawnModeSource = BridgeSpawnModeSource.Resume;
        }
        else if (args.SpawnMode is not null)
        {
            // 优先级2: flag — 用户通过命令行参数显式指定
            spawnModeSource = BridgeSpawnModeSource.Flag;
        }
        else if (_deps.GetSavedSpawnMode is not null && multiSessionEnabled)
        {
            // 优先级3: saved — 对齐 TS 端: gate 关闭时不加载已保存偏好
            // 原因: GrowthBook 回滚时，已保存的偏好不应悄悄重新启用多会话行为
            var savedMode = _deps.GetSavedSpawnMode();
            if (savedMode is not null)
            {
                effectiveSpawnMode = savedMode;
                spawnModeSource = BridgeSpawnModeSource.Saved;
            }
        }

        // 首次运行对话框 — 对齐 TS 端: multiSessionEnabled && !savedSpawnMode && worktreeAvailable && ...
        if (multiSessionEnabled &&
            spawnModeSource == BridgeSpawnModeSource.GateDefault &&
            args.SpawnMode is null &&
            resumeSessionId is null &&
            _deps.SpawnModeDialog is not null &&
            _deps.IsWorktreeAvailable?.Invoke() == true)
        {
            var chosenMode = await _deps.SpawnModeDialog(ct).ConfigureAwait(false);
            effectiveSpawnMode = chosenMode;
            // 对话框选择后保存偏好 — 来源仍为 gate_default（对话框是默认路径的一部分）
            _deps.SaveSpawnModePreference?.Invoke(chosenMode);
            _logger?.LogInformation("BridgeMain: spawn mode chosen via dialog: {Mode}", chosenMode.ToValue());
        }

        // 8. 构建 BridgeConfig — 对齐 TS 端 config 构建
        var isResuming = resumeSessionId is not null;
        _isResuming = isResuming; // 保存到实例字段 — 可恢复关闭时使用
        var config = BuildConfig(args, baseUrl, reuseEnvironmentId, effectiveSpawnMode, isResuming, spawnModeSource);

        // 8. 环境注册 — 对齐 TS 端: api.registerBridgeEnvironment(config)
        try
        {
            await RegisterEnvironmentAsync(config, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return HandleRegistrationError(ex);
        }

        _logger?.LogInformation("BridgeMain: environment registered, ID={EnvId}", EnvironmentId);

        // 对齐 TS 端: logEvent("tengu_bridge_started", {...})
        TelemetryCount("tengu_bridge_started", new Dictionary<string, string>
        {
            ["max_sessions"] = (args.Capacity ?? (effectiveSpawnMode == BridgeSpawnMode.SingleSession ? 1 : 5)).ToString(),
            ["has_debug_file"] = (args.DebugFile is not null).ToString(),
            ["sandbox"] = args.Sandbox.ToString(),
            ["debuglog"] = args.DebugLog.ToString(),
            ["heartbeat_interval_ms"] = (_deps.PollConfig?.HeartbeatIntervalMs ?? 30000).ToString(),
            ["spawn_mode"] = effectiveSpawnMode?.ToValue() ?? "single-session",
            ["spawn_mode_source"] = spawnModeSource.ToValue(),
            ["worktree_available"] = (_deps.IsWorktreeAvailable?.Invoke() ?? false).ToString(),
        });

        // 9.5 创建初始会话 — 对齐 TS 端: createBridgeSession
        // preCreateSession 且非 KAIROS 恢复模式时，预创建一个会话
        string? initialSessionId = resumeSessionId;
        if (initialSessionId is null)
        {
            var createdSessionId = await TryCreateInitialSessionAsync(
                args.Name, _deps.PermissionMode, config, ct).ConfigureAwait(false);
            if (createdSessionId is not null)
            {
                initialSessionId = createdSessionId;
            }
        }

        // 9. 单会话模式下写入崩溃恢复指针
        if (config.SpawnMode == BridgeSpawnMode.SingleSession && resumeSessionId is null)
        {
            // 先不写指针，等会话创建后再写
        }

        // 10. 注册键盘监听 — 对齐 TS 端: process.stdin.setRawMode(true) + on('data', onStdinData)
        if (_deps.RegisterKeyboardListener is not null)
        {
            _deps.RegisterKeyboardListener(OnKeyboardInputAsync);
        }

        // 10.5 创建 Token 刷新调度器 — 对齐 TS 端 createTokenRefreshScheduler + v1/v2 分支
        // v2 会话: reconnectSession 触发服务端重新派发（CC-1263）
        // v1 会话: 直接 updateAccessToken 传递 OAuth token 给子进程
        if (_deps.GetAccessToken is not null)
        {
            _tokenRefresh = new BridgeTokenRefreshScheduler(
                new TokenRefreshOptions
                {
                    GetAccessToken = _deps.GetAccessToken,
                    OnRefresh = (sessionId, oauthToken) =>
                    {
                        if (_tracker.Sessions.IsV2(sessionId))
                        {
                            // 对齐 TS 端: v2 会话通过 reconnectSession 刷新 — 服务端重新派发带新 JWT 的工作项
                            // 对齐 TS 端: 双 ID 尝试 — 先 compatId(session_*), 失败再 infraId(cse_*)
                            _logger?.LogDebug("BridgeMain: refreshing v2 session {SessionId} via reconnectSession", sessionId);
                            ReconnectV2SessionFireAndForget(sessionId);
                        }
                        else
                        {
                            // 对齐 TS 端: v1 会话直接更新 OAuth token
                            UpdateV1SessionTokenFireAndForget(sessionId, oauthToken);
                        }
                    },
                    Label = "bridge",
                    Logger = _logger,
                });
        }
        else
        {
            _tokenRefresh = _deps.TokenRefreshScheduler; // 回退到外部注入的 scheduler
        }

        // 10.8 Logger 初始化调用 — 对齐 TS 端 printBanner/setRepoInfo/setAttached
        _deps.BridgeLogger?.PrintBanner(config, GetEnvironmentId());
        _deps.BridgeLogger?.UpdateSessionCount(0, config.MaxSessions, config.SpawnMode);
        if (initialSessionId is not null)
        {
            var compatId = GetCompatId(initialSessionId);
            _deps.BridgeLogger?.SetAttached(compatId);
        }
        if (config.GitRepoUrl is not null || config.Branch is not null)
        {
            var repoName = ExtractRepoName(config.GitRepoUrl, _deps.WorkingDirectory);
            _deps.BridgeLogger?.SetRepoInfo(repoName, config.Branch ?? "");
        }

        // 11. 启动状态显示更新定时器 — 对齐 TS 端 startStatusUpdates
        // 每秒推送会话计数、活动、工具轨迹到 logger
        using var statusTimer = new Timer(_ => UpdateStatusDisplay(config), null,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        // 12. 启动主循环
        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loopTask = RunBridgeLoopAsync(config, initialSessionId, _loopCts.Token);
        try
        {
            await _loopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger?.LogInformation("BridgeMain: loop cancelled");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BridgeMain: loop failed");
            return new BridgeMainResult { Error = $"Loop failed: {ex.Message}" };
        }

        return new BridgeMainResult { Completed = true };
    }

    /// <summary>
    /// Headless 模式启动 — 对齐 TS 端 runBridgeHeadless()
    /// 守护进程入口：无 TUI、无交互、无 readline
    /// 配置性错误抛出 BridgeHeadlessPermanentError（supervisor 停放 worker）
    /// 瞬态错误抛出普通 Exception（supervisor 重试）
    /// </summary>
    public async Task RunHeadlessAsync(BridgeHeadlessOpts opts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(opts);

        // ===== 永久性验证 1: 工作区信任检查 — 对齐 TS 端 checkHasTrustDialogAccepted =====
        if (opts.CheckWorkspaceTrusted is not null && !opts.CheckWorkspaceTrusted())
        {
            throw new BridgeHeadlessPermanentError(
                $"Workspace not trusted: {opts.Dir}. Run '{BrandConstants.CliCommandName}' in that directory first to accept the trust dialog.");
        }

        // ===== 瞬态验证: Token 检查 — 对齐 TS 端 getAccessToken =====
        // Headless 模式使用 opts.GetAccessToken()，而非 _deps.GetAccessToken()
        var accessToken = opts.GetAccessToken();
        if (string.IsNullOrEmpty(accessToken))
        {
            throw new InvalidOperationException("No access token available. AuthManager may provide one in the next cycle.");
        }

        // ===== 永久性验证 2: HTTPS 检查 — 对齐 TS 端 HTTP URL 检查 =====
        var baseUrl = opts.GetBaseUrl();
        var httpsError = ValidateHttpsUrl(baseUrl);
        if (httpsError is not null)
        {
            throw new BridgeHeadlessPermanentError(
                "Remote Control base URL uses HTTP. Only HTTPS or localhost HTTP is allowed.");
        }

        // ===== 永久性验证 3: Worktree 可用性检查 — 对齐 TS 端 worktree 检查 =====
        if (opts.SpawnMode == BridgeSpawnMode.Worktree)
        {
            var hasGitRepo = opts.CheckGitRepoExists?.Invoke(opts.Dir) ?? false;
            var hasWorktreeHooks = opts.CheckWorktreeCreateHooks?.Invoke() ?? false;
            if (!hasGitRepo && !hasWorktreeHooks)
            {
                throw new BridgeHeadlessPermanentError(
                    $"Worktree mode requires a git repository or WorktreeCreate hooks. Directory {opts.Dir} has neither.");
            }
        }

        // ===== 构建 BridgeConfig — 对齐 TS 端 headless config 构建 =====
        // 对齐 TS 端: sessionIngressUrl — ant 开发环境下可能与 baseUrl 不同
        var headlessSessionIngressUrl = baseUrl;
        var userType = Environment.GetEnvironmentVariable("USER_TYPE");
        var ingressOverride = Environment.GetEnvironmentVariable(JccEnvVar.BridgeSessionIngressUrl.ToValue());
        if (string.Equals(userType, "ant", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(ingressOverride))
        {
            headlessSessionIngressUrl = ingressOverride;
        }

        var config = new BridgeConfig
        {
            Dir = opts.Dir,
            MachineName = Environment.MachineName,
            Branch = _deps.GitBranch ?? "main",
            GitRepoUrl = _deps.GitRepoUrl,
            MaxSessions = opts.Capacity,
            SpawnMode = opts.SpawnMode,
            DebugLog = false, // Headless 硬编码 false — 对齐 TS 端
            Sandbox = opts.Sandbox,
            BridgeId = Guid.NewGuid().ToString(),
            WorkerType = "bridge",
            ApiBaseUrl = baseUrl,
            SessionIngressUrl = headlessSessionIngressUrl,
            DebugFile = null, // Headless 不支持 debugFile
            SessionTimeoutMs = opts.SessionTimeoutMs,
        };

        // ===== 环境注册 — 对齐 TS 端 api.registerBridgeEnvironment(config) =====
        try
        {
            await RegisterEnvironmentAsync(config, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            throw; // null response — 透传
        }
        catch (Exception ex)
        {
            // 瞬态错误 — supervisor 会重试
            throw new InvalidOperationException($"Registration failed: {ex.Message}", ex);
        }

        _logger?.LogInformation("BridgeMain(headless): environment registered, ID={EnvId}", EnvironmentId);

        // ===== 可选: 预创建初始会话 — 对齐 TS 端 createSessionOnStart =====
        string? initialSessionId = null;
        if (opts.CreateSessionOnStart)
        {
            initialSessionId = await TryCreateInitialSessionAsync(
                opts.Name, opts.PermissionMode, config, ct).ConfigureAwait(false);
        }

        // Headless logger 初始化 — 对齐 TS 端: logger.printBanner(config, environmentId)
        _deps.BridgeLogger?.PrintBanner(config, GetEnvironmentId());

        // ===== 进入 runBridgeLoop — 共享同一个轮询循环 =====
        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loopTask = RunBridgeLoopAsync(config, initialSessionId, _loopCts.Token);
        try
        {
            await _loopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger?.LogInformation("BridgeMain(headless): loop cancelled");
        }
        catch (BridgeHeadlessPermanentError)
        {
            throw; // 透传永久性错误
        }
        catch (BridgeFatalError ex)
        {
            // 401: 尝试通过 OnAuth401 刷新
            if (ex.StatusCode == 401 && opts.OnAuth401 is not null)
            {
                var refreshed = await opts.OnAuth401(accessToken).ConfigureAwait(false);
                if (!refreshed)
                {
                    throw new InvalidOperationException($"Auth refresh failed: {ex.Message}", ex);
                }
                // 刷新成功 — supervisor 会重新启动 headless
                return;
            }

            throw new InvalidOperationException($"Bridge fatal error: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Loop failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 请求优雅关闭 — 对齐 TS 端 SIGINT/SIGTERM 处理
    /// </summary>
    public async Task ShutdownAsync()
        => await _shutdownHandler.ShutdownAsync().ConfigureAwait(false);

    /// <summary>
    /// 核心轮询循环 — 委托给 BridgeLoopRunner
    /// </summary>
    private async Task RunBridgeLoopAsync(
        BridgeConfig config, string? initialSessionId, CancellationToken ct)
        => await _loopRunner.RunBridgeLoopAsync(config, initialSessionId, ct).ConfigureAwait(false);

    /// <summary>
    /// 处理工作项 — 对齐 TS 端 bridgeMain.ts 的工作处理流程
    /// 流程: 解码 WorkSecret → healthcheck 处理 → ACK(sessionToken) → CCR v2 判断 → 生成子进程
    /// </summary>
    internal async Task HandleWorkAsync(BridgeConfig config, BridgeWorkItem work, CancellationToken ct)
        => await _workHandler.HandleWorkAsync(config, work, ct).ConfigureAwait(false);

    /// <summary>
    /// 监控会话完成 — 对齐 TS 端: onSessionDone 回调
    /// </summary>
    internal async Task MonitorSessionCompletionAsync(
        BridgeConfig config, BridgeWorkItem work, BridgeSubprocessHandle handle, CancellationToken ct)
        => await _sessionMonitor.MonitorSessionCompletionAsync(config, work, handle, ct).ConfigureAwait(false);

    /// <summary>
    /// 监控会话超时 — 对齐 TS 端: onSessionTimeout
    /// </summary>
    internal async Task MonitorSessionTimeoutAsync(
        BridgeConfig config, BridgeWorkItem work, BridgeSubprocessHandle handle,
        int timeoutMs, CancellationToken ct)
        => await _sessionMonitor.MonitorSessionTimeoutAsync(config, work, handle, timeoutMs, ct).ConfigureAwait(false);

    /// <summary>
    /// 清理所有会话 — 对齐 TS 端: 优雅关闭流程
    /// </summary>
    internal async Task CleanupAllSessionsAsync(BridgeConfig config, CancellationToken ct)
        => await _sessionMonitor.CleanupAllSessionsAsync(config, ct).ConfigureAwait(false);

    private BridgeMainResult HandleRegistrationError(Exception ex)
        => _environmentRegistrar.HandleRegistrationError(ex);
}
