
namespace Core.Bridge;

/// <summary>
/// 会话监控职责类 — 从 BridgeMain 提取
/// 包含: 会话完成监控/会话超时监控/全量会话清理
/// 对齐 TS 端: onSessionDone / onSessionTimeout / 优雅关闭流程
/// </summary>
internal sealed class BridgeSessionMonitor {
    private readonly BridgeMain _owner;

    /// <summary>
    /// 构造会话监控器
    /// </summary>
    /// <param name="owner">宿主 BridgeMain 实例</param>
    internal BridgeSessionMonitor(BridgeMain owner) {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <summary>
    /// 监控会话完成 — 对齐 TS 端: onSessionDone 回调
    /// </summary>
    internal async Task MonitorSessionCompletionAsync(
        BridgeConfig config, BridgeWorkItem work, BridgeSubprocessHandle handle, CancellationToken ct) {
        BridgeSubprocessStatus rawStatus;
        try {
            rawStatus = await handle.Done.ConfigureAwait(false);
        } catch (Exception ex) {
            rawStatus = BridgeSubprocessStatus.Failed;
            _owner._logger?.LogWarning(ex, "BridgeMain: session {SessionId} failed with exception", work.SessionId);
        }

        // wasTimedOut 检测 — 对齐 TS 端: timedOutSessions.delete(sessionId)
        // 如果会话被超时看门狗杀掉，interrupted 状态修正为 failed
        var wasTimedOut = _owner._tracker.Sessions.RemoveTimedOut(work.SessionId);
        var status = wasTimedOut && rawStatus == BridgeSubprocessStatus.Interrupted
            ? BridgeSubprocessStatus.Failed
            : rawStatus;

        var compatId = _owner.GetCompatId(work.SessionId);
        var durationMs = _owner._tracker.Sessions.GetDurationMs(work.SessionId, _owner._clock);

        _owner._logger?.LogInformation("BridgeMain: session {SessionId} done, status={Status}, duration={Duration}ms",
            work.SessionId, status, durationMs);

        // 对齐 TS 端: logEvent("tengu_bridge_session_done", {status, duration_ms})
        _owner.TelemetryCount("tengu_bridge_session_done", new Dictionary<string, string> {
            ["status"] = status.ToString().ToLowerInvariant(),
            ["duration_ms"] = durationMs.ToString(),
        });

        // 清除状态显示 — 对齐 TS 端: logger.clearStatus()
        _owner._deps.BridgeLogger?.ClearStatus();

        // 按状态调用 logger — 对齐 TS 端: switch(status)
        switch (status) {
            case BridgeSubprocessStatus.Completed:
            _owner._logger?.LogInformation("BridgeMain: session {SessionId} completed ({DurationMs}ms)", compatId, durationMs);
            break;
            case BridgeSubprocessStatus.Failed:
            // 超时杀掉的会话已由 onSessionTimeout 记录过日志，关机中断也跳过
            if (!wasTimedOut && !_owner._loopCts?.IsCancellationRequested != true) {
                var stderrSummary = handle.StderrLines.Count() > 0
                    ? string.Join("\n", handle.StderrLines)
                    : null;
                var failureMessage = stderrSummary ?? "Process exited with error";
                _owner._logger?.LogError("BridgeMain: session {SessionId} failed: {Error}", compatId, failureMessage);
            }
            break;
            case BridgeSubprocessStatus.Interrupted:
            _owner._logger?.LogDebug("BridgeMain: session {SessionId} interrupted", compatId);
            break;
        }

        // 清理跟踪
        _owner.CleanupSessionTracking(work.SessionId);

        // 清理 worktree — 对齐 TS 端: cleanupWorktree
        if (_owner._tracker.Sessions.RemoveWorktree(work.SessionId, out var worktreePath) &&
            _owner._deps.WorktreeService is not null) {
            try {
                await _owner._deps.WorktreeService.RemoveAgentWorktreeAsync(
                    work.SessionId, force: false, cancellationToken: ct).ConfigureAwait(false);
                _owner._logger?.LogInformation("BridgeMain: cleaned up worktree for session {SessionId}", work.SessionId);
            } catch (Exception ex) {
                _owner._logger?.LogDebug(ex, "BridgeMain: worktree cleanup failed for {SessionId} (non-fatal)", work.SessionId);
            }
        }

        // 停止工作项 — 对齐 TS 端: interrupted 状态跳过 stopWork（服务端已知道或 shutdown 会单独调用）
        // 非 interrupted 状态才 stopWork + completedWorkIds
        if (status != BridgeSubprocessStatus.Interrupted) {
            await _owner._workApi.StopWorkWithRetryAsync(_owner.EnvironmentId, work.WorkId, ct).ConfigureAwait(false);
            _owner._tracker.WorkCompletion.Mark(work.WorkId);
        }

        // 归档会话 — 对齐 TS 端: archiveSession(compatId)
        // 对齐 TS 端: resumable shutdown — resume 模式下跳过归档，保留指针文件
        // 对齐 TS 端: interrupted 状态跳过归档
        if (status != BridgeSubprocessStatus.Interrupted && !_owner._isResuming && _owner._deps.ArchiveSession is not null) {
            try {
                var archiveId = _owner.GetCompatId(work.SessionId);
                await _owner._deps.ArchiveSession(archiveId, ct).ConfigureAwait(false);
            } catch (Exception ex) {
                _owner._logger?.LogDebug(ex, "BridgeMain: archive failed for {SessionId} (non-fatal)", work.SessionId);
            }
        }

        // 取消 token 刷新
        _owner._tokenRefresh?.Cancel(work.SessionId);

        // 容量唤醒: 会话完成后通知容量变化
        _owner._deps.CapacityWake?.WakeUp();

        // 单会话模式: 非 interrupted 状态且非关机时退出循环 — 对齐 TS 端
        if (status != BridgeSubprocessStatus.Interrupted && _owner._loopCts is { IsCancellationRequested: false }
            && config.SpawnMode == BridgeSpawnMode.SingleSession) {
            _owner._logger?.LogInformation("BridgeMain: single-session mode, session done — exiting loop");
            await _owner._loopCts.CancelAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 监控会话超时 — 对齐 TS 端: onSessionTimeout
    /// </summary>
    internal async Task MonitorSessionTimeoutAsync(
        BridgeConfig config, BridgeWorkItem work, BridgeSubprocessHandle handle,
        int timeoutMs, CancellationToken ct) {
        try {
            await Task.Delay(timeoutMs, ct).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            return;
        }

        // 超时: 终止子进程
        if (handle.IsRunning && !_owner._tracker.Sessions.IsTimedOut(work.SessionId)) {
            _owner._tracker.Sessions.MarkTimedOut(work.SessionId);
            var compatId = _owner.GetCompatId(work.SessionId);
            var timeoutMsg = $"Session timed out after {timeoutMs}ms";
            _owner._logger?.LogWarning("BridgeMain: session {SessionId} timed out after {TimeoutMs}ms",
                work.SessionId, timeoutMs);
            // 对齐 TS 端: logEvent("tengu_bridge_session_timeout", {timeout_ms})
            _owner.TelemetryCount("tengu_bridge_session_timeout", new Dictionary<string, string> {
                ["timeout_ms"] = timeoutMs.ToString(),
            });
            handle.Kill();
        }
    }

    /// <summary>
    /// 清理所有会话 — 对齐 TS 端: 优雅关闭流程
    /// </summary>
    internal async Task CleanupAllSessionsAsync(BridgeConfig config, CancellationToken ct) {
        if (_owner._tracker.Sessions.Count == 0) return;

        _owner._logger?.LogInformation("BridgeMain: cleaning up {Count} sessions", _owner._tracker.Sessions.Count);

        // 1. SIGTERM 所有活跃子进程
        var handles = _owner._tracker.Sessions.GetAllHandles().ToList();
        await _owner._deps.Spawner.ShutdownAllAsync(handles, ct).ConfigureAwait(false);

        // 2. 停止所有工作项
        var workIds = _owner._tracker.Sessions.GetAllWorkIds().ToList();
        foreach (var workId in workIds) {
            await _owner._workApi.SafeStopWorkAsync(_owner.EnvironmentId, workId, ct).ConfigureAwait(false);
        }

        // 3. 归档所有会话 — 对齐 TS 端: archiveSession(compatId)
        if (_owner._deps.ArchiveSession is not null) {
            var sessionIds = _owner._tracker.Sessions.GetAllSessionIds().ToList();
            foreach (var sessionId in sessionIds) {
                try {
                    var archiveId = _owner.GetCompatId(sessionId);
                    await _owner._deps.ArchiveSession(archiveId, ct).ConfigureAwait(false);
                } catch (Exception ex) {
                    _owner._logger?.LogDebug(ex, "BridgeMain: archive failed for {SessionId} (non-fatal)", sessionId);
                }
            }
        }

        // 4. 清理 worktree — 对齐 TS 端: 清理所有会话的 worktree
        var worktreeSessionIds = _owner._tracker.Sessions.GetAllWorktreeSessionIds().ToList();
        if (worktreeSessionIds.Count > 0 && _owner._deps.WorktreeService is not null) {
            foreach (var sessionId in worktreeSessionIds) {
                try {
                    await _owner._deps.WorktreeService.RemoveAgentWorktreeAsync(
                        sessionId, force: true, cancellationToken: ct).ConfigureAwait(false);
                } catch (Exception ex) {
                    _owner._logger?.LogDebug(ex, "BridgeMain: worktree cleanup failed for {SessionId} (non-fatal)", sessionId);
                }
            }
        }

        // 5. 清理跟踪
        _owner._tracker.ClearAll();

        // 6. 等待待清理任务 — 对齐 TS 端: pendingCleanups
        if (_owner._pendingCleanups.Count > 0) {
            try {
                using var guard = await _owner._cleanupLock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_owner._cleanupLock.Name}' 等待超时");
                var cleanups = _owner._pendingCleanups.ToArray();
                await Task.WhenAll(cleanups).WaitAsync(
                    TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            } catch (Exception ex) {
                _owner._logger?.LogDebug(ex, "BridgeMain: pending cleanups timeout (non-fatal)");
            }
        }
    }
}