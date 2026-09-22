namespace JoinCode.Transport.Bridge;

/// <summary>
/// Bridge 专用 Token 刷新调度器 — Actor 化：Consumer 线程独占 _timers/_generations/_failureCounts，消除 AsyncLock。
/// <para>代际检查在 Consumer 中自然串行，DoRefreshAsync 不需要多次 TryLock。</para>
/// 按 sessionId 键管理定时器，支持 JWT exp 解码 + expires_in + 代际计数器 + 失败重试
/// </summary>
public sealed class BridgeTokenRefreshScheduler : ActorBase<IBridgeTokenRefreshCommand, Unit>, ITokenRefreshScheduler {
    private readonly TokenRefreshOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IClockService _clock;

    private readonly Dictionary<string, ITimer> _timers = new();
    private readonly Dictionary<string, long> _generations = new();
    private readonly Dictionary<string, int> _failureCounts = new();

    private const int MaxRefreshFailures = 3;
    private const int FailureRetryDelayMs = 60_000;
    private const int FallbackRefreshIntervalMs = 30 * 60 * 1000;
    private int _disposed;

    /// <summary>
    /// 构造 Bridge Token 刷新调度器
    /// </summary>
    /// <param name="options">刷新选项</param>
    /// <param name="timeProvider">时间提供者（可选，默认系统时间）</param>
    /// <param name="clock">时钟服务（可选，默认系统时钟）</param>
    public BridgeTokenRefreshScheduler(
        TokenRefreshOptions options,
        TimeProvider? timeProvider = null,
        IClockService? clock = null)
        : base() {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <summary>
    /// 基于 JWT exp 声明调度刷新
    /// </summary>
    public void Schedule(string sessionId, string token) {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(token);

        var expiryMs = DecodeJwtExpiry(token);
        if (expiryMs is null) {
            _options.Logger?.LogDebug("[{Label}] 无法解码 JWT exp，使用回退间隔", _options.Label);
            TrySend(new ScheduleFromDelayCmd(sessionId, FallbackRefreshIntervalMs));
            return;
        }

        var delayMs = expiryMs.Value - _options.RefreshBufferMs - _clock.GetUtcNowOffset().ToUnixTimeMilliseconds();
        if (delayMs <= 0) {
            TrySend(new DoRefreshCmd(sessionId));
            return;
        }

        TrySend(new ScheduleFromDelayCmd(sessionId, delayMs));
    }

    /// <summary>
    /// 基于 expires_in 调度刷新
    /// </summary>
    public void ScheduleFromExpiresIn(string sessionId, int expiresInSeconds) {
        ArgumentNullException.ThrowIfNull(sessionId);

        var delayMs = (expiresInSeconds * 1000L) - _options.RefreshBufferMs;
        if (delayMs <= 0) {
            TrySend(new DoRefreshCmd(sessionId));
            return;
        }

        TrySend(new ScheduleFromDelayCmd(sessionId, delayMs));
    }

    /// <summary>取消指定会话的刷新定时器</summary>
    public void Cancel(string sessionId) {
        TrySend(new CancelCmd(sessionId));
    }

    /// <summary>取消所有刷新定时器</summary>
    public void CancelAll() {
        TrySend(new CancelAllCmd());
    }

    /// <summary>
    /// 处理命令的核心逻辑 — Consumer 线程独占
    /// </summary>
    /// <param name="command">待处理命令</param>
    /// <param name="ct">取消令牌</param>
    protected override async ValueTask HandleAsync(IBridgeTokenRefreshCommand command, CancellationToken ct) {
        switch (command) {
            case ScheduleFromDelayCmd sched:
            await ScheduleFromDelayCore(sched.SessionId, sched.DelayMs).ConfigureAwait(false);
            break;

            case DoRefreshCmd refresh:
            await DoRefreshCoreAsync(refresh.SessionId).ConfigureAwait(false);
            break;

            case CancelCmd cancel:
            if (_timers.Remove(cancel.SessionId, out var timer))
                await timer.DisposeAsync().ConfigureAwait(false);
            _generations.Remove(cancel.SessionId);
            _failureCounts.Remove(cancel.SessionId);
            break;

            case CancelAllCmd:
            foreach (var t in _timers.Values)
                await t.DisposeAsync().ConfigureAwait(false);
            _timers.Clear();
            _generations.Clear();
            _failureCounts.Clear();
            break;
        }
    }

    /// <summary>
    /// 消费者线程异常回调 — 记录日志
    /// </summary>
    /// <param name="ex">捕获的异常</param>
    protected override void OnConsumerError(Exception ex) {
        _options.Logger?.LogWarning(ex, "[{Label}] BridgeTokenRefresh 消费者异常", _options.Label);
    }

    private async Task ScheduleFromDelayCore(string sessionId, long delayMs) {
        ref var generation = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(_generations, sessionId, out _);
        generation++;
        var currentGeneration = generation;

        if (_timers.Remove(sessionId, out var oldTimer))
            await oldTimer.DisposeAsync().ConfigureAwait(false);

        _failureCounts.Remove(sessionId);

        _timers[sessionId] = _timeProvider.CreateTimer(_ => {
            if (_generations.TryGetValue(sessionId, out var gen) && gen == currentGeneration) {
                TrySend(new DoRefreshCmd(sessionId));
            }
        }, null, TimeSpan.FromMilliseconds(delayMs), Timeout.InfiniteTimeSpan);
    }

    private async Task DoRefreshCoreAsync(string sessionId) {
        var generationBeforeRefresh = _generations.GetValueOrDefault(sessionId);

        try {
            var newToken = _options.GetAccessToken();
            if (newToken is not null) {
                if (!_generations.TryGetValue(sessionId, out var gen) || gen != generationBeforeRefresh) {
                    _options.Logger?.LogDebug("[{Label}] doRefresh 过时 (gen {OldGen} vs {NewGen})，跳过: {SessionId}",
                        _options.Label, generationBeforeRefresh, _generations.GetValueOrDefault(sessionId), sessionId);
                    return;
                }

                _options.OnRefresh(sessionId, newToken);
                _options.Logger?.LogDebug("[{Label}] Token 刷新成功: {SessionId}", _options.Label, sessionId);

                _failureCounts.Remove(sessionId);
                Schedule(sessionId, newToken);
            }
        } catch (Exception ex) {
            _options.Logger?.LogWarning(ex, "[{Label}] Token 刷新失败: {SessionId}", _options.Label, sessionId);

            ref var failures = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(_failureCounts, sessionId, out _);
            failures++;

            if (failures >= MaxRefreshFailures) {
                _options.Logger?.LogError("[{Label}] Token 刷新连续失败 {Count} 次，{DelayMs}ms 后重试: {SessionId}",
                    _options.Label, failures, FailureRetryDelayMs, sessionId);
                _failureCounts[sessionId] = 0;
                await ScheduleFromDelayCore(sessionId, FailureRetryDelayMs).ConfigureAwait(false);
            } else {
                await ScheduleFromDelayCore(sessionId, FallbackRefreshIntervalMs).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// 解码 JWT exp 声明
    /// </summary>
    private static long? DecodeJwtExpiry(string token) {
        try {
            var parts = token.Split('.');
            if (parts.Length < 2) return null;

            var payload = parts[1];
            var base64 = payload.Replace('-', '+').Replace('_', '/');
            var padding = base64.Length % 4;
            if (padding > 0) base64 += new string('=', 4 - padding);

            var jsonBytes = Convert.FromBase64String(base64);
            var json = System.Text.Encoding.UTF8.GetString(jsonBytes);

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("exp", out var expElement)) {
                var expSeconds = expElement.GetInt64();
                return expSeconds * 1000;
            }

            return null;
        } catch {
            return null;
        }
    }

    /// <summary>
    /// 异步释放资源，取消所有刷新定时器
    /// </summary>
    public override ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        CancelAll();
        return base.DisposeAsync();
    }
}

/// <summary>
/// Bridge Token 刷新命令 — Actor 消息类型
/// </summary>
public interface IBridgeTokenRefreshCommand;

/// <summary>按延迟调度刷新命令</summary>
public sealed record ScheduleFromDelayCmd(string SessionId, long DelayMs) : IBridgeTokenRefreshCommand;
/// <summary>立即执行刷新命令</summary>
public sealed record DoRefreshCmd(string SessionId) : IBridgeTokenRefreshCommand;
/// <summary>取消指定会话刷新命令</summary>
public sealed record CancelCmd(string SessionId) : IBridgeTokenRefreshCommand;
/// <summary>取消所有会话刷新命令</summary>
public sealed record CancelAllCmd : IBridgeTokenRefreshCommand;