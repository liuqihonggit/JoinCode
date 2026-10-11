namespace Core.Bridge;

/// <summary>
/// Bridge 运行编排器 — 从 BridgeMain 提取的运行入口职责类
/// 包含: RunBridgeFromContextAsync / RunDirectAsync / RunHeadlessAsync
/// 对齐 TS 端 bridgeMain.ts 的三种启动路径
/// </summary>
internal sealed class BridgeRunOrchestrator {
    private readonly BridgeMain _owner;

    internal BridgeRunOrchestrator(BridgeMain owner)
        => _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    internal async Task<BridgeMainResult> RunBridgeFromContextAsync(BridgeRunContext ctx, CancellationToken ct) {
        _owner._isResuming = ctx.IsResuming;
        _owner._pointerManager.ResumePointerDir = ctx.ResumePointerDir;

        var config = _owner.BuildConfig(ctx.Args, ctx.BaseUrl ?? throw new InvalidOperationException("BaseUrl is required"), ctx.ReuseEnvironmentId, ctx.EffectiveSpawnMode, ctx.IsResuming, ctx.SpawnModeSource);

        try {
            await _owner.RegisterEnvironmentAsync(config, ct).ConfigureAwait(false);
        } catch (Exception ex) {
            return _owner.HandleRegistrationError(ex);
        }

        _owner._logger?.LogInformation("BridgeMain: environment registered, ID={EnvId}", _owner.EnvironmentId);

        _owner.TelemetryCount("tengu_bridge_started", new Dictionary<string, string> {
            ["max_sessions"] = (ctx.Args.Capacity ?? (ctx.EffectiveSpawnMode == BridgeSpawnMode.SingleSession ? 1 : 5)).ToString(),
            ["has_debug_file"] = (ctx.Args.DebugFile is not null).ToString(),
            ["sandbox"] = ctx.Args.Sandbox.ToString(),
            ["debuglog"] = ctx.Args.DebugLog.ToString(),
            ["heartbeat_interval_ms"] = (_owner._deps.PollConfig?.HeartbeatIntervalMs ?? 30000).ToString(),
            ["spawn_mode"] = ctx.EffectiveSpawnMode?.ToValue() ?? "single-session",
            ["spawn_mode_source"] = ctx.SpawnModeSource.ToValue(),
            ["worktree_available"] = (_owner._deps.IsWorktreeAvailable?.Invoke() ?? false).ToString(),
        });

        var initialSessionId = ctx.ResumeSessionId;
        if (initialSessionId is null) {
            var createdSessionId = await _owner.TryCreateInitialSessionAsync(
                ctx.Args.Name, _owner._deps.PermissionMode, config, ct).ConfigureAwait(false);
            if (createdSessionId is not null) {
                initialSessionId = createdSessionId;
            }
        }

        if (_owner._deps.RegisterKeyboardListener is not null) {
            _owner._deps.RegisterKeyboardListener(_owner.OnKeyboardInputAsync);
        }

        if (_owner._deps.GetAccessToken is not null) {
            _owner._tokenRefresh = new BridgeTokenRefreshScheduler(
                new TokenRefreshOptions {
                    GetAccessToken = _owner._deps.GetAccessToken,
                    OnRefresh = (sessionId, oauthToken) => {
                        if (_owner._tracker.Sessions.IsV2(sessionId)) {
                            _owner._logger?.LogDebug("BridgeMain: refreshing v2 session {SessionId} via reconnectSession", sessionId);
                            _owner.ReconnectV2SessionFireAndForget(sessionId);
                        } else {
                            _owner.UpdateV1SessionTokenFireAndForget(sessionId, oauthToken);
                        }
                    },
                    Label = "bridge",
                    Logger = _owner._logger,
                });
        } else {
            _owner._tokenRefresh = _owner._deps.TokenRefreshScheduler;
        }

        _owner._deps.BridgeLogger?.PrintBanner(config, _owner.GetEnvironmentId());
        _owner._deps.BridgeLogger?.UpdateSessionCount(0, config.MaxSessions, config.SpawnMode);
        if (initialSessionId is not null) {
            var compatId = _owner.GetCompatId(initialSessionId);
            _owner._deps.BridgeLogger?.SetAttached(compatId);
        }
        if (config.GitRepoUrl is not null || config.Branch is not null) {
            var repoName = BridgeMain.ExtractRepoName(config.GitRepoUrl, _owner._deps.WorkingDirectory);
            _owner._deps.BridgeLogger?.SetRepoInfo(repoName, config.Branch ?? "");
        }

        await using var statusTimer = new Timer(_ => _owner.UpdateStatusDisplay(config), null,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        _owner._loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _owner._loopTask = _owner.RunBridgeLoopAsync(config, initialSessionId, _owner._loopCts.Token);
        try {
            await _owner._loopTask.ConfigureAwait(false);
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            _owner._logger?.LogInformation("BridgeMain: loop cancelled");
        } catch (Exception ex) {
            _owner._logger?.LogError(ex, "BridgeMain: loop failed");
            return new BridgeMainResult { Error = $"Loop failed: {ex.Message}" };
        }

        return new BridgeMainResult { Completed = true };
    }

    internal async Task<BridgeMainResult> RunDirectAsync(BridgeMainArgs args, CancellationToken ct) {
        // 1. 帮助检查
        if (args.Help) {
            return new BridgeMainResult { HelpText = BridgeMainArgsParser.GetHelpText() };
        }

        // 2. 参数错误检查
        if (args.HasError) {
            return new BridgeMainResult { Error = args.Error };
        }

        // 2.5 permissionMode 早期验证 — 对齐 TS 端: PERMISSION_MODES 校验
        if (_owner._deps.PermissionMode is not null) {
            if (!BridgeMain.ValidPermissionModes.Contains(_owner._deps.PermissionMode)) {
                return new BridgeMainResult { Error = $"Invalid permission mode '{_owner._deps.PermissionMode}'. Valid modes: default, plan, auto-accept, bubble" };
            }
        }

        // 3. OAuth 认证 — 对齐 TS 端: if (!getBridgeAccessToken())
        var accessToken = _owner.GetValidAccessToken();
        if (accessToken is null) {
            _owner._logger?.LogDebug("BridgeMain: no access token — skipping");
            return new BridgeMainResult { Error = "No access token available. Please login first." };
        }

        // 4. 首次远程确认 — 对齐 TS 端: remoteDialogSeen 检查 + readline y/n 对话框
        var remoteDialogSeen = _owner._deps.CheckRemoteDialogAccepted?.Invoke() ?? true;
        if (!remoteDialogSeen) {
            // 无对话框回调（非交互模式）: 直接拒绝
            if (_owner._deps.RemoteControlDialog is null) {
                _owner._logger?.LogDebug("BridgeMain: remote control not accepted — skipping");
                return new BridgeMainResult { Error = "Remote control not accepted." };
            }

            // 对齐 TS 端: if (!getGlobalConfig().remoteDialogSeen) → 弹出 readline 对话框
            var accepted = await _owner._deps.RemoteControlDialog(ct).ConfigureAwait(false);
            // 无论用户回答什么，都保存 remoteDialogSeen=true 防止下次再问
            _owner._deps.MarkRemoteDialogSeen?.Invoke();
            if (!accepted) {
                _owner._logger?.LogDebug("BridgeMain: remote control declined by user");
                return new BridgeMainResult { Error = "Remote control not accepted." };
            }
        }

        // 5. HTTPS 检查 — 对齐 TS 端: 非localhost必须HTTPS
        var baseUrl = _owner._deps.GetBaseUrl();
        var httpsError = BridgeMain.ValidateHttpsUrl(baseUrl);
        if (httpsError is not null) {
            _owner._logger?.LogDebug("BridgeMain: non-HTTPS URL — skipping");
            return new BridgeMainResult { Error = httpsError };
        }

        // 6. --continue 恢复 — 对齐 TS 端: readBridgePointerAcrossWorktrees
        string? resumeSessionId = null;
        string? reuseEnvironmentId = null;
        if (args.ContinueSession) {
            var found = await _owner._deps.PointerService.ReadAcrossWorktreesAsync(
                _owner._deps.WorkingDirectory, ct).ConfigureAwait(false);
            if (found is not null) {
                var (pointerWithAge, pointerDir) = found.Value;
                resumeSessionId = pointerWithAge.Pointer.SessionId;
                reuseEnvironmentId = pointerWithAge.Pointer.EnvironmentId;
                _owner._pointerManager.ResumePointerDir = pointerDir; // 记录指针来源目录 — 恢复失败时清除正确的指针
                var ageMin = Math.Round(pointerWithAge.AgeMs / 60_000.0);
                var ageStr = ageMin < 60 ? $"{ageMin}m" : $"{Math.Round(ageMin / 60.0)}h";
                var fromWt = pointerDir != _owner._deps.WorkingDirectory ? $" from worktree {pointerDir}" : "";
                _owner._logger?.LogInformation("BridgeMain: resuming session {SessionId} ({Age} ago){FromWt}",
                    resumeSessionId, ageStr, fromWt);
            } else {
                _owner._logger?.LogDebug("BridgeMain: --continue but no valid pointer found in this directory or its worktrees");
            }
        } else if (args.SessionId is not null) {
            resumeSessionId = args.SessionId;
            // 对齐 TS 端: getBridgeSession → reuseEnvironmentId
            // 通过 API 获取 session 的 environment_id，用于 idempotent 注册
            try {
                var envId = await _owner._deps.ApiClient.GetBridgeSessionEnvironmentIdAsync(
                    resumeSessionId, ct).ConfigureAwait(false);
                if (envId is not null) {
                    reuseEnvironmentId = envId;
                    _owner._logger?.LogInformation("BridgeMain: resuming session {SessionId} on environment {EnvId}",
                        resumeSessionId, envId);
                } else {
                    _owner._logger?.LogDebug("BridgeMain: session {SessionId} has no environment_id, will register fresh", resumeSessionId);
                }
            } catch (Exception ex) {
                _owner._logger?.LogDebug(ex, "BridgeMain: getBridgeSession failed for {SessionId} (non-fatal)", resumeSessionId);
            }
        }

        // 7. Spawn 模式选择 — 对齐 TS 端: spawnMode + spawnModeSource 优先级链
        // 优先级: resume > flag > saved > gate_default
        // 对齐 TS 端: GrowthBook gate tengu_ccr_bridge_multi_session
        var multiSessionEnabled = _owner._deps.IsMultiSessionSpawnEnabled?.Invoke() ?? false;
        var effectiveSpawnMode = args.SpawnMode;
        var spawnModeSource = BridgeSpawnModeSource.GateDefault; // 默认兜底

        if (resumeSessionId is not null) {
            // 优先级1: resume — 恢复会话强制 single-session
            effectiveSpawnMode = BridgeSpawnMode.SingleSession;
            spawnModeSource = BridgeSpawnModeSource.Resume;
        } else if (args.SpawnMode is not null) {
            // 优先级2: flag — 用户通过命令行参数显式指定
            spawnModeSource = BridgeSpawnModeSource.Flag;
        } else if (_owner._deps.GetSavedSpawnMode is not null && multiSessionEnabled) {
            // 优先级3: saved — 对齐 TS 端: gate 关闭时不加载已保存偏好
            // 原因: GrowthBook 回滚时，已保存的偏好不应悄悄重新启用多会话行为
            var savedMode = _owner._deps.GetSavedSpawnMode();
            if (savedMode is not null) {
                effectiveSpawnMode = savedMode;
                spawnModeSource = BridgeSpawnModeSource.Saved;
            }
        }

        // 首次运行对话框 — 对齐 TS 端: multiSessionEnabled && !savedSpawnMode && worktreeAvailable && ...
        if (multiSessionEnabled &&
            spawnModeSource == BridgeSpawnModeSource.GateDefault &&
            args.SpawnMode is null &&
            resumeSessionId is null &&
            _owner._deps.SpawnModeDialog is not null &&
            _owner._deps.IsWorktreeAvailable?.Invoke() == true) {
            var chosenMode = await _owner._deps.SpawnModeDialog(ct).ConfigureAwait(false);
            effectiveSpawnMode = chosenMode;
            // 对话框选择后保存偏好 — 来源仍为 gate_default（对话框是默认路径的一部分）
            _owner._deps.SaveSpawnModePreference?.Invoke(chosenMode);
            _owner._logger?.LogInformation("BridgeMain: spawn mode chosen via dialog: {Mode}", chosenMode.ToValue());
        }

        // 8. 构建 BridgeConfig — 对齐 TS 端 config 构建
        var isResuming = resumeSessionId is not null;
        _owner._isResuming = isResuming; // 保存到实例字段 — 可恢复关闭时使用
        var config = _owner.BuildConfig(args, baseUrl, reuseEnvironmentId, effectiveSpawnMode, isResuming, spawnModeSource);

        // 8. 环境注册 — 对齐 TS 端: api.registerBridgeEnvironment(config)
        try {
            await _owner.RegisterEnvironmentAsync(config, ct).ConfigureAwait(false);
        } catch (Exception ex) {
            return _owner.HandleRegistrationError(ex);
        }

        _owner._logger?.LogInformation("BridgeMain: environment registered, ID={EnvId}", _owner.EnvironmentId);

        // 对齐 TS 端: logEvent("tengu_bridge_started", {...})
        _owner.TelemetryCount("tengu_bridge_started", new Dictionary<string, string> {
            ["max_sessions"] = (args.Capacity ?? (effectiveSpawnMode == BridgeSpawnMode.SingleSession ? 1 : 5)).ToString(),
            ["has_debug_file"] = (args.DebugFile is not null).ToString(),
            ["sandbox"] = args.Sandbox.ToString(),
            ["debuglog"] = args.DebugLog.ToString(),
            ["heartbeat_interval_ms"] = (_owner._deps.PollConfig?.HeartbeatIntervalMs ?? 30000).ToString(),
            ["spawn_mode"] = effectiveSpawnMode?.ToValue() ?? "single-session",
            ["spawn_mode_source"] = spawnModeSource.ToValue(),
            ["worktree_available"] = (_owner._deps.IsWorktreeAvailable?.Invoke() ?? false).ToString(),
        });

        // 9.5 创建初始会话 — 对齐 TS 端: createBridgeSession
        // preCreateSession 且非 KAIROS 恢复模式时，预创建一个会话
        var initialSessionId = resumeSessionId;
        if (initialSessionId is null) {
            var createdSessionId = await _owner.TryCreateInitialSessionAsync(
                args.Name, _owner._deps.PermissionMode, config, ct).ConfigureAwait(false);
            if (createdSessionId is not null) {
                initialSessionId = createdSessionId;
            }
        }

        // 9. 单会话模式下写入崩溃恢复指针
        if (config.SpawnMode == BridgeSpawnMode.SingleSession && resumeSessionId is null) {
            // 先不写指针，等会话创建后再写
        }

        // 10. 注册键盘监听 — 对齐 TS 端: process.stdin.setRawMode(true) + on('data', onStdinData)
        if (_owner._deps.RegisterKeyboardListener is not null) {
            _owner._deps.RegisterKeyboardListener(_owner.OnKeyboardInputAsync);
        }

        // 10.5 创建 Token 刷新调度器 — 对齐 TS 端 createTokenRefreshScheduler + v1/v2 分支
        // v2 会话: reconnectSession 触发服务端重新派发（CC-1263）
        // v1 会话: 直接 updateAccessToken 传递 OAuth token 给子进程
        if (_owner._deps.GetAccessToken is not null) {
            _owner._tokenRefresh = new BridgeTokenRefreshScheduler(
                new TokenRefreshOptions {
                    GetAccessToken = _owner._deps.GetAccessToken,
                    OnRefresh = (sessionId, oauthToken) => {
                        if (_owner._tracker.Sessions.IsV2(sessionId)) {
                            // 对齐 TS 端: v2 会话通过 reconnectSession 刷新 — 服务端重新派发带新 JWT 的工作项
                            // 对齐 TS 端: 双 ID 尝试 — 先 compatId(session_*), 失败再 infraId(cse_*)
                            _owner._logger?.LogDebug("BridgeMain: refreshing v2 session {SessionId} via reconnectSession", sessionId);
                            _owner.ReconnectV2SessionFireAndForget(sessionId);
                        } else {
                            // 对齐 TS 端: v1 会话直接更新 OAuth token
                            _owner.UpdateV1SessionTokenFireAndForget(sessionId, oauthToken);
                        }
                    },
                    Label = "bridge",
                    Logger = _owner._logger,
                });
        } else {
            _owner._tokenRefresh = _owner._deps.TokenRefreshScheduler; // 回退到外部注入的 scheduler
        }

        // 10.8 Logger 初始化调用 — 对齐 TS 端 printBanner/setRepoInfo/setAttached
        _owner._deps.BridgeLogger?.PrintBanner(config, _owner.GetEnvironmentId());
        _owner._deps.BridgeLogger?.UpdateSessionCount(0, config.MaxSessions, config.SpawnMode);
        if (initialSessionId is not null) {
            var compatId = _owner.GetCompatId(initialSessionId);
            _owner._deps.BridgeLogger?.SetAttached(compatId);
        }
        if (config.GitRepoUrl is not null || config.Branch is not null) {
            var repoName = BridgeMain.ExtractRepoName(config.GitRepoUrl, _owner._deps.WorkingDirectory);
            _owner._deps.BridgeLogger?.SetRepoInfo(repoName, config.Branch ?? "");
        }

        await using var statusTimer = new Timer(_ => _owner.UpdateStatusDisplay(config), null,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        // 12. 启动主循环
        _owner._loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _owner._loopTask = _owner.RunBridgeLoopAsync(config, initialSessionId, _owner._loopCts.Token);
        try {
            await _owner._loopTask.ConfigureAwait(false);
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            _owner._logger?.LogInformation("BridgeMain: loop cancelled");
        } catch (Exception ex) {
            _owner._logger?.LogError(ex, "BridgeMain: loop failed");
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
    public async Task RunHeadlessAsync(BridgeHeadlessOpts opts, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(opts);

        // ===== 永久性验证 1: 工作区信任检查 — 对齐 TS 端 checkHasTrustDialogAccepted =====
        if (opts.CheckWorkspaceTrusted is not null && !opts.CheckWorkspaceTrusted()) {
            throw new BridgeHeadlessPermanentError(
                $"Workspace not trusted: {opts.Dir}. Run '{BrandConstants.CliCommandName}' in that directory first to accept the trust dialog.");
        }

        // ===== 瞬态验证: Token 检查 — 对齐 TS 端 getAccessToken =====
        // Headless 模式使用 opts.GetAccessToken()，而非 _deps.GetAccessToken()
        var accessToken = opts.GetAccessToken();
        if (string.IsNullOrEmpty(accessToken)) {
            throw new InvalidOperationException("No access token available. AuthManager may provide one in the next cycle.");
        }

        // ===== 永久性验证 2: HTTPS 检查 — 对齐 TS 端 HTTP URL 检查 =====
        var baseUrl = opts.GetBaseUrl();
        var httpsError = BridgeMain.ValidateHttpsUrl(baseUrl);
        if (httpsError is not null) {
            throw new BridgeHeadlessPermanentError(
                "Remote Control base URL uses HTTP. Only HTTPS or localhost HTTP is allowed.");
        }

        // ===== 永久性验证 3: Worktree 可用性检查 — 对齐 TS 端 worktree 检查 =====
        if (opts.SpawnMode == BridgeSpawnMode.Worktree) {
            var hasGitRepo = opts.CheckGitRepoExists?.Invoke(opts.Dir) ?? false;
            var hasWorktreeHooks = opts.CheckWorktreeCreateHooks?.Invoke() ?? false;
            if (!hasGitRepo && !hasWorktreeHooks) {
                throw new BridgeHeadlessPermanentError(
                    $"Worktree mode requires a git repository or WorktreeCreate hooks. Directory {opts.Dir} has neither.");
            }
        }

        // ===== 构建 BridgeConfig — 对齐 TS 端 headless config 构建 =====
        // 对齐 TS 端: sessionIngressUrl — ant 开发环境下可能与 baseUrl 不同
        var headlessSessionIngressUrl = baseUrl;
        var userType = Environment.GetEnvironmentVariable("USER_TYPE");
        var ingressOverride = Environment.GetEnvironmentVariable(JccEnvVar.BridgeSessionIngressUrl.ToValue());
        if (!string.IsNullOrEmpty(ingressOverride) && string.Equals(userType, "ant", StringComparison.OrdinalIgnoreCase)) {
            headlessSessionIngressUrl = ingressOverride;
        }

        var config = new BridgeConfig {
            Dir = opts.Dir,
            MachineName = Environment.MachineName,
            Branch = _owner._deps.GitBranch ?? "main",
            GitRepoUrl = _owner._deps.GitRepoUrl,
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
        try {
            await _owner.RegisterEnvironmentAsync(config, ct).ConfigureAwait(false);
        } catch (InvalidOperationException) {
            throw; // null response — 透传
        } catch (Exception ex) {
            // 瞬态错误 — supervisor 会重试
            throw new InvalidOperationException($"Registration failed: {ex.Message}", ex);
        }

        _owner._logger?.LogInformation("BridgeMain(headless): environment registered, ID={EnvId}", _owner.EnvironmentId);

        // ===== 可选: 预创建初始会话 — 对齐 TS 端 createSessionOnStart =====
        string? initialSessionId = null;
        if (opts.CreateSessionOnStart) {
            initialSessionId = await _owner.TryCreateInitialSessionAsync(
                opts.Name, opts.PermissionMode, config, ct).ConfigureAwait(false);
        }

        // Headless logger 初始化 — 对齐 TS 端: logger.printBanner(config, environmentId)
        _owner._deps.BridgeLogger?.PrintBanner(config, _owner.GetEnvironmentId());

        // ===== 进入 runBridgeLoop — 共享同一个轮询循环 =====
        _owner._loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _owner._loopTask = _owner.RunBridgeLoopAsync(config, initialSessionId, _owner._loopCts.Token);
        try {
            await _owner._loopTask.ConfigureAwait(false);
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            _owner._logger?.LogInformation("BridgeMain(headless): loop cancelled");
        } catch (BridgeHeadlessPermanentError) {
            throw; // 透传永久性错误
        } catch (BridgeFatalError ex) {
            // 401: 尝试通过 OnAuth401 刷新
            if (ex.StatusCode == 401 && opts.OnAuth401 is not null) {
                var refreshed = await opts.OnAuth401(accessToken).ConfigureAwait(false);
                if (!refreshed) {
                    throw new InvalidOperationException($"Auth refresh failed: {ex.Message}", ex);
                }
                // 刷新成功 — supervisor 会重新启动 headless
                return;
            }

            throw new InvalidOperationException($"Bridge fatal error: {ex.Message}", ex);
        } catch (Exception ex) {
            throw new InvalidOperationException($"Loop failed: {ex.Message}", ex);
        }
    }
}