namespace JoinCode.Transport.Bridge;

/// <summary>
/// Bridge 专用 Token 刷新调度器 — 对齐 TS 端 createTokenRefreshScheduler
/// 按 sessionId 键管理定时器，支持 JWT exp 解码 + expires_in + 代际计数器 + 失败重试
/// </summary>
public sealed class BridgeTokenRefreshScheduler : ITokenRefreshScheduler
{
    private readonly TokenRefreshOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IClockService _clock;

    // 按 sessionId 管理定时器
    private readonly Dictionary<string, ITimer> _timers = new();
    // 代际计数器 — 防止过期异步 doRefresh 设置孤立定时器
    private readonly Dictionary<string, long> _generations = new();
    // 连续失败计数
    private readonly Dictionary<string, int> _failureCounts = new();
    // 异步锁 — 替代 lock 语句避免 JCC4001
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private const int MaxRefreshFailures = 3;
    private const int FailureRetryDelayMs = 60_000;
    private const int FallbackRefreshIntervalMs = 30 * 60 * 1000; // 30 分钟

    public BridgeTokenRefreshScheduler(
        TokenRefreshOptions options,
        TimeProvider? timeProvider = null,
        IClockService? clock = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <summary>
    /// 基于 JWT exp 声明调度刷新 — 对齐 TS 端 schedule(sessionId, token)
    /// </summary>
    public void Schedule(string sessionId, string token)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(token);

        var expiryMs = DecodeJwtExpiry(token);
        if (expiryMs is null)
        {
            _options.Logger?.LogDebug("[{Label}] 无法解码 JWT exp，使用回退间隔", _options.Label);
            ScheduleFromDelay(sessionId, FallbackRefreshIntervalMs);
            return;
        }

        var delayMs = expiryMs.Value - _options.RefreshBufferMs - _clock.GetUtcNowOffset().ToUnixTimeMilliseconds();
        if (delayMs <= 0)
        {
            // 已过期或即将过期，立即刷新
            _ = DoRefreshAsync(sessionId);
            return;
        }

        ScheduleFromDelay(sessionId, delayMs);
    }

    /// <summary>
    /// 基于 expires_in 调度刷新 — 对齐 TS 端 scheduleFromExpiresIn
    /// 支持不透明 JWT（服务端直接返回 expires_in）
    /// </summary>
    public void ScheduleFromExpiresIn(string sessionId, int expiresInSeconds)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        var delayMs = (expiresInSeconds * 1000L) - _options.RefreshBufferMs;
        if (delayMs <= 0)
        {
            _ = DoRefreshAsync(sessionId);
            return;
        }

