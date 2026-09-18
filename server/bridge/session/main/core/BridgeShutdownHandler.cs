namespace Core.Bridge;

/// <summary>
/// Bridge 优雅关闭处理器 — 对齐 TS 端 SIGINT/SIGTERM 处理。
/// 从 BridgeMain 提取为单一职责类型，通过 BridgeMain 共享状态。
/// </summary>
internal sealed class BridgeShutdownHandler
{
    private readonly BridgeMain _owner;

    internal BridgeShutdownHandler(BridgeMain owner)
        => _owner = owner;

    /// <summary>
    /// 请求优雅关闭 — 对齐 TS 端 SIGINT/SIGTERM 处理
    /// </summary>
    internal async Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref _owner._isShuttingDown, 1) != 0)
        {
            return; // 防重入
        }

        if (_owner._shutdownPipeline is not null)
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
            IsResuming = _owner._isResuming,
            FatalExit = _owner._fatalExit,
            EnvironmentId = _owner.EnvironmentId,
            SpawnMode = _owner._deps.Config.SpawnMode,
            ResumePointerDir = _owner._pointerManager.ResumePointerDir,
            Tracker = _owner._tracker,
            Spawner = _owner._deps.Spawner,
            ApiClient = _owner._deps.ApiClient,
            PointerService = _owner._deps.PointerService,
            WorkingDirectory = _owner._deps.WorkingDirectory,
            ArchiveSession = _owner._deps.ArchiveSession,
            UnregisterKeyboardListener = () => _owner._deps.UnregisterKeyboardListener?.Invoke(),
            LoopCts = _owner._loopCts,
            LoopTask = _owner._loopTask,
            PointerRefreshTimer = _owner._pointerManager.RefreshTimer,
        };

        var pipeline = _owner._shutdownPipeline;
        if (pipeline is not null)
        {
            await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(false);
        }

        _owner._pointerManager.StopRefreshTimer();
    }

    private async Task ShutdownDirectAsync()
    {
        if (Interlocked.Exchange(ref _owner._isShuttingDown, 1) != 0)
        {
            return; // 防重入
        }

        _owner._logger?.LogInformation("BridgeMain: shutting down...");

        // 注销键盘监听 — 对齐 TS 端: process.stdin.setRawMode(false)
        _owner._deps.UnregisterKeyboardListener?.Invoke();

        // 取消主循环
        await (_owner._loopCts?.CancelAsync() ?? Task.CompletedTask).ConfigureAwait(false);

        // 等待主循环退出
        if (_owner._loopTask is not null)
        {
            try
            {
                await _owner._loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        // 优雅关闭所有子进程 — 对齐 TS 端 shutdownGraceMs
        var handles = _owner._tracker.Sessions.GetAllHandles().ToList();
        if (handles.Count > 0)
        {
            await _owner._deps.Spawner.ShutdownAllAsync(handles).ConfigureAwait(false);
        }

        // 归档所有已知会话 — 对齐 TS 端: archiveSession(compatId)
        // resume 模式下跳过归档，保留会话供下次 --continue 恢复
        if (!_owner._isResuming && _owner._deps.ArchiveSession is not null)
        {
            var sessionsToArchive = _owner._tracker.Sessions.GetAllCompatIds().ToList();
            if (sessionsToArchive.Count > 0)
            {
                _owner._logger?.LogInformation("BridgeMain: archiving {Count} session(s)", sessionsToArchive.Count);
                foreach (var kvp in sessionsToArchive)
                {
                    try
                    {
                        await _owner._deps.ArchiveSession(kvp.Value, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _owner._logger?.LogDebug(ex, "BridgeMain: archive failed for {SessionId} (non-fatal)", kvp.Value);
                    }
                }
            }

        }
        else if (_owner._isResuming && !_owner._fatalExit)
        {
            _owner._logger?.LogInformation($"Resume this session by running `{BrandConstants.CliCommandName} remote-control --continue`");
            _owner._logger?.LogDebug("BridgeMain: skipping archive+deregister to allow resume");
        }

        // 注销环境
        // 对齐 TS 端: resumable shutdown — resume 模式下跳过注销，保留环境供下次恢复
        if (!_owner._isResuming && _owner.EnvironmentId is not null)
        {
            try
            {
                await _owner._deps.ApiClient.DeregisterEnvironmentAsync(
                    _owner.EnvironmentId, CancellationToken.None).ConfigureAwait(false);
                _owner._logger?.LogInformation("BridgeMain: environment deregistered");
            }
            catch (Exception ex)
            {
                _owner._logger?.LogWarning(ex, "BridgeMain: deregister failed (non-fatal)");
            }
        }

        // 清除崩溃恢复指针 + 停止刷新定时器
        // 对齐 TS 端: resumable shutdown — resume 模式下保留指针文件供下次 --continue
        await _owner._pointerManager.ClearPointerAsync(_owner._isResuming, _owner._deps.Config.SpawnMode, _owner._deps.WorkingDirectory).ConfigureAwait(false);
        _owner._pointerManager.StopRefreshTimer();

        _owner._logger?.LogInformation("BridgeMain: shutdown complete");
    }
}
