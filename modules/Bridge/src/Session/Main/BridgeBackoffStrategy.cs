namespace Core.Bridge;

/// <summary>
/// Bridge 双轨退避策略 — 对齐 TS 端 BackoffConfig + 连接错误/通用错误双轨退避
/// 连接错误: 初始 2s，上限 120s
/// 通用错误: 初始 500ms，上限 30s
/// 互斥重置: 切换错误类型时重置另一个轨道
/// 放弃阈值: 10 分钟
/// </summary>
public sealed class BridgeBackoffStrategy
{
    private readonly IClockService _clock;
    private readonly ILogger? _logger;

    private int _connErrors;
    private int _generalErrors;
    private DateTime _firstErrorTime;
    private DateTime _lastErrorTime;
    private bool _lastErrorWasConnError;

    public BridgeBackoffStrategy(IClockService clock, ILogger? logger = null)
    {
        _clock = clock;
        _logger = logger;
    }

    /// <summary>是否处于错误状态</summary>
    public bool IsInErrorState => _firstErrorTime != default;

    /// <summary>首次错误时间</summary>
    public DateTime FirstErrorTime => _firstErrorTime;

    /// <summary>
    /// 处理轮询错误 — 对齐 TS 端指数退避 + 抖动 + 双轨退避
    /// 返回 true 表示可继续重试，false 表示应放弃
    /// </summary>
    public async Task<bool> HandleErrorAsync(Exception ex, Action? onFatalExit = null, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        var isConnError = IsConnectionError(ex);

        // 互斥重置: 切换错误类型时重置另一个轨道
        if (isConnError && _lastErrorWasConnError != true)
            _generalErrors = 0;
        else if (!isConnError && _lastErrorWasConnError)
            _connErrors = 0;

        _lastErrorWasConnError = isConnError;

        if (isConnError)
            _connErrors++;
        else
            _generalErrors++;

        if (_firstErrorTime == default)
            _firstErrorTime = now;
        _lastErrorTime = now;

        // 睡眠检测: 两次错误间隔超过 240s 则重置退避预算
        var gapMs = (now - _lastErrorTime).TotalMilliseconds;
        if (gapMs > 240_000)
        {
            _logger?.LogDebug("BridgeBackoff: sleep detected, resetting backoff budget");
            Reset(onReconnected: null);
            return true;
        }

        // 放弃阈值: 10 分钟
        var totalErrorMs = (now - _firstErrorTime).TotalMilliseconds;
        if (totalErrorMs > 600_000)
        {
            onFatalExit?.Invoke();
            return false;
        }

        // 双轨退避参数
        int baseBackoffMs;
        int maxBackoffMs;
        int errorCount;

        if (isConnError)
        {
            baseBackoffMs = 2000;
            maxBackoffMs = 120_000;
            errorCount = _connErrors;
        }
        else
        {
            baseBackoffMs = 500;
            maxBackoffMs = 30_000;
            errorCount = _generalErrors;
        }

        var backoff = Math.Min(baseBackoffMs * (1 << Math.Min(errorCount - 1, 6)), maxBackoffMs);
        var jitterMs = (int)(backoff * 0.25 * (Random.Shared.NextDouble() * 2 - 1));
        var backoffMs = Math.Max(500, backoff + jitterMs);

        _logger?.LogWarning(ex, "BridgeBackoff: error #{Count} ({Type}), backing off {BackoffMs}ms",
            errorCount, isConnError ? "conn" : "general", backoffMs);

        // P1-5: 改用 Task.Delay 异步等待，消除同步阻塞
        await Task.Delay(backoffMs, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// 重置退避状态 — 成功通信后调用
    /// </summary>
    public void Reset(Action<long>? onReconnected = null)
    {
        if (_firstErrorTime != default)
        {
            var disconnectedMs = (long)(_clock.GetUtcNow() - _firstErrorTime).TotalMilliseconds;
            onReconnected?.Invoke(disconnectedMs);
            _logger?.LogInformation("BridgeBackoff: reconnected after {Ms}ms", disconnectedMs);
        }

        _connErrors = 0;
        _generalErrors = 0;
        _firstErrorTime = default;
        _lastErrorTime = default;
    }

    /// <summary>
    /// 判断是否为连接错误 — 对齐 TS 端 isConnectionError()
    /// </summary>
    private static bool IsConnectionError(Exception ex)
    {
        return ex is HttpRequestException
            || ex is System.Net.Sockets.SocketException
            || ex is OperationCanceledException
            || (ex.InnerException is not null && IsConnectionError(ex.InnerException));
    }
}
