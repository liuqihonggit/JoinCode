namespace Core.Bridge;

/// <summary>
/// Bridge 工作轮询循环 — 对齐 TS 端 startWorkPollLoop
/// 实现: 注册环境 → 轮询工作 → 确认工作 → 连接传输 → 心跳保活 → 拆卸清理
/// </summary>
public sealed class BridgeWorkPollLoop : IAsyncDisposable
{
    private readonly BridgeApiClient _apiClient;
    private readonly ILogger? _logger;
    private readonly BridgeWorkPollOptions _options;
    private readonly CapacityWakeService? _capacityWake;
    private readonly IClockService _clock;

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private volatile int _isRunning;
    private int _isDisposed;

    // 环境状态
    private string? _environmentId;
    private string? _environmentSecret;
    private string? _currentSessionId;
    private string? _currentWorkId;
    private string? _currentIngressToken; // 对齐 TS 端: sessionIngressTokens
    private IReplBridgeTransport? _currentTransport;

    // 去重集合 — 对齐 TS 端 BoundedUUIDSet(2000)
    private readonly BoundedUUIDSet _recentPostedUUIDs;
    private readonly BoundedUUIDSet _recentInboundUUIDs;

    // 错误追踪
    private int _consecutiveErrors;
    private int _environmentRecreations;
    private DateTime _lastErrorTime;
    private DateTime? _firstErrorTime;

    // 对齐 TS 端: POLL_ERROR_GIVE_UP_MS (15分钟)
    private static readonly TimeSpan PollErrorGiveUp = TimeSpan.FromMinutes(15);

    // 传输代次 — 对齐 TS 端 v2Generation，防止并发 handshake 竞态
    private int _transportGeneration;

    public bool IsRunning => _isRunning != 0;
    public string? CurrentEnvironmentId => _environmentId;
    public string? CurrentSessionId => _currentSessionId;
    public string? CurrentWorkId => _currentWorkId;
    public IReplBridgeTransport? CurrentTransport => _currentTransport;

    public event EventHandler<BridgeWorkReceivedEventArgs>? WorkReceived;
    public event EventHandler<BridgePollStateEventArgs>? StateChanged;
    public event EventHandler<BridgePollErrorEventArgs>? FatalError;

    /// <summary>
    /// 心跳致命错误事件 — 对齐 TS 端 onHeartbeatFatal
    /// JWT 过期 (401/403) 或工作项消失 (404/410) 时触发
    /// 调用方应关闭传输、清除工作状态、唤醒轮询循环
    /// </summary>
    public event EventHandler<BridgePollErrorEventArgs>? HeartbeatFatal;

