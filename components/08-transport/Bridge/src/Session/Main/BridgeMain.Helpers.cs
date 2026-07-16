
namespace Core.Bridge;

/// <summary>
/// BridgeMain 辅助方法 — partial class 分文件
/// 包含: ID转换/URL构建/状态显示/清理/ACK/指针/配置/键盘/标题
/// 退避逻辑已迁移到 BridgeBackoffStrategy
/// </summary>
public sealed partial class BridgeMain
{
    /// <summary>
    /// 获取兼容 ID — 对齐 TS 端 sessionCompatIds.get(sessionId) ?? sessionId
    /// cse_* → session_* 转换，用于 logger/archive/title 等客户端兼容 API
    /// </summary>
    private string GetCompatId(string sessionId) => _tracker.GetCompatId(sessionId);

    /// <summary>
    /// 从 gitRepoUrl 提取仓库名 — 对齐 TS 端 parseGitHubRepository + basename 回退
    /// </summary>
    private static string ExtractRepoName(string? gitRepoUrl, string workingDirectory)
    {
        if (gitRepoUrl is not null)
        {
            // 尝试从 GitHub URL 提取 owner/repo
            var lastSlash = gitRepoUrl.LastIndexOf('/');
            var lastDot = gitRepoUrl.LastIndexOf('.');
            if (lastSlash >= 0)
            {
                var repo = lastDot > lastSlash
                    ? gitRepoUrl.Substring(lastSlash + 1, lastDot - lastSlash - 1)
                    : gitRepoUrl.Substring(lastSlash + 1);
                if (repo.Length > 0) return repo;
            }
        }

        // 回退到工作目录名
        try
        {
            return Path.GetFileName(workingDirectory) ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>
    /// 构建远程会话 URL — 对齐 TS 端 getRemoteSessionUrl
    /// </summary>
    private static string BuildRemoteSessionUrl(string sessionId, BridgeConfig config)
    {
        var baseUrl = config.ApiBaseUrl ?? "https://claude.ai";
        var trimmed = baseUrl.TrimEnd('/');
        return $"{trimmed}/remote-control/{sessionId}";
    }

    /// <summary>
    /// 更新状态显示 — 对齐 TS 端 updateStatusDisplay
    /// 每秒推送会话计数、每个会话的耗时/活动/工具轨迹到 logger
    /// </summary>
    private void UpdateStatusDisplay(BridgeConfig config)
    {
        try
        {
            if (_deps.BridgeLogger is null) return;

            // 推送会话计数
            _deps.BridgeLogger.UpdateSessionCount(_tracker.ActiveSessions.Count, config.MaxSessions, config.SpawnMode);

            // 推送每个会话的状态
            if (_tracker.ActiveSessions.Count == 0)
            {
                _deps.BridgeLogger.UpdateIdleStatus();
                return;
            }

            // 对齐 TS 端: 只显示最近一个会话的详细状态
            var lastSession = _tracker.ActiveSessions.Last();
            var sessionId = lastSession.Key;
            var compatId = GetCompatId(sessionId);

            if (_tracker.SessionStartTimes.TryGetValue(sessionId, out var startTime))
            {
                var elapsed = (_clock.GetUtcNow() - startTime).ToString(@"hh\:mm\:ss");
                // 使用默认活动状态 — 实际活动由 OnActivity 回调驱动
                _deps.BridgeLogger.UpdateSessionStatus(compatId, elapsed,
                    BridgeSessionActivity.Idle, Array.Empty<string>());
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "BridgeMain: status display update failed (non-fatal)");
        }
    }

    private void CleanupSessionTracking(string sessionId)
    {
        _tracker.CleanupSession(sessionId, onRemoveCompatId: compatId => _deps.BridgeLogger?.RemoveSession(compatId));
    }

    /// <summary>
    /// ACK 工作项 — 对齐 TS 端 ackWork 闭包
    /// 使用 session_ingress_token 作为 Bearer 认证
    /// </summary>
    private async Task AckWorkAsync(string workId, string sessionToken, CancellationToken ct)
    {
        try
        {
            await _deps.ApiClient.AcknowledgeWorkAsync(
                EnvironmentId ?? throw new InvalidOperationException("EnvironmentId not set"), workId, sessionToken, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "BridgeMain: ACK failed for work {WorkId}", workId);
        }
    }

    /// <summary>
    /// 带重试的停止工作 — 对齐 TS 端 stopWorkWithRetry
    /// 最多重试 3 次，指数退避
    /// </summary>
    private async Task StopWorkWithRetryAsync(string workId, CancellationToken ct, int baseDelayMs = 1000)
    {
        if (EnvironmentId is null) return;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await _deps.ApiClient.StopWorkAsync(EnvironmentId, workId, ct).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                if (attempt < 2)
                {
                    var delayMs = baseDelayMs * (1 << attempt);
                    _logger?.LogDebug(ex,
                        "BridgeMain: stopWork attempt {Attempt} failed for {WorkId}, retrying in {Delay}ms",
                        attempt + 1, workId, delayMs);
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                }
                else
                {
                    _logger?.LogDebug(ex, "BridgeMain: stopWork failed for {WorkId} after 3 attempts (non-fatal)", workId);
                }
            }
        }
    }

