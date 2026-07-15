
namespace Core.Bridge;

/// <summary>
/// Bridge 独立进程编排器 — 对齐 TS 端 bridgeMain.ts
/// 核心职责: 参数解析 → OAuth认证 → 环境注册 → 工作轮询 → 子进程管理 → 优雅关闭
/// </summary>
public sealed partial class BridgeMain : IAsyncDisposable
{
    private readonly BridgeMainDeps _deps;
    private readonly ILogger? _logger;
    private readonly IFileSystem _fs;
    private readonly IClockService _clock;

    // 活跃会话跟踪 — 对齐 TS 端 runBridgeLoop 的 7 个 Map + 3 个 Set
    private readonly BridgeSessionTracker _tracker = new();
    private readonly List<Task> _pendingCleanups = new(); // 待清理任务 — 对齐 TS 端 pendingCleanups
    private readonly SemaphoreSlim _cleanupLock = new(1, 1); // 替代 lock — JCC4001 分析器要求

    // 退避状态 — 对齐 TS 端 BackoffConfig + 双轨退避
    private readonly BridgeBackoffStrategy _backoff;

    // 生命周期
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private int _isDisposed;
    private int _isShuttingDown;
    private bool _isResuming; // 对齐 TS 端: resume 模式标记 — 可恢复关闭时跳过 archive+deregister
    private bool _fatalExit; // 对齐 TS 端: fatalExit — 致命错误后跳过 resume 提示
    private string? _resumePointerDir; // 对齐 TS 端: resumePointerDir — 指针来源目录，恢复失败时清除正确的指针

    // 崩溃恢复指针刷新
    private Timer? _pointerRefreshTimer;

    // Token 刷新调度器 — 对齐 TS 端 createTokenRefreshScheduler + v1/v2 分支
    private BridgeTokenRefreshScheduler? _tokenRefresh;

    // 遥测 — 对齐 TS 端 logEvent/logEventAsync (tengu_bridge_*)
    private readonly ITelemetryService? _telemetry;
    private DateTime _loopStartTime; // 主循环启动时间 — 用于计算 loop_duration_ms
    private readonly MiddlewarePipeline<HandleWorkContext>? _handleWorkPipeline;
    private readonly MiddlewarePipeline<ShutdownContext>? _shutdownPipeline;
    private readonly MiddlewarePipeline<BridgeRunContext>? _runPipeline;

    /// <summary>当前活跃会话数</summary>
    public int ActiveSessionCount => _tracker.ActiveSessionCount;

    /// <summary>是否正在运行</summary>
    public bool IsRunning => _loopTask is { IsCompleted: false };

    /// <summary>环境 ID</summary>
    public string? EnvironmentId { get; private set; }

    /// <summary>环境密钥</summary>
    public string? EnvironmentSecret { get; private set; }

    public BridgeMain(
        BridgeMainDeps deps,
        MiddlewarePipeline<HandleWorkContext>? handleWorkPipeline = null,
        MiddlewarePipeline<ShutdownContext>? shutdownPipeline = null,
        MiddlewarePipeline<BridgeRunContext>? runPipeline = null,
        ILogger? logger = null,
        IClockService? clock = null)
    {
        _deps = deps ?? throw new ArgumentNullException(nameof(deps));
        _logger = logger;
        _fs = deps.FileSystem;
        _telemetry = deps.TelemetryService;
        _clock = clock ?? SystemClockService.Instance;
        _backoff = new BridgeBackoffStrategy(_clock, _logger);
        _handleWorkPipeline = handleWorkPipeline;
        _shutdownPipeline = shutdownPipeline;
        _runPipeline = runPipeline;
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
    /// 注册 Bridge 环境 — RunAsync/RunHeadlessAsync 共享
    /// 成功时设置 EnvironmentId/EnvironmentSecret 并返回响应
    /// </summary>
    private async Task<BridgeEnvironmentRegistrationResponse> RegisterEnvironmentAsync(
        BridgeConfig config, CancellationToken ct)
    {
        var registration = new BridgeEnvironmentRegistration
        {
            BridgeId = config.BridgeId,
            MachineName = config.MachineName,
            Dir = config.Dir,
            Branch = config.Branch,
            GitRepoUrl = config.GitRepoUrl,
            MaxSessions = config.MaxSessions,
            SpawnMode = config.SpawnMode.ToValue(),
            WorkerType = config.WorkerType,
            ReuseEnvironmentId = config.ReuseEnvironmentId,
        };

        var response = await _deps.ApiClient.RegisterBridgeEnvironmentAsync(
            registration, ct).ConfigureAwait(false);

        if (response is null)
            throw new InvalidOperationException("Registration returned null response.");

        EnvironmentId = response.EnvironmentId;
        EnvironmentSecret = response.BridgeId;

        return response;
    }

    /// <summary>
    /// 尝试创建初始会话 — RunAsync/RunHeadlessAsync 共享
    /// </summary>
    /// <param name="name">会话标题</param>
    /// <param name="permissionMode">权限模式</param>
    /// <param name="config">Bridge 配置</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>创建的会话 ID；null 表示未创建或创建失败</returns>
    private async Task<string?> TryCreateInitialSessionAsync(
        string? name, string? permissionMode, BridgeConfig config, CancellationToken ct)
    {
        if (_deps.CreateBridgeSession is null) return null;

        try
        {
            var createRequest = new BridgeCreateSessionRequest
            {
                EnvironmentId = EnvironmentId!,
                Title = name,
                GitRepoUrl = config.GitRepoUrl,
                Branch = config.Branch,
                PermissionMode = permissionMode,
            };
            var createdSessionId = await _deps.CreateBridgeSession(
                createRequest, ct).ConfigureAwait(false);
            if (createdSessionId is not null)
            {
                _logger?.LogInformation("BridgeMain: created initial session {SessionId}", createdSessionId);
            }
            return createdSessionId;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "BridgeMain: session creation failed (non-fatal)");
            return null;
        }
    }

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
        _resumePointerDir = ctx.ResumePointerDir;

        var config = BuildConfig(ctx.Args, ctx.BaseUrl!, ctx.ReuseEnvironmentId, ctx.EffectiveSpawnMode, ctx.IsResuming, ctx.SpawnModeSource);

