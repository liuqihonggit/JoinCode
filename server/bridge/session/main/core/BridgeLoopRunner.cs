namespace Core.Bridge;

/// <summary>
/// Bridge 主循环轮询器 — 对齐 TS 端 runBridgeLoop。
/// 从 BridgeMain 提取为单一职责类型。
/// </summary>
internal sealed class BridgeLoopRunner
{
    private readonly BridgeMain _owner;

    internal BridgeLoopRunner(BridgeMain owner)
        => _owner = owner;

    /// <summary>
    /// 核心轮询循环 — 对齐 TS 端 runBridgeLoop
    /// 注册环境 → 轮询工作 → 确认工作 → 生成子进程 → 管理生命周期
    /// </summary>
    internal async Task RunBridgeLoopAsync(
        BridgeConfig config, string? initialSessionId, CancellationToken ct)
    {
        _owner._logger?.LogInformation("BridgeMain: entering runBridgeLoop, maxSessions={MaxSessions}, spawnMode={SpawnMode}",
            config.MaxSessions, config.SpawnMode);

        _owner._loopStartTime = _owner._clock.GetUtcNow();

        // 如果有初始会话 ID，先恢复 — 对齐 TS 端: reconnectSession
        if (initialSessionId is not null && _owner.EnvironmentId is not null)
        {
            try
            {
                await _owner._deps.ApiClient.ReconnectSessionAsync(
                    _owner.EnvironmentId, initialSessionId, ct).ConfigureAwait(false);
                _owner._logger?.LogInformation("BridgeMain: reconnected session {SessionId}", initialSessionId);
            }
            catch (Exception ex)
            {
                _owner._logger?.LogWarning(ex, "BridgeMain: reconnect failed for {SessionId}", initialSessionId);
            }
        }

        // 主轮询循环
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 轮询工作 — 对齐 TS 端: api.pollForWork(envId, envSecret, signal, pollConfig.reclaim_older_than_ms)
                var reclaimMs = _owner._deps.PollConfig?.ReclaimOlderThanMs ?? 5000;
                var work = await _owner._deps.ApiClient.PollForWorkAsync(
                    _owner.GetEnvironmentId(), ct, reclaimMs).ConfigureAwait(false);

                // 重置退避状态（成功通信）
                _owner._backoff.Reset(onReconnected: ms => _owner._logger?.LogInformation("BridgeMain: reconnected after {Ms}ms", ms));

                if (work is null)
                {
                    // 无工作: 根据容量状态选择休眠策略
                    await HandleNoWorkAsync(config, ct).ConfigureAwait(false);
                    continue;
                }

                // 有工作: 处理工作项
                await _owner.HandleWorkAsync(config, work, ct).ConfigureAwait(false);
            }
            catch (BridgeFatalError ex)
            {
                // 致命错误: 401/403/404/410 — 对齐 TS 端: 分层判断 + fatalExit 标记
                _owner._fatalExit = true;
                // 对齐 TS 端: logEvent("tengu_bridge_fatal_error", {status, error_type})
                _owner.TelemetryCount("tengu_bridge_fatal_error", new Dictionary<string, string>
                {
                    ["status"] = ex.StatusCode?.ToString() ?? "0",
                    ["error_type"] = ex.ErrorType ?? "unknown",
                });
                if (BridgeApiClient.IsExpiredErrorType(ex.ErrorType))
                {
                    // 过期类错误 → 信息性状态消息（非错误样式）
                    _owner._logger?.LogWarning("BridgeMain: registration expired: {Message}", ex.Message);
                }
                else if (BridgeApiClient.IsSuppressible403(ex))
                {
                    // 可抑制 403 → 仅调试日志（装饰性权限不足）
                    _owner._logger?.LogDebug("BridgeMain: suppressed 403 error: {Message}", ex.Message);
                }
                else
                {
                    // 其他致命错误 → 错误日志
                    _owner._logger?.LogError(ex, "BridgeMain: fatal error, exiting loop");
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
                var shouldContinue = await _owner._backoff.HandleErrorAsync(
                    ex, onFatalExit: () => _owner._fatalExit = true, ct).ConfigureAwait(false);
                if (!shouldContinue)
                {
                    _owner._logger?.LogError(ex, "BridgeMain: giving up after too many errors");
                    // 对齐 TS 端: logEvent("tengu_bridge_poll_give_up", {error_type, elapsed_ms})
                    var errorType = ex is HttpRequestException or System.Net.Sockets.SocketException ? "connection" : "general";
                    var elapsedMs = _owner._backoff.IsInErrorState
                        ? (long)(_owner._clock.GetUtcNow() - _owner._backoff.FirstErrorTime).TotalMilliseconds : 0;
                    _owner.TelemetryCount("tengu_bridge_poll_give_up", new Dictionary<string, string>
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
        _owner.TelemetryCount("tengu_bridge_shutdown", new Dictionary<string, string>
        {
            ["active_sessions"] = _owner._tracker.Sessions.Count.ToString(),
            ["loop_duration_ms"] = _owner._loopStartTime != default
                ? ((long)(_owner._clock.GetUtcNow() - _owner._loopStartTime).TotalMilliseconds).ToString()
                : "0",
        });

        await _owner.CleanupAllSessionsAsync(config, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 处理无工作状态 — 对齐 TS 端 at-capacity / partial-capacity 休眠策略
    /// </summary>
    private async Task HandleNoWorkAsync(BridgeConfig config, CancellationToken ct)
    {
        var atCapacity = _owner._tracker.Sessions.Count >= config.MaxSessions;

        if (atCapacity)
        {
            // at-capacity: 心跳保活 + 等待容量释放 — 对齐 TS 端: heartbeatActiveWorkItems + capacityWake
            await RunAtCapacityHeartbeatAsync(config, ct).ConfigureAwait(false);
        }
        else
        {
            // 部分容量或空闲: 按配置间隔休眠 — 对齐 TS 端: sleep(pollInterval)
            var pollInterval = _owner._deps.PollConfig?.PollIntervalMs ?? 5000;
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
        var heartbeatIntervalMs = _owner._deps.PollConfig?.HeartbeatIntervalMs ?? 30000;
        var nonExclusiveIntervalMs = _owner._deps.PollConfig?.NonExclusiveHeartbeatIntervalMs ?? 0;

        // P3-2: 心跳循环模式 — 对齐 TS 端 at-capacity 心跳循环
        // 当 non_exclusive_heartbeat_interval_ms > 0 时，在 at-capacity 期间循环发送心跳
        var useHeartbeatLoop = nonExclusiveIntervalMs > 0;
        var pollDeadlineMs = _owner._deps.PollConfig?.AtCapacityPollIntervalMs ?? 0;

        while (!ct.IsCancellationRequested)
        {
            // 对所有活跃工作项发送心跳 — 对齐 TS 端: heartbeatWork(environmentId, workId, ingressToken)
            foreach (var (sessionId, state) in _owner._tracker.Sessions.GetAllStates())
            {
                var workId = state.WorkId;
                var ingressToken = state.IngressToken;
                if (ingressToken is not null)
                {
                    try
                    {
                        await _owner._deps.ApiClient.HeartbeatWorkAsync(
                            _owner.GetEnvironmentId(), workId, ingressToken, ct).ConfigureAwait(false);
                    }
                    catch (BridgeFatalError ex) when (ex.StatusCode == 401 || ex.StatusCode == 403)
                    {
                        // P3-7: 对齐 TS 端 — heartbeat 401/403 → reconnectSession
                        _owner._logger?.LogDebug("BridgeMain: heartbeat auth_failed ({Status}) for {SessionId}, attempting reconnect",
                            ex.StatusCode, sessionId);
                        try
                        {
                            await _owner._deps.ApiClient.ReconnectSessionAsync(
                                _owner.GetEnvironmentId(), sessionId, ct).ConfigureAwait(false);
                        }
                        catch (Exception reconnectEx)
                        {
                            _owner._logger?.LogDebug(reconnectEx, "BridgeMain: reconnect failed for {SessionId} (non-fatal)", sessionId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _owner._logger?.LogDebug(ex, "BridgeMain: heartbeat failed for {SessionId}", sessionId);
                    }
                }
            }

            // 非循环模式: 只做一次心跳+等待，然后返回让主循环继续轮询
            if (!useHeartbeatLoop)
            {
                // 等待容量释放或超时 — 对齐 TS 端: capacityWake.signal() + sleep
                if (_owner._deps.CapacityWake is not null)
                {
                    await _owner._deps.CapacityWake.SleepUntilCapacityWakesAsync(
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
            if (_owner._deps.CapacityWake is not null)
            {
                await _owner._deps.CapacityWake.SleepUntilCapacityWakesAsync(
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
}
