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
    /// <summary>刷新延迟下限（TASK071 #3 — 对齐 TS jwtUtils.ts:157 clamp 30s：buffer 超过 expires_in 时防紧循环）。</summary>
    private const int MinRefreshDelayMs = 30_000;
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

        var delayMs = Math.Max(
            expiryMs.Value - _options.RefreshBufferMs - _clock.GetUtcNowOffset().ToUnixTimeMilliseconds(),
            MinRefreshDelayMs);
        TrySend(new ScheduleFromDelayCmd(sessionId, delayMs));
    }

    /// <summary>
    /// 基于 expires_in 调度刷新
    /// </summary>
    public void ScheduleFromExpiresIn(string sessionId, int expiresInSeconds) {
        ArgumentNullException.ThrowIfNull(sessionId);

        // 对齐 TS jwtUtils.ts:157 — clamp 30s 下限：buffer 超过 expires_in 时防立即刷新紧循环
        var delayMs = Math.Max((expiresInSeconds * 1000L) - _options.RefreshBufferMs, MinRefreshDelayMs);
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
    protected override void Handle(IBridgeTokenRefreshCommand command, CancellationToken ct) {
        RegisterInFlight(HandleAsyncImpl(command, ct).AsTask());
    }

    private async ValueTask HandleAsyncImpl(IBridgeTokenRefreshCommand command, CancellationToken ct) {
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

        // TASK071 #3 — 此处禁止清零 _failureCounts（旧实现重试调度即清零 → MaxRefreshFailures 永不触发）；
        // 计数只在成功（DoRefreshCoreAsync 成功路径）/取消（Cancel/CancelAll）/达上限放弃时清理。

        _timers[sessionId] = _timeProvider.CreateTimer(_ => {
            if (_generations.TryGetValue(sessionId, out var gen) && gen == currentGeneration) {
                TrySend(new DoRefreshCmd(sessionId));
            }
        }, null, TimeSpan.FromMilliseconds(delayMs), Timeout.InfiniteTimeSpan);
    }

    private async Task DoRefreshCoreAsync(string sessionId) {
        var generationBeforeRefresh = _generations.GetValueOrDefault(sessionId);

        // 对齐 TS jwtUtils.ts:167-176 — getAccessToken 异常吞掉走 null 分支（不中断刷新链）
        string? newToken = null;
        try {
            newToken = _options.GetAccessToken();
        } catch (Exception ex) {
            _options.Logger?.LogWarning(ex, "[{Label}] getAccessToken 异常: {SessionId}", _options.Label, sessionId);
        }

        // 对齐 TS jwtUtils.ts:178-186 — 等待期间被取消/重排则代际失效，直接退出
        if (!_generations.TryGetValue(sessionId, out var gen) || gen != generationBeforeRefresh) {
            _options.Logger?.LogDebug("[{Label}] doRefresh 过时 (gen {OldGen} vs {NewGen})，跳过: {SessionId}",
                _options.Label, generationBeforeRefresh, _generations.GetValueOrDefault(sessionId), sessionId);
            return;
        }

        // TASK071 #3 — 对齐 TS jwtUtils.ts:188-206：null token 计数失败，未达上限 60s 重试，达上限放弃。
        // 旧实现 null token 无任何处理（刷新链静默中断）。
        if (newToken is null) {
            var failures = IncrementFailure(sessionId);
            _options.Logger?.LogError("[{Label}] 无可用 OAuth token，刷新失败 {Failures}/{Max}: {SessionId}",
                _options.Label, failures, MaxRefreshFailures, sessionId);
            if (failures < MaxRefreshFailures) {
                await ScheduleFromDelayCore(sessionId, FailureRetryDelayMs).ConfigureAwait(false);
            } else {
                _options.Logger?.LogError("[{Label}] 刷新连续失败达上限，放弃自动刷新链（等待上游 401 恢复接管）: {SessionId}",
                    _options.Label, sessionId);
                _failureCounts.Remove(sessionId);
            }
            return;
        }

        try {
            _options.OnRefresh(sessionId, newToken);
            _options.Logger?.LogDebug("[{Label}] Token 刷新成功: {SessionId}", _options.Label, sessionId);

            _failureCounts.Remove(sessionId);
            Schedule(sessionId, newToken);
        } catch (Exception ex) {
            _options.Logger?.LogWarning(ex, "[{Label}] Token 刷新失败: {SessionId}", _options.Label, sessionId);

            // 对齐 TS — 失败计数不清零（旧实现在 ScheduleFromDelayCore 清零 → 上限永不触发）；
            // 未达上限 60s 重试，达上限放弃
            var failures = IncrementFailure(sessionId);
            if (failures < MaxRefreshFailures) {
                await ScheduleFromDelayCore(sessionId, FailureRetryDelayMs).ConfigureAwait(false);
            } else {
                _options.Logger?.LogError("[{Label}] 刷新连续失败达上限，放弃自动刷新链（等待上游 401 恢复接管）: {SessionId}",
                    _options.Label, sessionId);
                _failureCounts.Remove(sessionId);
            }
        }
    }

    private int IncrementFailure(string sessionId) {
        ref var failures = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(_failureCounts, sessionId, out _);
        failures++;
        return failures;
    }

    /// <summary>
    /// 解码 JWT exp 声明
    /// </summary>
    internal static long? DecodeJwtExpiry(string token) {
        try {
            var parts = token.Split('.');
            if (parts.Length < 2) return null;

            var payload = parts[1];
            var base64 = payload.Replace('-', '+').Replace('_', '/');
            var padding = base64.Length % 4;
            if (padding > 0) base64 += new string('=', 4 - padding);

            var jsonBytes = Convert.FromBase64String(base64);
            var json = System.Text.Encoding.UTF8.GetString(jsonBytes);

            var jwtPayload = JsonSerializer.Deserialize(json, TransportBridgeJsonContext.Default.JwtPayloadExpDto);
            if (jwtPayload?.Exp is { } expSeconds) {
                return ClampExpiryToMilliseconds(expSeconds);
            }

            return null;
        } catch {
            return null;
        }
    }

    /// <summary>
    /// 将 JWT exp（秒）钳制为毫秒，防止 long 乘法溢出。
    /// 纯函数，对齐 TS 端 exp*1000 但加溢出守卫。
    /// </summary>
    /// <param name="expSeconds">JWT exp 声明值（unix 秒）</param>
    /// <returns>exp*1000（毫秒），溢出时钳制到 long.MaxValue</returns>
    internal static long ClampExpiryToMilliseconds(long expSeconds) {
        // long.MaxValue / 1000 = 9223372036854775，超过此值 expSeconds*1000 会溢出
        const long maxSafeSeconds = long.MaxValue / 1000;
        if (expSeconds > maxSafeSeconds) return long.MaxValue;
        if (expSeconds < long.MinValue / 1000) return long.MinValue;
        return expSeconds * 1000;
    }

    /// <summary>
    /// 异步释放资源，取消所有刷新定时器
    /// </summary>
    public override ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        CancelAll();
        return base.DisposeAsync();
    }

    /// <summary>Consumer 退出后释放所有定时器(DSG033 S2)</summary>
    protected override async ValueTask PostStopAsync() {
        foreach (var t in _timers.Values)
            await t.DisposeAsync().ConfigureAwait(false);
        _timers.Clear();
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