    /// <summary>
    /// 跟踪待清理任务 — 对齐 TS 端 trackCleanup
    /// 后台执行不阻塞主循环，定期清理已完成的任务
    /// </summary>
    private void TrackCleanup(Task cleanupTask)
    {
        _cleanupLock.Wait();
        try
        {
            _pendingCleanups.Add(cleanupTask);
        }
        finally
        {
            _cleanupLock.Release();
        }

        // 清理已完成的任务
        _ = cleanupTask.ContinueWith(_ =>
        {
            _cleanupLock.Wait();
            try
            {
                _pendingCleanups.Remove(cleanupTask);
            }
            finally
            {
                _cleanupLock.Release();
            }
        }, TaskScheduler.Default);
    }

    private async Task SafeStopWorkAsync(string workId, CancellationToken ct)
    {
        if (EnvironmentId is null) return;
        try
        {
            await _deps.ApiClient.StopWorkAsync(EnvironmentId, workId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "BridgeMain: stopWork failed for {WorkId} (non-fatal)", workId);
        }
    }

    private async Task WritePointerAsync(BridgeConfig config, string sessionId)
    {
        try
        {
            var pointer = new BridgePointer
            {
                SessionId = sessionId,
                EnvironmentId = EnvironmentId ?? "",
                Source = BridgePointerSource.Standalone.ToValue(),
            };
            await _deps.PointerService.WriteAsync(config.Dir, pointer).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "BridgeMain: pointer write failed (non-fatal)");
        }
    }

    /// <summary>
    /// 启动崩溃恢复指针刷新定时器 — 对齐 TS 端: 每小时刷新 mtime
    /// </summary>
    private void StartPointerRefreshTimer(BridgeConfig config, string sessionId)
    {
        _pointerRefreshTimer?.Dispose();
        _pointerRefreshTimer = new Timer(async _ =>
        {
            await WritePointerAsync(config, sessionId).ConfigureAwait(false);
        }, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
    }

    /// <summary>
    /// 确定子进程工作目录 — 对齐 TS 端: worktree/same-dir/session 模式
    /// </summary>
    private string DetermineSpawnDir(BridgeConfig config, BridgeWorkItem work)
    {
        return config.SpawnMode switch
        {
            BridgeSpawnMode.Worktree => _deps.WorktreeDir ?? config.Dir,
            BridgeSpawnMode.SameDir => config.Dir,
            _ => config.Dir // SingleSession 也使用 config.Dir
        };
    }

    /// <summary>
    /// 构建 BridgeConfig — 对齐 TS 端 config 构建
    /// </summary>
    internal BridgeConfig BuildConfig(BridgeMainArgs args, string baseUrl, string? reuseEnvironmentId,
        BridgeSpawnMode? dialogChosenSpawnMode = null, bool isResuming = false,
        BridgeSpawnModeSource spawnModeSource = BridgeSpawnModeSource.GateDefault)
    {
        var spawnMode = dialogChosenSpawnMode ?? args.SpawnMode ?? _deps.DefaultSpawnMode ?? BridgeSpawnMode.SingleSession;

        // 对齐 TS 端: resume forces single-session mode
        // --continue 或 --session-id 时，强制单会话模式，确保恢复的会话独占环境
        if (isResuming && spawnMode != BridgeSpawnMode.SingleSession)
        {
            _logger?.LogInformation("BridgeMain: resume mode — forcing single-session mode (was {Mode})", spawnMode.ToValue());
            spawnMode = BridgeSpawnMode.SingleSession;
        }

        var maxSessions = args.Capacity ?? (spawnMode == BridgeSpawnMode.SingleSession ? 1 : 5);

        // 对齐 TS 端: sessionIngressUrl — ant 开发环境下可能与 baseUrl 不同
        // USER_TYPE=ant 且 CLAUDE_BRIDGE_SESSION_INGRESS_URL 环境变量存在时使用环境变量值
        // 生产环境下 sessionIngressUrl 与 baseUrl 相同（Envoy 路由 /v1/session_ingress/*）
        var sessionIngressUrl = baseUrl;
        var userType = Environment.GetEnvironmentVariable("USER_TYPE");
        var ingressOverride = Environment.GetEnvironmentVariable("CLAUDE_BRIDGE_SESSION_INGRESS_URL");
        if (string.Equals(userType, "ant", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(ingressOverride))
        {
            sessionIngressUrl = ingressOverride;
        }

        return new BridgeConfig
        {
            Dir = _deps.WorkingDirectory,
            MachineName = Environment.MachineName,
            Branch = _deps.GitBranch ?? "main",
            GitRepoUrl = _deps.GitRepoUrl,
            MaxSessions = maxSessions,
            SpawnMode = spawnMode,
            SpawnModeSource = spawnModeSource,
            Verbose = args.Verbose,
            Sandbox = args.Sandbox,
            BridgeId = Guid.NewGuid().ToString(),
            WorkerType = "bridge",
            ApiBaseUrl = baseUrl,
            SessionIngressUrl = sessionIngressUrl,
            DebugFile = args.DebugFile,
            SessionTimeoutMs = args.SessionTimeoutMs ?? 0,
            ReuseEnvironmentId = reuseEnvironmentId,
        };
    }

    /// <summary>
    /// 键盘输入处理器 — 对齐 TS 端 onStdinData
    /// Space(0x20)=切换QR, w(0x77)=切换spawnMode, Ctrl+C(0x03)/Ctrl+D(0x04)=优雅关闭
    /// </summary>
    private async Task OnKeyboardInputAsync(byte key)
    {
        switch (key)
        {
            case 0x03: // Ctrl+C
            case 0x04: // Ctrl+D
                // P3-10: 对齐 TS 端 — abort controller 让循环自然退出，而非直接调用 ShutdownAsync
                // TS 端 SIGINT 只是 controller.abort()，循环退出后走统一的清理路径（包括 resumable shutdown 判断）
                _logger?.LogInformation("BridgeMain: keyboard shutdown requested (key=0x{Key:X2})", key);
                _loopCts?.Cancel();
                break;

            case 0x20: // Space — 切换 QR 码显示
                _deps.BridgeLogger?.ToggleQr();
                break;

            case 0x77: // 'w' — 切换 spawnMode
                ToggleSpawnMode();
                break;
        }
    }

    /// <summary>
    /// 切换 spawn 模式 — 对齐 TS 端 w 键切换逻辑
    /// same-dir ↔ worktree 互切，保存偏好到项目配置
    /// </summary>
    private void ToggleSpawnMode()
    {
        // 对齐 TS 端: toggleAvailable 检查 — worktree 不可用时不能切换
        if (_deps.IsWorktreeAvailable?.Invoke() != true)
        {
            return;
        }

        var currentMode = _deps.Config.SpawnMode;
        var newMode = currentMode == BridgeSpawnMode.SameDir
            ? BridgeSpawnMode.Worktree
            : BridgeSpawnMode.SameDir;

        _deps.Config.SpawnMode = newMode;

        // 保存偏好 — 对齐 TS 端: saveCurrentProjectConfig
        _deps.SaveSpawnModePreference?.Invoke(newMode);

        _deps.BridgeLogger?.SetSpawnModeDisplay(newMode);

        _logger?.LogInformation("BridgeMain: spawn mode toggled to {Mode}", newMode.ToValue());
    }

    /// <summary>
    /// 首条用户消息回调 — 对齐 TS 端 onFirstUserMessage
    /// 服务端标题（--name, web rename）优先: 如果 fetchSessionTitle 已标记 titledSessions 则跳过
    /// 否则派生标题 + 更新服务端 + 标记 titledSessions
    /// </summary>
    private void OnFirstUserMessage(string sessionId, string text, BridgeConfig config)
    {
        // 对齐 TS 端: if (titledSessions.has(compatSessionId)) return
        var compatId = GetCompatId(sessionId);
        if (_tracker.TitledSessions.Contains(compatId)) return;

        _tracker.TitledSessions.Add(compatId);
        var title = DeriveSessionTitle(text);
        _deps.BridgeLogger?.SetSessionTitle(compatId, title);
        _logger?.LogDebug("BridgeMain: derived title for {SessionId}: {Title}", sessionId, title);

        // 对齐 TS 端: updateBridgeSessionTitle — 异步更新服务端标题（best-effort）
        if (_deps.UpdateSessionTitle is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _deps.UpdateSessionTitle(sessionId, title, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "BridgeMain: failed to update title for {SessionId} (non-fatal)", sessionId);
                }
            }, CancellationToken.None);
        }
    }

    /// <summary>
    /// 异步获取服务端会话标题 — 对齐 TS 端 fetchSessionTitle
    /// GET /v1/sessions/{id} → 提取 title → 设置本地显示 + 标记 titledSessions
    /// </summary>
    private async Task FetchSessionTitleAsync(string sessionId, BridgeConfig config)
    {
        try
        {
            string? title = null;

            // 优先使用 deps 回调（允许外部注入自定义获取逻辑）
            if (_deps.FetchSessionTitle is not null)
            {
                title = await _deps.FetchSessionTitle(sessionId, CancellationToken.None).ConfigureAwait(false);
            }
            else if (_deps.ApiClient is not null)
            {
                title = await _deps.ApiClient.GetSessionTitleAsync(
                    sessionId, CancellationToken.None).ConfigureAwait(false);
            }

            if (title is not null && _tracker.ActiveSessions.ContainsKey(sessionId))
            {
                var compatId = GetCompatId(sessionId);
                _tracker.TitledSessions.Add(compatId);
                _deps.BridgeLogger?.SetSessionTitle(compatId, title);
                _logger?.LogDebug("BridgeMain: server title for {SessionId}: {Title}", sessionId, title);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "BridgeMain: failed to fetch title for {SessionId} (non-fatal)", sessionId);
        }
    }

    /// <summary>
    /// 从用户消息派生会话标题 — 对齐 TS 端 deriveSessionTitle
    /// 折叠空白（换行/制表符 → 空格），截断到 80 字符
    /// </summary>
    internal static string DeriveSessionTitle(string text)
    {
        // 对齐 TS 端: text.replace(/\s+/g, ' ').trim()
        var flat = WhitespaceRegex().Replace(text, " ").Trim();
        return flat.Length > TitleMaxLen ? flat[..TitleMaxLen] : flat;
    }

    private const int TitleMaxLen = 80; // 对齐 TS 端 TITLE_MAX_LEN

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        await ShutdownAsync().ConfigureAwait(false);

        _loopCts?.Dispose();
        _pointerRefreshTimer?.Dispose();
        _tokenRefresh?.DisposeAsync().GetAwaiter().GetResult(); // 对齐 TS 端: tokenRefresh.cancelAll()
        _cleanupLock.Dispose();
    }

    // ===== 遥测辅助方法 — 对齐 TS 端 logEvent(tengu_bridge_*) =====

    /// <summary>
    /// 记录遥测计数事件 — 对齐 TS 端 logEvent(eventName, metadata)
    /// </summary>
    private void TelemetryCount(string eventName, Dictionary<string, string>? tags = null)
    {
        _telemetry?.GetCounter(eventName)?.Add(1, tags);
    }

    /// <summary>
    /// 记录遥测直方图事件 — 对齐 TS 端 logEvent(eventName, {duration_ms: ...})
    /// </summary>
    private void TelemetryHistogram(string eventName, double value, Dictionary<string, string>? tags = null)
    {
        _telemetry?.GetHistogram(eventName)?.Record(value, tags);
    }
}
