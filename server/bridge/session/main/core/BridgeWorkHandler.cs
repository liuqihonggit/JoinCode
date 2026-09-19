namespace Core.Bridge;

/// <summary>
/// Bridge 工作处理 — 从 BridgeMain 提取的工作项处理逻辑
/// 包含: HandleWorkAsync（入口）→ HandleWorkViaPipelineAsync（管道路径）/ HandleWorkDirectAsync（直接路径）
/// </summary>
internal sealed class BridgeWorkHandler {
    private readonly BridgeMain _owner;

    internal BridgeWorkHandler(BridgeMain owner) => _owner = owner;

    /// <summary>
    /// 处理工作项 — 对齐 TS 端 bridgeMain.ts 的工作处理流程
    /// 流程: 解码 WorkSecret → healthcheck 处理 → ACK(sessionToken) → CCR v2 判断 → 生成子进程
    /// </summary>
    internal async Task HandleWorkAsync(BridgeConfig config, BridgeWorkItem work, CancellationToken ct) {
        if (_owner._handleWorkPipeline is not null) {
            await HandleWorkViaPipelineAsync(config, work, ct).ConfigureAwait(false);
            return;
        }

        await HandleWorkDirectAsync(config, work, ct).ConfigureAwait(false);
    }

    private async Task HandleWorkViaPipelineAsync(BridgeConfig config, BridgeWorkItem work, CancellationToken ct) {
        var ctx = new HandleWorkContext {
            Config = config,
            Work = work,
            CancellationToken = ct,
            EnvironmentId = _owner.EnvironmentId,
            Tracker = _owner._tracker,
            StopWorkAsync = (workId, token) => _owner._workApi.StopWorkWithRetryAsync(_owner.EnvironmentId, workId, token),
            TrackCleanup = task => _owner.TrackCleanup(task),
            CapacityWake = () => _owner._deps.CapacityWake?.WakeUp(),
            TelemetryCount = (name, props) => _owner.TelemetryCount(name, props),
            Spawner = _owner._deps.Spawner,
            PollConfig = _owner._deps.PollConfig,
            SpawnDir = _owner.DetermineSpawnDir(config, work),
            GetAccessToken = _owner._deps.GetAccessToken,
            PermissionMode = _owner._deps.PermissionMode,
            OnPermissionRequest = _owner._deps.OnPermissionRequest is not null
                ? (req, token) => _owner._deps.OnPermissionRequest(work.SessionId, req, token)
                : null,
            OnActivity = _owner._deps.OnActivity is not null
                ? activity => _owner._deps.OnActivity(work.SessionId, activity)
                : null,
            OnFirstUserMessage = text => _owner.OnFirstUserMessage(work.SessionId, text, config),
        };

        var pipeline = _owner._handleWorkPipeline;
        if (pipeline is not null) {
            await pipeline.ExecuteAsync(ctx, ct).ConfigureAwait(false);
        }

        if (!ctx.ShortCircuited && ctx.Handle is not null) {
            var compatId = SessionIdCompat.ToCompatSessionId(work.SessionId);
            _owner._deps.BridgeLogger?.AddSession(compatId, BridgeMain.BuildRemoteSessionUrl(compatId, config));
            _owner._deps.BridgeLogger?.SetAttached(compatId);

            if (config.SpawnMode == BridgeSpawnMode.SingleSession) {
                await _owner._pointerManager.WritePointerAsync(config, work.SessionId, _owner.EnvironmentId).ConfigureAwait(false);
                _owner._pointerManager.StartPointerRefreshTimer(config, work.SessionId, _owner.EnvironmentId);
            }

            _ = _owner.MonitorSessionCompletionAsync(config, work, ctx.Handle, ct);

            var timeoutMs = config.SessionTimeoutMs > 0 ? config.SessionTimeoutMs : 24 * 60 * 60 * 1000;
            _ = _owner.MonitorSessionTimeoutAsync(config, work, ctx.Handle, timeoutMs, ct);

            if (ctx.SessionIngressToken is not null && _owner._tokenRefresh is not null) {
                _owner._tokenRefresh.Schedule(work.SessionId, ctx.SessionIngressToken);
            }

            _ = _owner.FetchSessionTitleAsync(work.SessionId, config);
        }
    }