        ScheduleFromDelay(sessionId, delayMs);
    }

    /// <summary>取消指定会话的刷新定时器</summary>
    public void Cancel(string sessionId)
    {
        _semaphore.Wait();
        try
        {
            if (_timers.Remove(sessionId, out var timer))
            {
                timer.Dispose();
            }

            _generations.Remove(sessionId);
            _failureCounts.Remove(sessionId);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>取消所有刷新定时器</summary>
    public void CancelAll()
    {
        _semaphore.Wait();
        try
        {
            foreach (var timer in _timers.Values)
            {
                timer.Dispose();
            }

            _timers.Clear();
            _generations.Clear();
            _failureCounts.Clear();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private void ScheduleFromDelay(string sessionId, long delayMs)
    {
        _semaphore.Wait();
        try
        {
            // 递增代际计数器
            ref var generation = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(_generations, sessionId, out _);
            generation++;

            // 取消旧定时器
            if (_timers.Remove(sessionId, out var oldTimer))
            {
                oldTimer.Dispose();
            }

            // 重置失败计数
            _failureCounts.Remove(sessionId);

            var currentGeneration = generation;
            _timers[sessionId] = _timeProvider.CreateTimer(_ =>
            {
                // 代际检查 — 防止过期异步 doRefresh 设置孤立定时器
                _semaphore.Wait();
                try
                {
                    if (!_generations.TryGetValue(sessionId, out var gen) || gen != currentGeneration)
                    {
                        return;
                    }
                }
                finally
                {
                    _semaphore.Release();
                }

                _ = DoRefreshAsync(sessionId);
            }, null, TimeSpan.FromMilliseconds(delayMs), Timeout.InfiniteTimeSpan);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task DoRefreshAsync(string sessionId)
    {
        // 对齐 TS: doRefresh 中记录当前代际，异步操作后检查是否已过时
        long generationBeforeRefresh;
        _semaphore.Wait();
        try
        {
            generationBeforeRefresh = _generations.GetValueOrDefault(sessionId);
        }
        finally
        {
            _semaphore.Release();
        }

        try
        {
            var newToken = _options.GetAccessToken();
            if (newToken is not null)
            {
                // 对齐 TS: 异步操作后检查代际是否已变化
                // 如果 schedule/cancel 在此期间被调用，此 doRefresh 已过时
                _semaphore.Wait();
                try
                {
                    if (!_generations.TryGetValue(sessionId, out var gen) || gen != generationBeforeRefresh)
                    {
                        _options.Logger?.LogDebug("[{Label}] doRefresh 过时 (gen {OldGen} vs {NewGen})，跳过: {SessionId}",
                            _options.Label, generationBeforeRefresh, _generations.GetValueOrDefault(sessionId), sessionId);
                        return;
                    }
                }
                finally
                {
                    _semaphore.Release();
                }

                _options.OnRefresh(sessionId, newToken);
                _options.Logger?.LogDebug("[{Label}] Token 刷新成功: {SessionId}", _options.Label, sessionId);

                // 重置失败计数 + 调度后续刷新（Schedule 内部获取信号量）
                _semaphore.Wait();
                try
                {
                    _failureCounts.Remove(sessionId);
                }
                finally
                {
                    _semaphore.Release();
                }

                Schedule(sessionId, newToken);
            }
        }
        catch (Exception ex)
        {
            _options.Logger?.LogWarning(ex, "[{Label}] Token 刷新失败: {SessionId}", _options.Label, sessionId);

            _semaphore.Wait();
            try
            {
                ref var failures = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(_failureCounts, sessionId, out _);
                failures++;

                if (failures >= MaxRefreshFailures)
                {
                    _options.Logger?.LogError("[{Label}] Token 刷新连续失败 {Count} 次，{DelayMs}ms 后重试: {SessionId}",
                        _options.Label, failures, FailureRetryDelayMs, sessionId);

                    // 重置失败计数并延迟重试
                    _failureCounts[sessionId] = 0;
                    ScheduleFromDelay(sessionId, FailureRetryDelayMs);
                }
                else
                {
                    // 回退刷新间隔
                    ScheduleFromDelay(sessionId, FallbackRefreshIntervalMs);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }

    /// <summary>
    /// 解码 JWT exp 声明 — 对齐 TS 端 decodeJwtExpiry
    /// 返回过期时间的 Unix 毫秒时间戳
    /// </summary>
    private static long? DecodeJwtExpiry(string token)
    {
        try
        {
            // JWT 格式: header.payload.signature
            var parts = token.Split('.');
            if (parts.Length < 2) return null;

            // base64url 解码 payload
            var payload = parts[1];
            var base64 = payload.Replace('-', '+').Replace('_', '/');
            var padding = base64.Length % 4;
            if (padding > 0) base64 += new string('=', 4 - padding);

            var jsonBytes = Convert.FromBase64String(base64);
            var json = System.Text.Encoding.UTF8.GetString(jsonBytes);

            // 手动查找 "exp" 字段 — AOT 兼容
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("exp", out var expElement))
            {
                var expSeconds = expElement.GetInt64();
                return expSeconds * 1000; // 转为毫秒
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        CancelAll();
        await ValueTask.CompletedTask.ConfigureAwait(false);
    }
}