        try
        {
            await RegisterEnvironmentAsync(config, ct).ConfigureAwait(false);
        }
        catch (BridgeFatalError ex)
        {
            TelemetryCount("tengu_bridge_registration_failed", new Dictionary<string, string>
            {
                ["status"] = ex.StatusCode?.ToString() ?? "0",
            });
            if (BridgeApiClient.IsExpiredErrorType(ex.ErrorType))
            {
                _logger?.LogWarning("BridgeMain: registration expired: {Message}", ex.Message);
            }
            else if (BridgeApiClient.IsSuppressible403(ex))
            {
                _logger?.LogDebug("BridgeMain: suppressed 403 during registration: {Message}", ex.Message);
            }
            else
            {
                _logger?.LogError(ex, "BridgeMain: environment registration failed (fatal)");
            }
            return new BridgeMainResult { Error = $"Registration failed: {ex.Message}" };
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Registration returned null"))
        {
            return new BridgeMainResult { Error = ex.Message };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BridgeMain: environment registration failed");
            return new BridgeMainResult { Error = $"Registration failed: {ex.Message}" };
        }

        _logger?.LogInformation("BridgeMain: environment registered, ID={EnvId}", EnvironmentId);

        TelemetryCount("tengu_bridge_started", new Dictionary<string, string>
        {
            ["max_sessions"] = (ctx.Args.Capacity ?? (ctx.EffectiveSpawnMode == BridgeSpawnMode.SingleSession ? 1 : 5)).ToString(),
            ["has_debug_file"] = (ctx.Args.DebugFile is not null).ToString(),
            ["sandbox"] = ctx.Args.Sandbox.ToString(),
            ["verbose"] = ctx.Args.Verbose.ToString(),
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
                        if (_tracker.V2Sessions.Contains(sessionId))
                        {
                            _logger?.LogDebug("BridgeMain: refreshing v2 session {SessionId} via reconnectSession", sessionId);
                            if (EnvironmentId is not null && _deps.ReconnectSession is not null)
                            {
                                _ = Task.Run(async () =>
                                {
                                    var compatId = GetCompatId(sessionId);
                                    var infraId = SessionIdCompat.ToInfraSessionId(sessionId);
                                    var candidates = infraId == sessionId
                                        ? [sessionId]
                                        : new[] { sessionId, infraId };
                                    foreach (var candidateId in candidates)
                                    {
                                        try
                                        {
                                            await _deps.ReconnectSession(EnvironmentId, candidateId, CancellationToken.None).ConfigureAwait(false);
                                            _logger?.LogDebug("BridgeMain: reconnectSession succeeded for {CandidateId}", candidateId);
                                            return;
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger?.LogDebug(ex, "BridgeMain: reconnectSession({CandidateId}) failed, trying next", candidateId);
                                        }
                                    }
                                }, CancellationToken.None);
                            }
                        }
                        else
                        {
                            if (_tracker.ActiveSessions.TryGetValue(sessionId, out var handle))
                            {
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await handle.UpdateAccessTokenAsync(oauthToken, CancellationToken.None).ConfigureAwait(false);
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger?.LogDebug(ex, "BridgeMain: updateAccessToken failed for {SessionId} (non-fatal)", sessionId);
                                    }
                                }, CancellationToken.None);
                            }
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

        _deps.BridgeLogger?.PrintBanner(config, EnvironmentId!);
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
            var validModes = new[] { "default", "plan", "auto-accept", "bubble" };
            if (!validModes.Contains(_deps.PermissionMode, StringComparer.OrdinalIgnoreCase))
            {
                return new BridgeMainResult { Error = $"Invalid permission mode '{_deps.PermissionMode}'. Valid modes: {string.Join(", ", validModes)}" };
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
                _resumePointerDir = pointerDir; // 记录指针来源目录 — 恢复失败时清除正确的指针
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
        catch (BridgeFatalError ex)
        {
            // 对齐 TS 端: logEvent("tengu_bridge_registration_failed", {status})
            TelemetryCount("tengu_bridge_registration_failed", new Dictionary<string, string>
            {
                ["status"] = ex.StatusCode?.ToString() ?? "0",
            });
            // 对齐 TS 端: 分层判断 — 过期/可抑制403 vs 真正致命
            if (BridgeApiClient.IsExpiredErrorType(ex.ErrorType))
            {
                _logger?.LogWarning("BridgeMain: registration expired: {Message}", ex.Message);
            }
            else if (BridgeApiClient.IsSuppressible403(ex))
            {
                _logger?.LogDebug("BridgeMain: suppressed 403 during registration: {Message}", ex.Message);
            }
            else
            {
                _logger?.LogError(ex, "BridgeMain: environment registration failed (fatal)");
            }
            return new BridgeMainResult { Error = $"Registration failed: {ex.Message}" };
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Registration returned null"))
        {
            return new BridgeMainResult { Error = ex.Message };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BridgeMain: environment registration failed");
            return new BridgeMainResult { Error = $"Registration failed: {ex.Message}" };
        }

        _logger?.LogInformation("BridgeMain: environment registered, ID={EnvId}", EnvironmentId);

        // 对齐 TS 端: logEvent("tengu_bridge_started", {...})
        TelemetryCount("tengu_bridge_started", new Dictionary<string, string>
        {
            ["max_sessions"] = (args.Capacity ?? (effectiveSpawnMode == BridgeSpawnMode.SingleSession ? 1 : 5)).ToString(),
            ["has_debug_file"] = (args.DebugFile is not null).ToString(),
            ["sandbox"] = args.Sandbox.ToString(),
            ["verbose"] = args.Verbose.ToString(),
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
                        if (_tracker.V2Sessions.Contains(sessionId))
                        {
                            // 对齐 TS 端: v2 会话通过 reconnectSession 刷新 — 服务端重新派发带新 JWT 的工作项
                            // 对齐 TS 端: 双 ID 尝试 — 先 compatId(session_*), 失败再 infraId(cse_*)
                            _logger?.LogDebug("BridgeMain: refreshing v2 session {SessionId} via reconnectSession", sessionId);
                            if (EnvironmentId is not null && _deps.ReconnectSession is not null)
                            {
                                _ = Task.Run(async () =>
                                {
                                    var compatId = GetCompatId(sessionId);
                                    var infraId = SessionIdCompat.ToInfraSessionId(sessionId);
                                    var candidates = infraId == sessionId
                                        ? [sessionId]
                                        : new[] { sessionId, infraId };
                                    foreach (var candidateId in candidates)
                                    {
                                        try
                                        {
                                            await _deps.ReconnectSession(EnvironmentId, candidateId, CancellationToken.None).ConfigureAwait(false);
                                            _logger?.LogDebug("BridgeMain: reconnectSession succeeded for {CandidateId}", candidateId);
                                            return; // 成功即返回
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger?.LogDebug(ex, "BridgeMain: reconnectSession({CandidateId}) failed, trying next", candidateId);
                                        }
                                    }
                                }, CancellationToken.None);
                            }
                        }
                        else
                        {
                            // 对齐 TS 端: v1 会话直接更新 OAuth token
                            if (_tracker.ActiveSessions.TryGetValue(sessionId, out var handle))
                            {
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await handle.UpdateAccessTokenAsync(oauthToken, CancellationToken.None).ConfigureAwait(false);
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger?.LogDebug(ex, "BridgeMain: updateAccessToken failed for {SessionId} (non-fatal)", sessionId);
                                    }
                                }, CancellationToken.None);
                            }
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
        _deps.BridgeLogger?.PrintBanner(config, EnvironmentId!);
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
                $"Workspace not trusted: {opts.Dir}. Run 'claude' in that directory first to accept the trust dialog.");
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
        var ingressOverride = Environment.GetEnvironmentVariable("CLAUDE_BRIDGE_SESSION_INGRESS_URL");
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
            Verbose = false, // Headless 硬编码 false — 对齐 TS 端
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
        _deps.BridgeLogger?.PrintBanner(config, EnvironmentId!);

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
    {
        if (Interlocked.Exchange(ref _isShuttingDown, 1) == 1)
        {
            return; // 防重入
        }

        if (_shutdownPipeline is not null)
        {
            await ShutdownViaPipelineAsync().ConfigureAwait(false);
            return;
        }

        await ShutdownDirectAsync().ConfigureAwait(false);
    }

    private async Task ShutdownViaPipelineAsync()
    {
        var ctx = new ShutdownContext
        {
            IsResuming = _isResuming,
            FatalExit = _fatalExit,
            EnvironmentId = EnvironmentId,
            SpawnMode = _deps.Config.SpawnMode,
            ResumePointerDir = _resumePointerDir,
            ActiveSessions = _tracker.ActiveSessions,
            SessionCompatIds = _tracker.SessionCompatIds,
            Spawner = _deps.Spawner,
            ApiClient = _deps.ApiClient,
            PointerService = _deps.PointerService,
            WorkingDirectory = _deps.WorkingDirectory,
            ArchiveSession = _deps.ArchiveSession,
            UnregisterKeyboardListener = () => _deps.UnregisterKeyboardListener?.Invoke(),
            LoopCts = _loopCts,
            LoopTask = _loopTask,
            PointerRefreshTimer = _pointerRefreshTimer,
        };

        var pipeline = _shutdownPipeline;
        if (pipeline is not null)
        {
            await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(false);
        }

        _pointerRefreshTimer = null;
    }

    private async Task ShutdownDirectAsync()
    {
        if (Interlocked.Exchange(ref _isShuttingDown, 1) == 1)
        {
            return; // 防重入
        }

        _logger?.LogInformation("BridgeMain: shutting down...");

        // 注销键盘监听 — 对齐 TS 端: process.stdin.setRawMode(false)
        _deps.UnregisterKeyboardListener?.Invoke();

        // 取消主循环
        await (_loopCts?.CancelAsync() ?? Task.CompletedTask).ConfigureAwait(false);

        // 等待主循环退出
        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        // 优雅关闭所有子进程 — 对齐 TS 端 shutdownGraceMs
        var handles = _tracker.ActiveSessions.Values.ToList();
        if (handles.Count > 0)
        {
            await _deps.Spawner.ShutdownAllAsync(handles).ConfigureAwait(false);
        }

        // 归档所有已知会话 — 对齐 TS 端: archiveSession(compatId)
        // resume 模式下跳过归档，保留会话供下次 --continue 恢复
        if (!_isResuming && _deps.ArchiveSession is not null)
        {
            var sessionsToArchive = _tracker.SessionCompatIds.ToList();
            if (sessionsToArchive.Count > 0)
            {
                _logger?.LogInformation("BridgeMain: archiving {Count} session(s)", sessionsToArchive.Count);
                foreach (var kvp in sessionsToArchive)
                {
                    try
                    {
                        await _deps.ArchiveSession(kvp.Value, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(ex, "BridgeMain: archive failed for {SessionId} (non-fatal)", kvp.Value);
                    }
                }
            }
        }
        else if (_isResuming && !_fatalExit)
        {
            _logger?.LogInformation("Resume this session by running `claude remote-control --continue`");
            _logger?.LogDebug("BridgeMain: skipping archive+deregister to allow resume");
        }

        // 注销环境
        // 对齐 TS 端: resumable shutdown — resume 模式下跳过注销，保留环境供下次恢复
        if (!_isResuming && EnvironmentId is not null)
        {
            try
            {
                await _deps.ApiClient.DeregisterEnvironmentAsync(
                    EnvironmentId, CancellationToken.None).ConfigureAwait(false);
                _logger?.LogInformation("BridgeMain: environment deregistered");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "BridgeMain: deregister failed (non-fatal)");
            }
        }

        // 清除崩溃恢复指针
        // 对齐 TS 端: resumable shutdown — resume 模式下保留指针文件供下次 --continue
        // 对齐 TS 端: 使用 _resumePointerDir（可能来自 worktree 兄弟目录）而非 _deps.WorkingDirectory
        if (!_isResuming && _deps.Config.SpawnMode == BridgeSpawnMode.SingleSession)
        {
            try
            {
                var pointerDir = _resumePointerDir ?? _deps.WorkingDirectory;
                await _deps.PointerService.ClearAsync(
                    pointerDir).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "BridgeMain: pointer clear failed (non-fatal)");
            }
        }

        // 停止指针刷新定时器
        _pointerRefreshTimer?.Dispose();
        _pointerRefreshTimer = null;

        _logger?.LogInformation("BridgeMain: shutdown complete");
    }

    /// <summary>
    /// 核心轮询循环 — 对齐 TS 端 runBridgeLoop
    /// 注册环境 → 轮询工作 → 确认工作 → 生成子进程 → 管理生命周期
    /// </summary>
    private async Task RunBridgeLoopAsync(
        BridgeConfig config, string? initialSessionId, CancellationToken ct)
    {
        _logger?.LogInformation("BridgeMain: entering runBridgeLoop, maxSessions={MaxSessions}, spawnMode={SpawnMode}",
            config.MaxSessions, config.SpawnMode);

        _loopStartTime = _clock.GetUtcNow();

        // 如果有初始会话 ID，先恢复 — 对齐 TS 端: reconnectSession
        if (initialSessionId is not null && EnvironmentId is not null)
        {
            try
            {
                await _deps.ApiClient.ReconnectSessionAsync(
                    EnvironmentId, initialSessionId, ct).ConfigureAwait(false);
                _logger?.LogInformation("BridgeMain: reconnected session {SessionId}", initialSessionId);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "BridgeMain: reconnect failed for {SessionId}", initialSessionId);
            }
        }

        // 主轮询循环
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 轮询工作 — 对齐 TS 端: api.pollForWork(envId, envSecret, signal, pollConfig.reclaim_older_than_ms)
                var reclaimMs = _deps.PollConfig?.ReclaimOlderThanMs ?? 5000;
                var work = await _deps.ApiClient.PollForWorkAsync(
                    EnvironmentId!, ct, reclaimMs).ConfigureAwait(false);

                // 重置退避状态（成功通信）
                _backoff.Reset(onReconnected: ms => _logger?.LogInformation("BridgeMain: reconnected after {Ms}ms", ms));

                if (work is null)
                {
                    // 无工作: 根据容量状态选择休眠策略
                    await HandleNoWorkAsync(config, ct).ConfigureAwait(false);
                    continue;
                }

                // 有工作: 处理工作项
                await HandleWorkAsync(config, work, ct).ConfigureAwait(false);
            }
            catch (BridgeFatalError ex)
            {
                // 致命错误: 401/403/404/410 — 对齐 TS 端: 分层判断 + fatalExit 标记
                _fatalExit = true;
                // 对齐 TS 端: logEvent("tengu_bridge_fatal_error", {status, error_type})
                TelemetryCount("tengu_bridge_fatal_error", new Dictionary<string, string>
                {
                    ["status"] = ex.StatusCode?.ToString() ?? "0",
                    ["error_type"] = ex.ErrorType ?? "unknown",
                });
                if (BridgeApiClient.IsExpiredErrorType(ex.ErrorType))
                {
                    // 过期类错误 → 信息性状态消息（非错误样式）
                _logger?.LogWarning("BridgeMain: registration expired: {Message}", ex.Message);
                }
                else if (BridgeApiClient.IsSuppressible403(ex))
                {
                    // 可抑制 403 → 仅调试日志（装饰性权限不足）
                    _logger?.LogDebug("BridgeMain: suppressed 403 error: {Message}", ex.Message);
                }
                else
                {
                    // 其他致命错误 → 错误日志
                    _logger?.LogError(ex, "BridgeMain: fatal error, exiting loop");
                }
                throw;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // 连接错误: 指数退避 — 对齐 TS 端
                // P1-5: 改用异步等待，消除同步阻塞
                var shouldContinue = await _backoff.HandleErrorAsync(
                    ex, onFatalExit: () => _fatalExit = true, ct).ConfigureAwait(false);
                if (!shouldContinue)
                {
                    _logger?.LogError(ex, "BridgeMain: giving up after too many errors");
                    // 对齐 TS 端: logEvent("tengu_bridge_poll_give_up", {error_type, elapsed_ms})
                    var errorType = ex is HttpRequestException or System.Net.Sockets.SocketException ? "connection" : "general";
                    var elapsedMs = _backoff.IsInErrorState
                        ? (long)(_clock.GetUtcNow() - _backoff.FirstErrorTime).TotalMilliseconds : 0;
                    TelemetryCount("tengu_bridge_poll_give_up", new Dictionary<string, string>
                    {
                        ["error_type"] = errorType,
                        ["elapsed_ms"] = elapsedMs.ToString(),
                    });
                    throw;
                }
            }
        }

        // 循环退出后清理
        // 对齐 TS 端: logEvent("tengu_bridge_shutdown", {active_sessions, loop_duration_ms})
        TelemetryCount("tengu_bridge_shutdown", new Dictionary<string, string>
        {
            ["active_sessions"] = _tracker.ActiveSessions.Count.ToString(),
            ["loop_duration_ms"] = _loopStartTime != default
                ? ((long)(_clock.GetUtcNow() - _loopStartTime).TotalMilliseconds).ToString()
                : "0",
        });

        await CleanupAllSessionsAsync(config, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 处理无工作状态 — 对齐 TS 端 at-capacity / partial-capacity 休眠策略
    /// </summary>
    private async Task HandleNoWorkAsync(BridgeConfig config, CancellationToken ct)
    {
        var atCapacity = _tracker.ActiveSessions.Count >= config.MaxSessions;

        if (atCapacity)
        {
            // at-capacity: 心跳保活 + 等待容量释放 — 对齐 TS 端: heartbeatActiveWorkItems + capacityWake
            await RunAtCapacityHeartbeatAsync(config, ct).ConfigureAwait(false);
        }
        else
        {
            // 部分容量或空闲: 按配置间隔休眠 — 对齐 TS 端: sleep(pollInterval)
            var pollInterval = _deps.PollConfig?.PollIntervalMs ?? 5000;
            await Task.Delay(pollInterval, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// at-capacity 心跳保活循环 — 对齐 TS 端: heartbeatActiveWorkItems + sleepUntilCapacityWakes
    /// P3-2: 当 non_exclusive_heartbeat_interval_ms > 0 时进入心跳循环模式
    /// 循环发送心跳，直到容量变化（capacityWake）或需要轮询刷新 token
    /// </summary>
    private async Task RunAtCapacityHeartbeatAsync(BridgeConfig config, CancellationToken ct)
    {
        var heartbeatIntervalMs = _deps.PollConfig?.HeartbeatIntervalMs ?? 30000;
        var nonExclusiveIntervalMs = _deps.PollConfig?.NonExclusiveHeartbeatIntervalMs ?? 0;

        // P3-2: 心跳循环模式 — 对齐 TS 端 at-capacity 心跳循环
        // 当 non_exclusive_heartbeat_interval_ms > 0 时，在 at-capacity 期间循环发送心跳
        var useHeartbeatLoop = nonExclusiveIntervalMs > 0;
        var pollDeadlineMs = _deps.PollConfig?.AtCapacityPollIntervalMs ?? 0;

        while (!ct.IsCancellationRequested)
        {
            // 对所有活跃工作项发送心跳 — 对齐 TS 端: heartbeatWork(environmentId, workId, ingressToken)
            foreach (var kvp in _tracker.ActiveSessions)
            {
                var sessionId = kvp.Key;
                if (_tracker.SessionWorkIds.TryGetValue(sessionId, out var workId) &&
                    _tracker.SessionIngressTokens.TryGetValue(sessionId, out var ingressToken))
                {
                    try
                    {
                        await _deps.ApiClient.HeartbeatWorkAsync(
                            EnvironmentId!, workId, ingressToken, ct).ConfigureAwait(false);
                    }
                    catch (BridgeFatalError ex) when (ex.StatusCode == 401 || ex.StatusCode == 403)
                    {
                        // P3-7: 对齐 TS 端 — heartbeat 401/403 → reconnectSession
                        _logger?.LogDebug("BridgeMain: heartbeat auth_failed ({Status}) for {SessionId}, attempting reconnect",
                            ex.StatusCode, sessionId);
                        try
                        {
                            await _deps.ApiClient.ReconnectSessionAsync(
                                EnvironmentId!, sessionId, ct).ConfigureAwait(false);
                        }
                        catch (Exception reconnectEx)
                        {
                            _logger?.LogDebug(reconnectEx, "BridgeMain: reconnect failed for {SessionId} (non-fatal)", sessionId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(ex, "BridgeMain: heartbeat failed for {SessionId}", sessionId);
                    }
                }
            }

            // 非循环模式: 只做一次心跳+等待，然后返回让主循环继续轮询
            if (!useHeartbeatLoop)
            {
                // 等待容量释放或超时 — 对齐 TS 端: capacityWake.signal() + sleep
                if (_deps.CapacityWake is not null)
                {
                    await _deps.CapacityWake.SleepUntilCapacityWakesAsync(
                        TimeSpan.FromMilliseconds(heartbeatIntervalMs), ct).ConfigureAwait(false);
                }
                else
                {
                    await Task.Delay(heartbeatIntervalMs, ct).ConfigureAwait(false);
                }
                return;
            }

            // P3-2: 循环模式 — 等待 nonExclusiveIntervalMs 或容量变化
            var waitMs = nonExclusiveIntervalMs;
            if (_deps.CapacityWake is not null)
            {
                await _deps.CapacityWake.SleepUntilCapacityWakesAsync(
                    TimeSpan.FromMilliseconds(waitMs), ct).ConfigureAwait(false);
                // 容量变化 → 退出心跳循环，返回主循环轮询
                break;
            }
            else
            {
                await Task.Delay(waitMs, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// 处理工作项 — 对齐 TS 端 bridgeMain.ts 的工作处理流程
    /// 流程: 解码 WorkSecret → healthcheck 处理 → ACK(sessionToken) → CCR v2 判断 → 生成子进程
    /// </summary>
    private async Task HandleWorkAsync(BridgeConfig config, BridgeWorkItem work, CancellationToken ct)
    {
        if (_handleWorkPipeline is not null)
        {
            await HandleWorkViaPipelineAsync(config, work, ct).ConfigureAwait(false);
            return;
        }

        await HandleWorkDirectAsync(config, work, ct).ConfigureAwait(false);
    }

    private async Task HandleWorkViaPipelineAsync(BridgeConfig config, BridgeWorkItem work, CancellationToken ct)
    {
        var ctx = new HandleWorkContext
        {
            Config = config,
            Work = work,
            CancellationToken = ct,
            EnvironmentId = EnvironmentId,
            ActiveSessions = _tracker.ActiveSessions,
            SessionStartTimes = _tracker.SessionStartTimes,
            SessionWorkIds = _tracker.SessionWorkIds,
            SessionIngressTokens = _tracker.SessionIngressTokens,
            SessionWorktrees = _tracker.SessionWorktrees,
            CompletedWorkIds = _tracker.CompletedWorkIds,
            V2Sessions = _tracker.V2Sessions,
            SessionCompatIds = _tracker.SessionCompatIds,
            StopWorkAsync = (workId, token) => StopWorkWithRetryAsync(workId, token),
            TrackCleanup = task => TrackCleanup(task),
            CapacityWake = () => _deps.CapacityWake?.WakeUp(),
            TelemetryCount = (name, props) => TelemetryCount(name, props),
            Spawner = _deps.Spawner,
            PollConfig = _deps.PollConfig,
            SpawnDir = DetermineSpawnDir(config, work),
            GetAccessToken = _deps.GetAccessToken,
            PermissionMode = _deps.PermissionMode,
            OnPermissionRequest = _deps.OnPermissionRequest is not null
                ? (req, token) => _deps.OnPermissionRequest(work.SessionId, req, token)
                : null,
            OnActivity = _deps.OnActivity is not null
                ? activity => _deps.OnActivity(work.SessionId, activity)
                : null,
            OnFirstUserMessage = text => OnFirstUserMessage(work.SessionId, text, config),
        };

        var pipeline = _handleWorkPipeline;
        if (pipeline is not null)
        {
            await pipeline.ExecuteAsync(ctx, ct).ConfigureAwait(false);
        }

        if (!ctx.ShortCircuited && ctx.Handle is not null)
        {
            var compatId = SessionIdCompat.ToCompatSessionId(work.SessionId);
            _deps.BridgeLogger?.AddSession(compatId, BuildRemoteSessionUrl(compatId, config));
            _deps.BridgeLogger?.SetAttached(compatId);

            if (config.SpawnMode == BridgeSpawnMode.SingleSession)
            {
                await WritePointerAsync(config, work.SessionId).ConfigureAwait(false);
                StartPointerRefreshTimer(config, work.SessionId);
            }

            _ = MonitorSessionCompletionAsync(config, work, ctx.Handle, ct);

            var timeoutMs = config.SessionTimeoutMs > 0 ? config.SessionTimeoutMs : 24 * 60 * 60 * 1000;
            _ = MonitorSessionTimeoutAsync(config, work, ctx.Handle, timeoutMs, ct);

            if (ctx.SessionIngressToken is not null && _tokenRefresh is not null)
            {
                _tokenRefresh.Schedule(work.SessionId, ctx.SessionIngressToken);
            }

            _ = FetchSessionTitleAsync(work.SessionId, config);
        }
    }

    private async Task HandleWorkDirectAsync(BridgeConfig config, BridgeWorkItem work, CancellationToken ct)
    {
        _logger?.LogInformation("BridgeMain: received work, WorkId={WorkId}, SessionId={SessionId}, WorkType={WorkType}",
            work.WorkId, work.SessionId, work.WorkType);

        // 容量检查 — 对齐 TS 端: activeSessions.size >= config.maxSessions
        if (_tracker.ActiveSessions.Count >= config.MaxSessions)
        {
            _logger?.LogWarning("BridgeMain: at capacity, skipping work {WorkId}", work.WorkId);
            return;
        }

        // 去重检查 — 对齐 TS 端: completedWorkIds
        // 服务端可能在处理 stopWork 请求前重新投递过期工作项
        if (_tracker.CompletedWorkIds.Contains(work.WorkId))
        {
            _logger?.LogDebug("BridgeMain: skipping duplicate work {WorkId}", work.WorkId);
            // 容量节流 — 对齐 TS 端: 持续的过期重投递会导致 tight-loop
            // at-capacity 时 sleep 一段时间避免空转
            if (_tracker.ActiveSessions.Count >= config.MaxSessions)
            {
                var pollConfig = _deps.PollConfig;
                var delayMs = pollConfig?.NonExclusiveHeartbeatIntervalMs > 0
                    ? pollConfig!.NonExclusiveHeartbeatIntervalMs
                    : pollConfig?.HeartbeatIntervalMs ?? 30000;
                try
                {
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
            }
            return;
        }

        // ===== P0-1: 解码 WorkSecret — 对齐 TS 端 decodeWorkSecret =====
        // TS 端: secret = decodeWorkSecret(work.secret)
        // 解码后的 session_ingress_token 用于 ACK、spawn、tokenRefresh
        BridgeWorkSecret? secret = null;
        if (!string.IsNullOrEmpty(work.Secret))
        {
            try
            {
                secret = BridgeWorkSecretDecoder.DecodeWorkSecret(work.Secret);
                _logger?.LogDebug("BridgeMain: decoded work secret for WorkId={WorkId}, useCodeSessions={UseCcrV2}",
                    work.WorkId, secret.UseCodeSessions);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "BridgeMain: failed to decode work secret for WorkId={WorkId}", work.WorkId);
                // 对齐 TS 端: logEvent("tengu_bridge_work_secret_failed")
                TelemetryCount("tengu_bridge_work_secret_failed");
                // 对齐 TS 端: 解码失败 → stopWork（用 OAuth token）+ 标记完成 + 跳过
                _tracker.MarkWorkCompleted(work.WorkId);
                TrackCleanup(StopWorkWithRetryAsync(work.WorkId, ct));
                _deps.CapacityWake?.WakeUp();
                return;
            }
        }

        // 提取解码后的关键值 — 对齐 TS 端 secret.session_ingress_token
        var sessionIngressToken = secret?.SessionIngressToken ?? work.SessionIngressToken;
        var secretApiBaseUrl = secret?.ApiBaseUrl ?? work.ApiBaseUrl;

        // ===== P0-4: Healthcheck 工作类型处理 — 对齐 TS 端 case 'healthcheck' =====
        if (string.Equals(work.WorkType, "healthcheck", StringComparison.OrdinalIgnoreCase))
        {
            // 对齐 TS 端: await ackWork() → 仅记录日志
            if (sessionIngressToken is not null)
            {
                await AckWorkAsync(work.WorkId, sessionIngressToken, ct).ConfigureAwait(false);
            }
            _logger?.LogDebug("BridgeMain: healthcheck received");
            return;
        }

        // 已有会话: 更新 token — 对齐 TS 端: existingHandle 路径
        // TS 端使用 secret.session_ingress_token 更新（而非 OAuth token）
        if (_tracker.ActiveSessions.TryGetValue(work.SessionId, out var existingHandle))
        {
            if (sessionIngressToken is not null && sessionIngressToken != existingHandle.AccessToken)
            {
                await existingHandle.UpdateAccessTokenAsync(sessionIngressToken, ct).ConfigureAwait(false);
                _logger?.LogDebug("BridgeMain: updated token for existing session {SessionId}", work.SessionId);
            }
            // 存储 ingress token — 对齐 TS 端: sessionIngressTokens.set(sessionId, secret.session_ingress_token)
            if (sessionIngressToken is not null)
            {
                _tracker.SessionIngressTokens[work.SessionId] = sessionIngressToken;
            }
            return;
        }

        // ===== P0-5: ACK 使用解码后的 session_ingress_token — 对齐 TS 端 acknowledgeWork =====
        // TS 端: api.acknowledgeWork(environmentId, work.id, secret.session_ingress_token)
        // ACK 必须在确认要处理该工作项之后调用（at-capacity 守卫已通过）
        if (sessionIngressToken is not null)
        {
            await AckWorkAsync(work.WorkId, sessionIngressToken, ct).ConfigureAwait(false);
        }
        else
        {
            // 无 sessionToken 时仍尝试 ACK（兼容旧版服务端）
            try
            {
                await _deps.ApiClient.AcknowledgeWorkAsync(
                    EnvironmentId!, work.WorkId, ct: ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "BridgeMain: ACK failed for work {WorkId}", work.WorkId);
                return;
            }
        }

        // ===== P0-2: CCR v2 路径 — 对齐 TS 端 use_code_sessions =====
        // TS 端: if (secret.use_code_sessions === true || isEnvTruthy(CLAUDE_BRIDGE_USE_CCR_V2))
        var useCcrV2 = false;
        int? workerEpoch = null;
        string sdkUrl;

        var forceCcrV2 = Environment.GetEnvironmentVariable("CLAUDE_BRIDGE_USE_CCR_V2") is "1" or "true";
        if ((secret?.UseCodeSessions == true || forceCcrV2) && secretApiBaseUrl is not null)
        {
            // CCR v2: buildCCRv2SdkUrl + registerWorker（最多2次重试）
            sdkUrl = BridgeWorkSecretDecoder.BuildCCRv2SdkUrl(secretApiBaseUrl, work.SessionId);

            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    workerEpoch = (int)await BridgeWorkSecretDecoder.RegisterWorkerAsync(
                        sdkUrl, sessionIngressToken!, _deps.ApiClient.HttpClient, ct).ConfigureAwait(false);
                    useCcrV2 = true;
                    _logger?.LogInformation(
                        "BridgeMain: CCR v2 registered worker, SessionId={SessionId}, epoch={Epoch}, attempt={Attempt}",
                        work.SessionId, workerEpoch, attempt);
                    break;
                }
                catch (Exception ex)
                {
                    if (attempt < 2)
                    {
                        _logger?.LogDebug(ex,
                            "BridgeMain: CCR v2 registerWorker attempt {Attempt} failed, retrying", attempt);
                        await Task.Delay(2000, ct).ConfigureAwait(false);
                        continue;
                    }

                    _logger?.LogError(ex,
                        "BridgeMain: CCR v2 worker registration failed for session {SessionId}", work.SessionId);
                    _tracker.MarkWorkCompleted(work.WorkId);
                    TrackCleanup(StopWorkWithRetryAsync(work.WorkId, ct));
                    _deps.CapacityWake?.WakeUp();
                    return;
                }
            }
        }
        else
        {
            // v1 路径: buildSdkUrl — 对齐 TS 端: buildSdkUrl(config.sessionIngressUrl, sessionId)
            var ingressUrl = secretApiBaseUrl ?? config.SessionIngressUrl;
            sdkUrl = BridgeWorkSecretDecoder.BuildSdkUrl(ingressUrl, work.SessionId);
        }

        // 生成子进程 — 对齐 TS 端: safeSpawn(spawner, opts, dir)
        // Worktree 模式: 为非初始会话创建 git worktree — 对齐 TS 端 createWorktreeForSession
        var spawnDir = DetermineSpawnDir(config, work);
        string? createdWorktreePath = null; // 跟踪已创建的 worktree，spawn 失败时需要清理
        if (config.SpawnMode == BridgeSpawnMode.Worktree && _deps.WorktreeService is not null)
        {
            try
            {
                var worktreeResult = await _deps.WorktreeService.CreateAgentWorktreeAsync(
                    work.SessionId,
                    config.Dir,
                    cancellationToken: ct).ConfigureAwait(false);

                if (worktreeResult.Success && worktreeResult.Session?.WorktreePath is not null)
                {
                    spawnDir = worktreeResult.Session.WorktreePath;
                    createdWorktreePath = worktreeResult.Session.WorktreePath;
                    _tracker.SessionWorktrees[work.SessionId] = worktreeResult.Session.WorktreePath;
                    _logger?.LogInformation("BridgeMain: created worktree for session {SessionId} at {Path}",
                        work.SessionId, worktreeResult.Session.WorktreePath);
                }
                else
                {
                    // P3-4: 对齐 TS 端 — worktree 创建失败时 stopWork + completedWorkIds，而非 fallback
                    _logger?.LogError("BridgeMain: worktree creation failed for session {SessionId}, stopping work",
                        work.SessionId);
                    _tracker.MarkWorkCompleted(work.WorkId);
                    await SafeStopWorkAsync(work.WorkId, ct).ConfigureAwait(false);
                    return;
                }
            }
            catch (Exception ex)
            {
                // P3-4: 对齐 TS 端 — worktree 创建异常时 stopWork + completedWorkIds
                _logger?.LogError(ex, "BridgeMain: worktree creation error for session {SessionId}, stopping work",
                    work.SessionId);
                _tracker.MarkWorkCompleted(work.WorkId);
                await SafeStopWorkAsync(work.WorkId, ct).ConfigureAwait(false);
                return;
            }
        }

        // 对齐 TS 端: accessToken 使用 secret.session_ingress_token（而非 OAuth token）
        var accessTokenForSpawn = sessionIngressToken ?? _deps.GetAccessToken();

        var spawnOptions = new BridgeSubprocessOptions
        {
            SessionId = work.SessionId,
            SdkUrl = sdkUrl,
            AccessToken = accessTokenForSpawn,
            Dir = spawnDir,
            Verbose = config.Verbose,
            Sandbox = config.Sandbox,
            DebugFile = config.DebugFile,
            PermissionMode = _deps.PermissionMode,
            UseCcrV2 = useCcrV2,
            WorkerEpoch = workerEpoch,
            // 对齐 TS 端 SessionSpawnOpts.onFirstUserMessage — 首条用户消息回调用于派生标题
            OnFirstUserMessage = text => OnFirstUserMessage(work.SessionId, text, config),
            // 对齐 TS 端 deps.onPermissionRequest — 权限请求回调
            OnPermissionRequest = _deps.OnPermissionRequest is not null
                ? (req, token) => _deps.OnPermissionRequest(work.SessionId, req, token)
                : null,
            // 对齐 TS 端 deps.onActivity — 活动回调
            OnActivity = _deps.OnActivity is not null
                ? activity => _deps.OnActivity(work.SessionId, activity)
                : null,
        };

        BridgeSubprocessHandle handle;
        try
        {
            handle = await _deps.Spawner.SpawnAsync(spawnOptions).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BridgeMain: spawn failed for session {SessionId}", work.SessionId);

            // P3-5: 对齐 TS 端 — spawn 失败时清理已创建的 worktree + completedWorkIds + stopWork
            if (createdWorktreePath is not null && _deps.WorktreeService is not null)
            {
                try
                {
                    await _deps.WorktreeService.RemoveAgentWorktreeAsync(
                        work.SessionId, force: true, cancellationToken: ct).ConfigureAwait(false);
                    _tracker.RemoveWorktree(work.SessionId, out _);
                }
                catch (Exception cleanupEx)
                {
                    _logger?.LogDebug(cleanupEx, "BridgeMain: worktree cleanup after spawn failure for {SessionId} (non-fatal)", work.SessionId);
                }
            }

            _tracker.MarkWorkCompleted(work.WorkId);
            await SafeStopWorkAsync(work.WorkId, ct).ConfigureAwait(false);
            return;
        }

        // 注册跟踪
        var compatId = SessionIdCompat.ToCompatSessionId(work.SessionId);
        _tracker.RegisterSession(work.SessionId, handle, work.WorkId,
            ingressToken: sessionIngressToken, compatId: compatId, isV2: useCcrV2);

        // 注册到 logger 会话列表 — 对齐 TS 端: logger.addSession(compatSessionId, url)
        _deps.BridgeLogger?.AddSession(compatId, BuildRemoteSessionUrl(compatId, config));
        _deps.BridgeLogger?.SetAttached(compatId);

        // 单会话模式: 写入崩溃恢复指针
        if (config.SpawnMode == BridgeSpawnMode.SingleSession)
        {
            await WritePointerAsync(config, work.SessionId).ConfigureAwait(false);
            StartPointerRefreshTimer(config, work.SessionId);
        }

        // 会话完成回调 — 对齐 TS 端: handle.done.then(onSessionDone)
        _ = MonitorSessionCompletionAsync(config, work, handle, ct);

        // 会话超时看门狗 — 对齐 TS 端: setTimeout(onSessionTimeout, timeoutMs)
        var timeoutMs = config.SessionTimeoutMs > 0 ? config.SessionTimeoutMs : 24 * 60 * 60 * 1000;
        _ = MonitorSessionTimeoutAsync(config, work, handle, timeoutMs, ct);

        // Token 刷新调度 — 对齐 TS 端: tokenRefresh?.schedule(sessionId, secret.session_ingress_token)
        if (sessionIngressToken is not null && _tokenRefresh is not null)
        {
            _tokenRefresh.Schedule(work.SessionId, sessionIngressToken);
        }

        _logger?.LogInformation("BridgeMain: session {SessionId} started, active={Active}/{Max}, ccrV2={CcrV2}",
            work.SessionId, _tracker.ActiveSessions.Count, config.MaxSessions, useCcrV2);

        // 对齐 TS 端: logEvent("tengu_bridge_session_started", {...})
        TelemetryCount("tengu_bridge_session_started", new Dictionary<string, string>
        {
            ["active_sessions"] = _tracker.ActiveSessions.Count.ToString(),
            ["spawn_mode"] = config.SpawnMode.ToValue(),
            ["in_worktree"] = (_tracker.SessionWorktrees.ContainsKey(work.SessionId)).ToString(),
        });

        // 对齐 TS 端: fetchSessionTitle — spawn 后立即异步获取服务端标题
        // 服务端标题（--name/web rename）优先于 onFirstUserMessage 派生的标题
        _ = FetchSessionTitleAsync(work.SessionId, config);

        // 容量唤醒: 新会话启动后通知容量变化
        _deps.CapacityWake?.WakeUp();
    }

    /// <summary>
    /// 监控会话完成 — 对齐 TS 端: onSessionDone 回调
    /// </summary>
    private async Task MonitorSessionCompletionAsync(
        BridgeConfig config, BridgeWorkItem work, BridgeSubprocessHandle handle, CancellationToken ct)
    {
        BridgeSubprocessStatus rawStatus;
        try
        {
            rawStatus = await handle.Done.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            rawStatus = BridgeSubprocessStatus.Failed;
            _logger?.LogWarning(ex, "BridgeMain: session {SessionId} failed with exception", work.SessionId);
        }

        // wasTimedOut 检测 — 对齐 TS 端: timedOutSessions.delete(sessionId)
        // 如果会话被超时看门狗杀掉，interrupted 状态修正为 failed
        var wasTimedOut = _tracker.RemoveTimedOut(work.SessionId);
        var status = wasTimedOut && rawStatus == BridgeSubprocessStatus.Interrupted
            ? BridgeSubprocessStatus.Failed
            : rawStatus;

        var compatId = GetCompatId(work.SessionId);
        var durationMs = _tracker.SessionStartTimes.TryGetValue(work.SessionId, out var st)
            ? (long)(_clock.GetUtcNow() - st).TotalMilliseconds : 0L;

        _logger?.LogInformation("BridgeMain: session {SessionId} done, status={Status}, duration={Duration}ms",
            work.SessionId, status, durationMs);

        // 对齐 TS 端: logEvent("tengu_bridge_session_done", {status, duration_ms})
        TelemetryCount("tengu_bridge_session_done", new Dictionary<string, string>
        {
            ["status"] = status.ToString().ToLowerInvariant(),
            ["duration_ms"] = durationMs.ToString(),
        });

        // 清除状态显示 — 对齐 TS 端: logger.clearStatus()
        _deps.BridgeLogger?.ClearStatus();

        // 按状态调用 logger — 对齐 TS 端: switch(status)
        switch (status)
        {
            case BridgeSubprocessStatus.Completed:
                _logger?.LogInformation("BridgeMain: session {SessionId} completed ({DurationMs}ms)", compatId, durationMs);
                break;
            case BridgeSubprocessStatus.Failed:
                // 超时杀掉的会话已由 onSessionTimeout 记录过日志，关机中断也跳过
                if (!wasTimedOut && !_loopCts?.IsCancellationRequested != true)
                {
                    var stderrSummary = handle.StderrLines.Count > 0
                        ? string.Join("\n", handle.StderrLines)
                        : null;
                    var failureMessage = stderrSummary ?? "Process exited with error";
                    _logger?.LogError("BridgeMain: session {SessionId} failed: {Error}", compatId, failureMessage);
                }
                break;
            case BridgeSubprocessStatus.Interrupted:
                _logger?.LogDebug("BridgeMain: session {SessionId} interrupted", compatId);
                break;
        }

        // 清理跟踪
        CleanupSessionTracking(work.SessionId);

        // 清理 worktree — 对齐 TS 端: cleanupWorktree
        if (_tracker.RemoveWorktree(work.SessionId, out var worktreePath) &&
            _deps.WorktreeService is not null)
        {
            try
            {
                await _deps.WorktreeService.RemoveAgentWorktreeAsync(
                    work.SessionId, force: false, cancellationToken: ct).ConfigureAwait(false);
                _logger?.LogInformation("BridgeMain: cleaned up worktree for session {SessionId}", work.SessionId);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "BridgeMain: worktree cleanup failed for {SessionId} (non-fatal)", work.SessionId);
            }
        }

        // 停止工作项 — 对齐 TS 端: interrupted 状态跳过 stopWork（服务端已知道或 shutdown 会单独调用）
        // 非 interrupted 状态才 stopWork + completedWorkIds
        if (status != BridgeSubprocessStatus.Interrupted)
        {
            await StopWorkWithRetryAsync(work.WorkId, ct).ConfigureAwait(false);
            _tracker.MarkWorkCompleted(work.WorkId);
        }

        // 归档会话 — 对齐 TS 端: archiveSession(compatId)
        // 对齐 TS 端: resumable shutdown — resume 模式下跳过归档，保留指针文件
        // 对齐 TS 端: interrupted 状态跳过归档
        if (status != BridgeSubprocessStatus.Interrupted && !_isResuming && _deps.ArchiveSession is not null)
        {
            try
            {
                var archiveId = GetCompatId(work.SessionId);
                await _deps.ArchiveSession(archiveId, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "BridgeMain: archive failed for {SessionId} (non-fatal)", work.SessionId);
            }
        }

        // 取消 token 刷新
        _tokenRefresh?.Cancel(work.SessionId);

        // 容量唤醒: 会话完成后通知容量变化
        _deps.CapacityWake?.WakeUp();

        // 单会话模式: 非 interrupted 状态且非关机时退出循环 — 对齐 TS 端
        if (status != BridgeSubprocessStatus.Interrupted && _loopCts?.IsCancellationRequested != true
            && config.SpawnMode == BridgeSpawnMode.SingleSession)
        {
            _logger?.LogInformation("BridgeMain: single-session mode, session done — exiting loop");
            await _loopCts!.CancelAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 监控会话超时 — 对齐 TS 端: onSessionTimeout
    /// </summary>
    private async Task MonitorSessionTimeoutAsync(
        BridgeConfig config, BridgeWorkItem work, BridgeSubprocessHandle handle,
        int timeoutMs, CancellationToken ct)
    {
        try
        {
            await Task.Delay(timeoutMs, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // 超时: 终止子进程
        if (handle.IsRunning && !_tracker.TimedOutSessions.Contains(work.SessionId))
        {
            _tracker.MarkTimedOut(work.SessionId);
            var compatId = GetCompatId(work.SessionId);
            var timeoutMsg = $"Session timed out after {timeoutMs}ms";
            _logger?.LogWarning("BridgeMain: session {SessionId} timed out after {TimeoutMs}ms",
                work.SessionId, timeoutMs);
            // 对齐 TS 端: logEvent("tengu_bridge_session_timeout", {timeout_ms})
            TelemetryCount("tengu_bridge_session_timeout", new Dictionary<string, string>
            {
                ["timeout_ms"] = timeoutMs.ToString(),
            });
            handle.Kill();
        }
    }

    /// <summary>
    /// 清理所有会话 — 对齐 TS 端: 优雅关闭流程
    /// </summary>
    private async Task CleanupAllSessionsAsync(BridgeConfig config, CancellationToken ct)
    {
        if (_tracker.ActiveSessions.Count == 0) return;

        _logger?.LogInformation("BridgeMain: cleaning up {Count} sessions", _tracker.ActiveSessions.Count);

        // 1. SIGTERM 所有活跃子进程
        var handles = _tracker.ActiveSessions.Values.ToList();
        await _deps.Spawner.ShutdownAllAsync(handles, ct).ConfigureAwait(false);

        // 2. 停止所有工作项
        var workIds = _tracker.SessionWorkIds.Values.ToList();
        foreach (var workId in workIds)
        {
            await SafeStopWorkAsync(workId, ct).ConfigureAwait(false);
        }

        // 3. 归档所有会话 — 对齐 TS 端: archiveSession(compatId)
        if (_deps.ArchiveSession is not null)
        {
            var sessionIds = _tracker.ActiveSessions.Keys.ToList();
            foreach (var sessionId in sessionIds)
            {
                try
                {
                    var archiveId = GetCompatId(sessionId);
                    await _deps.ArchiveSession(archiveId, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "BridgeMain: archive failed for {SessionId} (non-fatal)", sessionId);
                }
            }
        }

        // 4. 清理 worktree — 对齐 TS 端: 清理所有会话的 worktree
        if (_tracker.SessionWorktrees.Count > 0 && _deps.WorktreeService is not null)
        {
            foreach (var (sessionId, _) in _tracker.SessionWorktrees)
            {
                try
                {
                    await _deps.WorktreeService.RemoveAgentWorktreeAsync(
                        sessionId, force: true, cancellationToken: ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "BridgeMain: worktree cleanup failed for {SessionId} (non-fatal)", sessionId);
                }
            }
        }

        // 5. 清理跟踪
        _tracker.ClearAll();

        // 6. 等待待清理任务 — 对齐 TS 端: pendingCleanups
        if (_pendingCleanups.Count > 0)
        {
            try
            {
                await _cleanupLock.WaitAsync(ct).ConfigureAwait(false);
                Task[] cleanups;
                try
                {
                    cleanups = _pendingCleanups.ToArray();
                }
                finally
                {
                    _cleanupLock.Release();
                }
                await Task.WhenAll(cleanups).WaitAsync(
                    TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "BridgeMain: pending cleanups timeout (non-fatal)");
            }
        }
    }
}