    public BridgeWorkPollLoop(
        BridgeApiClient apiClient,
        BridgeWorkPollOptions? options = null,
        ILogger? logger = null,
        CapacityWakeService? capacityWake = null,
        IClockService? clock = null)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _options = options ?? new BridgeWorkPollOptions();
        _logger = logger;
        _capacityWake = capacityWake;
        _clock = clock ?? SystemClockService.Instance;
        _recentPostedUUIDs = new BoundedUUIDSet(_options.UuidDedupBufferSize);
        _recentInboundUUIDs = new BoundedUUIDSet(_options.UuidDedupBufferSize);
        _lastErrorTime = DateTime.MinValue;
    }

    /// <summary>
    /// 注册 Bridge 环境并启动轮询循环 — 对齐 TS 端 initBridgeCore
    /// </summary>
    public async Task<bool> StartAsync(
        BridgeEnvironmentRegistration registration,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (Interlocked.Exchange(ref _isRunning, 1) != 0)
        {
            _logger?.LogWarning("[BridgeWorkPollLoop] 已在运行");
            return false;
        }

        try
        {
            // 1. 注册 Bridge 环境
            var regResponse = await _apiClient.RegisterBridgeEnvironmentAsync(registration, ct).ConfigureAwait(false);
            if (regResponse is null)
            {
                _logger?.LogError("[BridgeWorkPollLoop] 注册环境失败");
                Interlocked.Exchange(ref _isRunning, 0);
                return false;
            }

            _environmentId = regResponse.EnvironmentId;
            _environmentSecret = regResponse.BridgeId; // TS 端: environment_secret
            _logger?.LogInformation("[BridgeWorkPollLoop] 环境已注册: {EnvId}", _environmentId);

            // 2. 启动轮询循环
            _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _loopTask = RunPollLoopAsync(_loopCts.Token);

            StateChanged?.Invoke(this, new BridgePollStateEventArgs("registered"));
            return true;
        }
        catch (BridgeFatalError ex)
        {
            _logger?.LogError(ex, "[BridgeWorkPollLoop] 注册环境致命错误: {Message}", ex.Message);
            Interlocked.Exchange(ref _isRunning, 0);
            FatalError?.Invoke(this, new BridgePollErrorEventArgs(ex, "registration_fatal"));
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[BridgeWorkPollLoop] 注册环境失败");
            Interlocked.Exchange(ref _isRunning, 0);
            return false;
        }
    }

    /// <summary>
    /// 使用已有环境凭证启动轮询循环 — 对齐 TS 端 initBridgeCore v1 路径
    /// 环境已在外部注册，直接使用 environmentId + environmentSecret 开始轮询
    /// </summary>
    public bool StartWithExistingEnvironment(
        string environmentId,
        string environmentSecret,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(environmentId);

        if (Interlocked.Exchange(ref _isRunning, 1) != 0)
        {
            _logger?.LogWarning("[BridgeWorkPollLoop] 已在运行");
            return false;
        }

        _environmentId = environmentId;
        _environmentSecret = environmentSecret;
        _logger?.LogInformation("[BridgeWorkPollLoop] 使用已有环境: {EnvId}", _environmentId);

        // 启动轮询循环
        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loopTask = RunPollLoopAsync(_loopCts.Token);

        StateChanged?.Invoke(this, new BridgePollStateEventArgs("registered"));
        return true;
    }

    /// <summary>
    /// 停止轮询循环并清理 — 对齐 TS 端 teardown 序列
    /// stopWork → archiveSession → close transport → deregisterEnvironment
    /// </summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _isRunning, 0) == 0)
        {
            return;
        }

        _logger?.LogInformation("[BridgeWorkPollLoop] 停止轮询循环...");

        // 1. 取消轮询循环
        await (_loopCts?.CancelAsync() ?? Task.CompletedTask).ConfigureAwait(false);

        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        // 2. 捕获序列号
        var lastSeq = _currentTransport?.GetLastSequenceNum() ?? 0;

        // 3. 停止工作
        if (_environmentId is not null && _currentWorkId is not null)
        {
            try
            {
                await _apiClient.StopWorkAsync(_environmentId, _currentWorkId, ct).ConfigureAwait(false);
                _logger?.LogInformation("[BridgeWorkPollLoop] 工作已停止");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[BridgeWorkPollLoop] 停止工作失败");
            }
        }

        // 4. 归档会话
        if (_currentSessionId is not null)
        {
            try
            {
                await _apiClient.ArchiveSessionAsync(_currentSessionId, ct).ConfigureAwait(false);
                _logger?.LogInformation("[BridgeWorkPollLoop] 会话已归档");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[BridgeWorkPollLoop] 归档会话失败");
            }
        }

        // 5. 关闭传输
        if (_currentTransport is not null)
        {
            try
            {
                await _currentTransport.FlushAsync(ct).ConfigureAwait(false);
                await _currentTransport.CloseAsync(ct).ConfigureAwait(false);
                await _currentTransport.DisposeAsync().ConfigureAwait(false);
                _currentTransport = null;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[BridgeWorkPollLoop] 关闭传输失败");
            }
        }

        // 6. 注销环境
        if (_environmentId is not null)
        {
            try
            {
                await _apiClient.DeregisterEnvironmentAsync(_environmentId, ct).ConfigureAwait(false);
                _logger?.LogInformation("[BridgeWorkPollLoop] 环境已注销");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[BridgeWorkPollLoop] 注销环境失败");
            }
        }

        _currentWorkId = null;
        _currentSessionId = null;
        _environmentId = null;

        StateChanged?.Invoke(this, new BridgePollStateEventArgs("stopped"));
        _logger?.LogInformation("[BridgeWorkPollLoop] 已停止");
    }

    /// <summary>
    /// 设置当前传输 — 外部调用方在收到工作后创建传输并设置
    /// </summary>
    public void SetTransport(IReplBridgeTransport transport)
    {
        var oldTransport = _currentTransport;
        _currentTransport = transport;
        Interlocked.Increment(ref _transportGeneration);

        // 关闭旧传输
        if (oldTransport is not null)
        {
            try
            {
                // P1-4: 改用异步关闭+释放，消除 sync 方法中的 sync-over-async 阻塞
                _ = Task.Run(async () =>
                {
                    try { await oldTransport.CloseAsync().ConfigureAwait(false); }
                    catch (Exception closeEx) { System.Diagnostics.Trace.WriteLine($"[BridgeWorkPollLoop] CloseAsync old transport failed: {closeEx.Message}"); }
                    await oldTransport.DisposeAsync().ConfigureAwait(false);
                });
            }
            catch (Exception ex)
            {
                // 忽略旧传输关闭错误
                System.Diagnostics.Trace.WriteLine($"[BridgeWorkPollLoop] Close old transport failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 清除当前传输 — 工作完成或传输关闭时调用
    /// </summary>
    public void ClearTransport()
    {
        _currentTransport = null;
        _currentWorkId = null;
    }

    /// <summary>
    /// 唤醒轮询循环 — 对齐 TS 端 wakePollLoop
    /// 传输关闭后调用，使轮询循环从 at-capacity 心跳睡眠中醒来快速轮询
    /// </summary>
    public void Wake()
    {
        // 优先使用 CapacityWakeService 唤醒 — 对齐 TS 端 capacityWake.wake()
        _capacityWake?.WakeUp();
        // 备用: 取消当前等待以触发下一轮轮询
        _loopCts?.CancelAfter(TimeSpan.FromMilliseconds(100));
    }

    #region 轮询循环

    /// <summary>
    /// 工作轮询循环 — 对齐 TS 端 startWorkPollLoop
    /// </summary>
    private async Task RunPollLoopAsync(CancellationToken ct)
    {
        _logger?.LogInformation("[BridgeWorkPollLoop] 轮询循环已启动");

        while (!ct.IsCancellationRequested && _isRunning != 0)
        {
            try
            {
                if (_environmentId is null)
                {
                    _logger?.LogWarning("[BridgeWorkPollLoop] 环境未注册，等待...");
                    await Task.Delay(_options.ErrorRetryBaseDelayMs, ct).ConfigureAwait(false);
                    continue;
                }

                // at-capacity 心跳模式 — 对齐 TS 端
                if (_currentTransport is not null && _currentWorkId is not null)
                {
                    await RunAtCapacityHeartbeatAsync(ct).ConfigureAwait(false);
                    continue;
                }

                // 轮询工作 — 对齐 TS 端: pollForWork(envId, envSecret, signal, pollConfig.reclaim_older_than_ms)
                var work = await _apiClient.PollForWorkAsync(_environmentId, ct, _options.ReclaimOlderThanMs).ConfigureAwait(false);

                // 重置错误计数
                _consecutiveErrors = 0;
                _firstErrorTime = null;

                if (work is null)
                {
                    // 无可用工作，空闲等待
                    var idleDelay = _options.IdlePollIntervalMs;
                    await Task.Delay(idleDelay, ct).ConfigureAwait(false);
                    continue;
                }

                // 处理工作项
                await HandleWorkItemAsync(work, ct).ConfigureAwait(false);
            }
            catch (BridgeFatalError ex)
            {
                _logger?.LogError(ex, "[BridgeWorkPollLoop] 致命错误: {Message}", ex.Message);
                FatalError?.Invoke(this, new BridgePollErrorEventArgs(ex, "poll_fatal"));

                // 404 = 环境丢失，尝试重注册
                if (ex.StatusCode == 404)
                {
                    await HandleEnvironmentLostAsync(ct).ConfigureAwait(false);
                    continue;
                }

                // 其他致命错误，停止循环
                break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _consecutiveErrors++;
                var now = _clock.GetUtcNow();

                // 对齐 TS 端: 系统休眠检测 — 如果上次错误间隔远超最大退避延迟，重置预算
                if (_lastErrorTime != DateTime.MinValue &&
                    (now - _lastErrorTime) > TimeSpan.FromMilliseconds(_options.ErrorRetryMaxDelayMs * 2))
                {
                    _logger?.LogDebug("[BridgeWorkPollLoop] 检测到系统休眠，重置轮询错误预算");
                    _consecutiveErrors = 0;
                    _firstErrorTime = null;
                }

                _lastErrorTime = now;

                // 对齐 TS 端: 首次错误记录时间
                if (_firstErrorTime is null)
                {
                    _firstErrorTime = now;
                }

                // 对齐 TS 端: 连续失败超过 15 分钟则放弃
                var elapsed = now - _firstErrorTime;
                if (elapsed >= PollErrorGiveUp)
                {
                    _logger?.LogError("[BridgeWorkPollLoop] 轮询连续失败超过 {GiveUpMin} 分钟，放弃",
                        PollErrorGiveUp.TotalMinutes);
                    FatalError?.Invoke(this, new BridgePollErrorEventArgs(
                        new BridgeFatalError("Poll failures exceeded give-up threshold"), "poll_give_up"));
                    break;
                }

                var delay = CalculateErrorDelay(_consecutiveErrors);
                _logger?.LogWarning(ex,
                    "[BridgeWorkPollLoop] 轮询错误（第 {Count} 次，已持续 {Elapsed}s），{Delay}ms 后重试",
                    _consecutiveErrors, (int)elapsed.Value.TotalSeconds, delay);

                StateChanged?.Invoke(this, new BridgePollStateEventArgs("error"));

                try
                {
                    // 对齐 TS 端: 错误退避期间发送心跳，防止工作项租约过期
                    if (_environmentId is not null && _currentWorkId is not null)
                    {
                        try
                        {
                            await _apiClient.HeartbeatWorkAsync(
                                _environmentId, _currentWorkId, _currentIngressToken, ct).ConfigureAwait(false);
                        }
                        catch (Exception ex2)
                        {
                            // best-effort: 心跳失败不阻塞退避
                            System.Diagnostics.Trace.WriteLine($"[BridgeWorkPollLoop] Heartbeat during backoff failed: {ex2.Message}");
                        }
                    }

                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger?.LogInformation("[BridgeWorkPollLoop] 轮询循环已停止");
    }

    /// <summary>
    /// at-capacity 心跳模式 — 对齐 TS 端
    /// 当已有传输在工作时，定期发送心跳保持租约
    /// </summary>
    private async Task RunAtCapacityHeartbeatAsync(CancellationToken ct)
    {
        var atCapacityDeadline = _clock.GetUtcNow().AddMilliseconds(_options.AtCapacityPollIntervalMs);

        while (!ct.IsCancellationRequested && _isRunning != 0 && _currentTransport is not null)
        {
            if (_clock.GetUtcNow() >= atCapacityDeadline)
            {
                // 到达 at-capacity 超时，回到外层 poll
                return;
            }

            try
            {
                // 发送工作心跳
                if (_environmentId is not null && _currentWorkId is not null)
                {
                    var heartbeat = await _apiClient.HeartbeatWorkAsync(
                        _environmentId, _currentWorkId, _currentIngressToken, ct).ConfigureAwait(false);

                    if (heartbeat is null)
                    {
                        _logger?.LogWarning("[BridgeWorkPollLoop] 心跳返回 null，可能工作已过期");
                        ClearTransport();
                        return;
                    }
                }

                // 进程挂起检测 — 对齐 TS 端: overrun > 60s 视为挂起
                var sleepStart = _clock.GetUtcNowOffset().ToUnixTimeMilliseconds();
                // 使用 CapacityWake 等待唤醒 — 对齐 TS 端 sleepUntilCapacityWakes
                if (_capacityWake is not null)
                {
                    var woke = await _capacityWake.SleepUntilCapacityWakesAsync(
                        TimeSpan.FromMilliseconds(_options.HeartbeatIntervalMs), ct).ConfigureAwait(false);
                    if (!woke && ct.IsCancellationRequested)
                        return;
                    // 即使未唤醒(超时), 也继续心跳循环
                }
                else
                {
                    await Task.Delay(_options.HeartbeatIntervalMs, ct).ConfigureAwait(false);
                }
                var overrun = _clock.GetUtcNowOffset().ToUnixTimeMilliseconds() - sleepStart - _options.HeartbeatIntervalMs;
                if (overrun > 60_000)
                {
                    // 进程可能被挂起（合盖/VM暂停），强制快速轮询
                    _logger?.LogWarning("[BridgeWorkPollLoop] 检测到进程挂起 ({Overrun}ms)，强制快速轮询", overrun);
                    return;
                }
            }
            catch (BridgeFatalError ex)
            {
                _logger?.LogError(ex, "[BridgeWorkPollLoop] 心跳致命错误 (status={Status})", ex.StatusCode);

                // 对齐 TS 端 onHeartbeatFatal: 通知调用方清理工作状态
                HeartbeatFatal?.Invoke(this, new BridgePollErrorEventArgs(ex, "heartbeat_fatal"));
                ClearTransport();
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[BridgeWorkPollLoop] 心跳失败");
                await Task.Delay(_options.HeartbeatIntervalMs, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// 处理工作项 — 对齐 TS 端 onWorkReceived
    /// </summary>
    private async Task HandleWorkItemAsync(BridgeWorkItem work, CancellationToken ct)
    {
        _logger?.LogInformation(
            "[BridgeWorkPollLoop] 收到工作: WorkId={WorkId}, SessionId={SessionId}",
            work.WorkId, work.SessionId);

        // 解码工作密钥 — 对齐 TS 端 decodeWorkSecret(work.secret)
        bool useCcrV2 = false;
        string? ingressToken = work.SessionIngressToken;
        string? apiBaseUrl = work.ApiBaseUrl;

        if (!string.IsNullOrEmpty(work.Secret))
        {
            try
            {
                var secret = BridgeWorkSecretDecoder.DecodeWorkSecret(work.Secret);
                useCcrV2 = secret.UseCodeSessions;
                ingressToken = secret.SessionIngressToken;
                apiBaseUrl = secret.ApiBaseUrl;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[BridgeWorkPollLoop] 解码工作密钥失败");
                // 对齐 TS 端: 解码失败则 stopWork 防止毒消息重复投递
                if (_environmentId is not null)
                {
                    try
                    {
                        await _apiClient.StopWorkAsync(_environmentId, work.WorkId, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex2) { System.Diagnostics.Trace.WriteLine($"[BridgeWorkPollLoop] StopWork after decode failure failed: {ex2.Message}"); }
                }
                return;
            }
        }

        // 环境变量覆盖 — 对齐 TS 端 CLAUDE_BRIDGE_USE_CCR_V2
        var envOverride = Environment.GetEnvironmentVariable("CLAUDE_BRIDGE_USE_CCR_V2");
        if (envOverride is "1" or "true" or "TRUE")
        {
            useCcrV2 = true;
        }

        // 确认工作 — 对齐 TS 端 acknowledgeWork(envId, work.id, secret.session_ingress_token)
        if (_environmentId is not null)
        {
            try
            {
                await _apiClient.AcknowledgeWorkAsync(
                    _environmentId, work.WorkId, ingressToken, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[BridgeWorkPollLoop] 确认工作失败");
            }
        }

        // 更新当前工作状态
        _currentWorkId = work.WorkId;
        _currentSessionId = work.SessionId;
        _currentIngressToken = ingressToken;

        // 通知外部调用方 — 由外部创建传输并设置
        WorkReceived?.Invoke(this, new BridgeWorkReceivedEventArgs(
            work.SessionId,
            ingressToken,
            work.WorkId,
            work.SdkUrl,
            apiBaseUrl,
            useCcrV2));

        StateChanged?.Invoke(this, new BridgePollStateEventArgs("working"));
    }

    /// <summary>
    /// 环境丢失处理 — 对齐 TS 端 onEnvironmentLost
    /// 尝试重新注册环境（最多3次）
    /// </summary>
    private async Task HandleEnvironmentLostAsync(CancellationToken ct)
    {
        _environmentRecreations++;

        if (_environmentRecreations > _options.MaxEnvironmentRecreations)
        {
            _logger?.LogError("[BridgeWorkPollLoop] 环境重建次数耗尽");
            FatalError?.Invoke(this, new BridgePollErrorEventArgs(
                new BridgeFatalError("Environment recreation limit exceeded"), "env_lost"));
            return;
        }

        _logger?.LogWarning(
            "[BridgeWorkPollLoop] 环境丢失，尝试重注册（第 {Count} 次）",
            _environmentRecreations);

        try
        {
            var registration = new BridgeEnvironmentRegistration
            {
                BridgeId = Guid.NewGuid().ToString("N"),
                MaxSessions = 1
            };

            var regResponse = await _apiClient.RegisterBridgeEnvironmentAsync(registration, ct).ConfigureAwait(false);
            if (regResponse is not null)
            {
                _environmentId = regResponse.EnvironmentId;
                _environmentSecret = regResponse.BridgeId;
                _logger?.LogInformation("[BridgeWorkPollLoop] 环境重注册成功: {EnvId}", _environmentId);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[BridgeWorkPollLoop] 环境重注册失败");
        }
    }

    /// <summary>
    /// 计算错误退避延迟 — 对齐 TS 端指数退避
    /// </summary>
    private int CalculateErrorDelay(int attempt)
    {
        var baseDelay = _options.ErrorRetryBaseDelayMs;
        var maxDelay = _options.ErrorRetryMaxDelayMs;
        var delay = (int)Math.Min(baseDelay * Math.Pow(2, attempt - 1), maxDelay);
        var jitter = Random.Shared.Next(0, (int)(delay * 0.1));
        return delay + jitter;
    }

    #endregion

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _loopCts?.Dispose();
        await _recentPostedUUIDs.DisposeAsync().ConfigureAwait(false);
        await _recentInboundUUIDs.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// 工作轮询选项 — 对齐 TS 端 getPollIntervalConfig
/// </summary>
public sealed class BridgeWorkPollOptions
{
    /// <summary>空闲轮询间隔（毫秒）</summary>
    public int IdlePollIntervalMs { get; init; } = 5000;

    /// <summary>at-capacity 轮询间隔（毫秒）</summary>
    public int AtCapacityPollIntervalMs { get; init; } = 30000;

    /// <summary>心跳间隔（毫秒）</summary>
    public int HeartbeatIntervalMs { get; init; } = 20000;

    /// <summary>错误重试基础延迟（毫秒）</summary>
    public int ErrorRetryBaseDelayMs { get; init; } = 2000;

    /// <summary>错误重试最大延迟（毫秒）</summary>
    public int ErrorRetryMaxDelayMs { get; init; } = 60000;

    /// <summary>最大环境重建次数</summary>
    public int MaxEnvironmentRecreations { get; init; } = 3;

    /// <summary>UUID 去重缓冲区大小</summary>
    public int UuidDedupBufferSize { get; init; } = 2000;

    /// <summary>
    /// 回收超时未确认工作项的阈值（毫秒）— 对齐 TS 端 reclaim_older_than_ms
    /// 默认 5000ms，与服务端 DEFAULT_RECLAIM_OLDER_THAN_MS 匹配
    /// </summary>
    public int ReclaimOlderThanMs { get; init; } = 5000;
}

#region 事件参数

public sealed class BridgeWorkReceivedEventArgs : EventArgs
{
    public string SessionId { get; }
    public string? IngressToken { get; }
    public string WorkId { get; }
    public string? SdkUrl { get; }
    public string? ApiBaseUrl { get; }

    /// <summary>
    /// 服务器是否指示使用 CCR v2 — 对齐 TS 端 serverUseCcrV2
    /// 来自工作密钥的 secret.use_code_sessions 字段
    /// </summary>
    public bool UseCcrV2 { get; }

    public BridgeWorkReceivedEventArgs(
        string sessionId,
        string? ingressToken,
        string workId,
        string? sdkUrl,
        string? apiBaseUrl,
        bool useCcrV2 = false)
    {
        SessionId = sessionId;
        IngressToken = ingressToken;
        WorkId = workId;
        SdkUrl = sdkUrl;
        ApiBaseUrl = apiBaseUrl;
        UseCcrV2 = useCcrV2;
    }
}

public sealed class BridgePollStateEventArgs : EventArgs
{
    public string State { get; }

    public BridgePollStateEventArgs(string state)
    {
        State = state;
    }
}

public sealed class BridgePollErrorEventArgs : EventArgs
{
    public Exception Exception { get; }
    public string ErrorType { get; }

    public BridgePollErrorEventArgs(Exception exception, string errorType)
    {
        Exception = exception;
        ErrorType = errorType;
    }
}

#endregion