    private async Task HandleWorkDirectAsync(BridgeConfig config, BridgeWorkItem work, CancellationToken ct) {
        _owner._logger?.LogInformation("BridgeMain: received work, WorkId={WorkId}, SessionId={SessionId}, WorkType={WorkType}",
            work.WorkId, work.SessionId, work.WorkType);

        // 容量检查 — 对齐 TS 端: activeSessions.size >= config.maxSessions
        if (_owner._tracker.Sessions.Count >= config.MaxSessions) {
            _owner._logger?.LogWarning("BridgeMain: at capacity, skipping work {WorkId}", work.WorkId);
            return;
        }

        // 去重检查 — 对齐 TS 端: completedWorkIds
        // 服务端可能在处理 stopWork 请求前重新投递过期工作项
        if (_owner._tracker.WorkCompletion.IsCompleted(work.WorkId)) {
            _owner._logger?.LogDebug("BridgeMain: skipping duplicate work {WorkId}", work.WorkId);
            // 容量节流 — 对齐 TS 端: 持续的过期重投递会导致 tight-loop
            // at-capacity 时 sleep 一段时间避免空转
            if (_owner._tracker.Sessions.Count >= config.MaxSessions) {
                var pollConfig = _owner._deps.PollConfig;
                var delayMs = pollConfig?.NonExclusiveHeartbeatIntervalMs > 0
                    ? pollConfig.NonExclusiveHeartbeatIntervalMs
                    : pollConfig?.HeartbeatIntervalMs ?? 30000;
                try {
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                } catch (OperationCanceledException) { }
            }
            return;
        }

        // ===== P0-1: 解码 WorkSecret — 对齐 TS 端 decodeWorkSecret =====
        // TS 端: secret = decodeWorkSecret(work.secret)
        // 解码后的 session_ingress_token 用于 ACK、spawn、tokenRefresh
        BridgeWorkSecret? secret = null;
        if (!string.IsNullOrEmpty(work.Secret)) {
            try {
                secret = BridgeWorkSecretDecoder.DecodeWorkSecret(work.Secret);
                _owner._logger?.LogDebug("BridgeMain: decoded work secret for WorkId={WorkId}, useCodeSessions={UseCcrV2}",
                    work.WorkId, secret.UseCodeSessions);
            } catch (Exception ex) {
                _owner._logger?.LogError(ex, "BridgeMain: failed to decode work secret for WorkId={WorkId}", work.WorkId);
                // 对齐 TS 端: logEvent("tengu_bridge_work_secret_failed")
                _owner.TelemetryCount("tengu_bridge_work_secret_failed");
                // 对齐 TS 端: 解码失败 → stopWork（用 OAuth token）+ 标记完成 + 跳过
                _owner._tracker.WorkCompletion.Mark(work.WorkId);
                _owner.TrackCleanup(_owner._workApi.StopWorkWithRetryAsync(_owner.EnvironmentId, work.WorkId, ct));
                _owner._deps.CapacityWake?.WakeUp();
                return;
            }
        }

        // 提取解码后的关键值 — 对齐 TS 端 secret.session_ingress_token
        var sessionIngressToken = secret?.SessionIngressToken ?? work.SessionIngressToken;
        var secretApiBaseUrl = secret?.ApiBaseUrl ?? work.ApiBaseUrl;

        // ===== P0-4: Healthcheck 工作类型处理 — 对齐 TS 端 case 'healthcheck' =====
        if (string.Equals(work.WorkType, "healthcheck", StringComparison.OrdinalIgnoreCase)) {
            // 对齐 TS 端: await ackWork() → 仅记录日志
            if (sessionIngressToken is not null) {
                await _owner._workApi.AckWorkAsync(_owner.EnvironmentId, work.WorkId, sessionIngressToken, ct).ConfigureAwait(false);
            }
            _owner._logger?.LogDebug("BridgeMain: healthcheck received");
            return;
        }

        // 已有会话: 更新 token — 对齐 TS 端: existingHandle 路径
        // TS 端使用 secret.session_ingress_token 更新（而非 OAuth token）
        var existingHandle = _owner._tracker.Sessions.GetHandle(work.SessionId);
        if (existingHandle is not null) {
            if (sessionIngressToken is not null && sessionIngressToken != existingHandle.AccessToken) {
                await existingHandle.UpdateAccessTokenAsync(sessionIngressToken, ct).ConfigureAwait(false);
                _owner._logger?.LogDebug("BridgeMain: updated token for existing session {SessionId}", work.SessionId);
            }
            // 存储 ingress token — 对齐 TS 端: sessionIngressTokens.set(sessionId, secret.session_ingress_token)
            if (sessionIngressToken is not null) {
                _owner._tracker.Sessions.UpdateIngressToken(work.SessionId, sessionIngressToken);
            }
            return;
        }

        // ===== P0-5: ACK 使用解码后的 session_ingress_token — 对齐 TS 端 acknowledgeWork =====
        // TS 端: api.acknowledgeWork(environmentId, work.id, secret.session_ingress_token)
        // ACK 必须在确认要处理该工作项之后调用（at-capacity 守卫已通过）
        if (sessionIngressToken is not null) {
            await _owner._workApi.AckWorkAsync(_owner.EnvironmentId, work.WorkId, sessionIngressToken, ct).ConfigureAwait(false);
        } else {
            // 无 sessionToken 时仍尝试 ACK（兼容旧版服务端）
            try {
                await _owner._deps.ApiClient.AcknowledgeWorkAsync(
                    _owner.GetEnvironmentId(), work.WorkId, ct: ct).ConfigureAwait(false);
            } catch (Exception ex) {
                _owner._logger?.LogWarning(ex, "BridgeMain: ACK failed for work {WorkId}", work.WorkId);
                return;
            }
        }

        // ===== P0-2: CCR v2 路径 — 对齐 TS 端 use_code_sessions =====
        // TS 端: if (secret.use_code_sessions === true || isEnvTruthy(CLAUDE_BRIDGE_USE_CCR_V2))
        // V1/V2 切换前确保网络可用(统一入口)
        await BridgeRuntimeGate.WaitForNetworkAsync(_owner._networkService, _owner._logger, ct).ConfigureAwait(false);

        var useCcrV2 = false;
        int? workerEpoch = null;
        string sdkUrl;

        if (BridgeRuntimeGate.ShouldUseCcrV2(secret?.UseCodeSessions) && secretApiBaseUrl is not null && sessionIngressToken is not null) {
            // CCR v2: buildCCRv2SdkUrl + registerWorker（最多2次重试）
            sdkUrl = BridgeWorkSecretDecoder.BuildCCRv2SdkUrl(secretApiBaseUrl, work.SessionId);

            for (var attempt = 1; attempt <= 2; attempt++) {
                try {
                    workerEpoch = (int)await BridgeWorkSecretDecoder.RegisterWorkerAsync(
                        sdkUrl, sessionIngressToken, _owner._deps.ApiClient.HttpClient, ct).ConfigureAwait(false);
                    useCcrV2 = true;
                    _owner._logger?.LogInformation(
                        "BridgeMain: CCR v2 registered worker, SessionId={SessionId}, epoch={Epoch}, attempt={Attempt}",
                        work.SessionId, workerEpoch, attempt);
                    break;
                } catch (Exception ex) {
                    if (attempt < 2) {
                        _owner._logger?.LogDebug(ex,
                            "BridgeMain: CCR v2 registerWorker attempt {Attempt} failed, retrying", attempt);
                        await Task.Delay(2000, ct).ConfigureAwait(false);
                        continue;
                    }

                    _owner._logger?.LogError(ex,
                        "BridgeMain: CCR v2 worker registration failed for session {SessionId}", work.SessionId);
                    _owner._tracker.WorkCompletion.Mark(work.WorkId);
                    _owner.TrackCleanup(_owner._workApi.StopWorkWithRetryAsync(_owner.EnvironmentId, work.WorkId, ct));
                    _owner._deps.CapacityWake?.WakeUp();
                    return;
                }
            }
        } else {
            // v1 路径: buildSdkUrl — 对齐 TS 端: buildSdkUrl(config.sessionIngressUrl, sessionId)
            var ingressUrl = secretApiBaseUrl ?? config.SessionIngressUrl;
            sdkUrl = BridgeWorkSecretDecoder.BuildSdkUrl(ingressUrl, work.SessionId);
        }

        // 生成子进程 — 对齐 TS 端: safeSpawn(spawner, opts, dir)
        // Worktree 模式: 为非初始会话创建 git worktree — 对齐 TS 端 createWorktreeForSession
        var spawnDir = _owner.DetermineSpawnDir(config, work);
        string? createdWorktreePath = null; // 跟踪已创建的 worktree，spawn 失败时需要清理
        if (config.SpawnMode == BridgeSpawnMode.Worktree && _owner._deps.WorktreeService is not null) {
            try {
                var worktreeResult = await _owner._deps.WorktreeService.CreateAgentWorktreeAsync(
                    work.SessionId,
                    config.Dir,
                    cancellationToken: ct).ConfigureAwait(false);

                if (worktreeResult.Success && worktreeResult.Session?.WorktreePath is not null) {
                    spawnDir = worktreeResult.Session.WorktreePath;
                    createdWorktreePath = worktreeResult.Session.WorktreePath;
                    _owner._tracker.Sessions.UpdateWorktree(work.SessionId, worktreeResult.Session.WorktreePath);
                    _owner._logger?.LogInformation("BridgeMain: created worktree for session {SessionId} at {Path}",
                        work.SessionId, worktreeResult.Session.WorktreePath);
                } else {
                    // P3-4: 对齐 TS 端 — worktree 创建失败时 stopWork + completedWorkIds，而非 fallback
                    _owner._logger?.LogError("BridgeMain: worktree creation failed for session {SessionId}, stopping work",
                        work.SessionId);
                    _owner._tracker.WorkCompletion.Mark(work.WorkId);
                    await _owner._workApi.SafeStopWorkAsync(_owner.EnvironmentId, work.WorkId, ct).ConfigureAwait(false);
                    return;
                }
            } catch (Exception ex) {
                // P3-4: 对齐 TS 端 — worktree 创建异常时 stopWork + completedWorkIds
                _owner._logger?.LogError(ex, "BridgeMain: worktree creation error for session {SessionId}, stopping work",
                    work.SessionId);
                _owner._tracker.WorkCompletion.Mark(work.WorkId);
                await _owner._workApi.SafeStopWorkAsync(_owner.EnvironmentId, work.WorkId, ct).ConfigureAwait(false);
                return;
            }
        }

        // 对齐 TS 端: accessToken 使用 secret.session_ingress_token（而非 OAuth token）
        var accessTokenForSpawn = sessionIngressToken ?? _owner._deps.GetAccessToken();

        var spawnOptions = new BridgeSubprocessOptions {
            SessionId = work.SessionId,
            SdkUrl = sdkUrl,
            AccessToken = accessTokenForSpawn,
            Dir = spawnDir,
            DebugLog = config.DebugLog,
            Sandbox = config.Sandbox,
            DebugFile = config.DebugFile,
            PermissionMode = _owner._deps.PermissionMode,
            UseCcrV2 = useCcrV2,
            WorkerEpoch = workerEpoch,
            // 对齐 TS 端 SessionSpawnOpts.onFirstUserMessage — 首条用户消息回调用于派生标题
            OnFirstUserMessage = text => _owner.OnFirstUserMessage(work.SessionId, text, config),
            // 对齐 TS 端 deps.onPermissionRequest — 权限请求回调
            OnPermissionRequest = _owner._deps.OnPermissionRequest is not null
                ? (req, token) => _owner._deps.OnPermissionRequest(work.SessionId, req, token)
                : null,
            // 对齐 TS 端 deps.onActivity — 活动回调
            OnActivity = _owner._deps.OnActivity is not null
                ? activity => _owner._deps.OnActivity(work.SessionId, activity)
                : null,
        };

        BridgeSubprocessHandle handle;
        try {
            handle = await _owner._deps.Spawner.SpawnAsync(spawnOptions).ConfigureAwait(false);
        } catch (Exception ex) {
            _owner._logger?.LogError(ex, "BridgeMain: spawn failed for session {SessionId}", work.SessionId);

            // P3-5: 对齐 TS 端 — spawn 失败时清理已创建的 worktree + completedWorkIds + stopWork
            if (createdWorktreePath is not null && _owner._deps.WorktreeService is not null) {
                try {
                    await _owner._deps.WorktreeService.RemoveAgentWorktreeAsync(
                        work.SessionId, force: true, cancellationToken: ct).ConfigureAwait(false);
                    _owner._tracker.Sessions.RemoveWorktree(work.SessionId, out _);
                } catch (Exception cleanupEx) {
                    _owner._logger?.LogDebug(cleanupEx, "BridgeMain: worktree cleanup after spawn failure for {SessionId} (non-fatal)", work.SessionId);
                }
            }

            _owner._tracker.WorkCompletion.Mark(work.WorkId);
            await _owner._workApi.SafeStopWorkAsync(_owner.EnvironmentId, work.WorkId, ct).ConfigureAwait(false);
            return;
        }

        // 注册跟踪
        var compatId = SessionIdCompat.ToCompatSessionId(work.SessionId);
        _owner._tracker.Sessions.Register(work.SessionId, new BridgeSessionState {
            Handle = handle,
            StartTime = DateTime.UtcNow,
            WorkId = work.WorkId,
            IngressToken = sessionIngressToken,
            WorktreePath = createdWorktreePath,
            CompatId = compatId,
            IsV2 = useCcrV2,
        });

        // 注册到 logger 会话列表 — 对齐 TS 端: logger.addSession(compatSessionId, url)
        _owner._deps.BridgeLogger?.AddSession(compatId, BridgeMain.BuildRemoteSessionUrl(compatId, config));
        _owner._deps.BridgeLogger?.SetAttached(compatId);

        // 单会话模式: 写入崩溃恢复指针
        if (config.SpawnMode == BridgeSpawnMode.SingleSession) {
            await _owner._pointerManager.WritePointerAsync(config, work.SessionId, _owner.EnvironmentId).ConfigureAwait(false);
            _owner._pointerManager.StartPointerRefreshTimer(config, work.SessionId, _owner.EnvironmentId);
        }

        // 会话完成回调 — 对齐 TS 端: handle.done.then(onSessionDone)
        _ = _owner.MonitorSessionCompletionAsync(config, work, handle, ct);

        // 会话超时看门狗 — 对齐 TS 端: setTimeout(onSessionTimeout, timeoutMs)
        var timeoutMs = config.SessionTimeoutMs > 0 ? config.SessionTimeoutMs : 24 * 60 * 60 * 1000;
        _ = _owner.MonitorSessionTimeoutAsync(config, work, handle, timeoutMs, ct);

        // Token 刷新调度 — 对齐 TS 端: tokenRefresh?.schedule(sessionId, secret.session_ingress_token)
        if (sessionIngressToken is not null && _owner._tokenRefresh is not null) {
            _owner._tokenRefresh.Schedule(work.SessionId, sessionIngressToken);
        }

        _owner._logger?.LogInformation("BridgeMain: session {SessionId} started, active={Active}/{Max}, ccrV2={CcrV2}",
            work.SessionId, _owner._tracker.Sessions.Count, config.MaxSessions, useCcrV2);

        // 对齐 TS 端: logEvent("tengu_bridge_session_started", {...})
        _owner.TelemetryCount("tengu_bridge_session_started", new Dictionary<string, string> {
            ["active_sessions"] = _owner._tracker.Sessions.Count.ToString(),
            ["spawn_mode"] = config.SpawnMode.ToValue(),
            ["in_worktree"] = (_owner._tracker.Sessions.HasWorktree(work.SessionId)).ToString(),
        });

        // 对齐 TS 端: fetchSessionTitle — spawn 后立即异步获取服务端标题
        // 服务端标题（--name/web rename）优先于 onFirstUserMessage 派生的标题
        _ = _owner.FetchSessionTitleAsync(work.SessionId, config);

        // 容量唤醒: 新会话启动后通知容量变化
        _owner._deps.CapacityWake?.WakeUp();
    }
}