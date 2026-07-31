namespace Api.LLM.Fallback;

/// <summary>
/// 流式空闲看门狗 — 检测流长时间无数据到达，主动中止流触发 fallback
/// 对齐 TS claude.ts 的 streamIdleAborted / resetStreamIdleTimer / STREAM_IDLE_TIMEOUT_MS
/// </summary>
/// <remarks>
/// 使用方式：
/// 1. 创建 watchdog，传入原始 CancellationToken
/// 2. 在流式循环中 await foreach (var chunk in stream.WithCancellation(watchdog.CombinedToken))
/// 3. 每个 chunk 到达后调用 watchdog.Reset()
/// 4. 流结束后检查 watchdog.WasIdleAborted
/// 5. 使用 using 确保资源释放
/// </remarks>
public sealed class StreamIdleWatchdog : IDisposable
{
    private readonly int _idleTimeoutMs;
    private readonly CancellationTokenSource _watchdogCts;
    private readonly CancellationTokenRegistration _originalRegistration;
    private Timer? _timer;
    private volatile bool _aborted;
    private volatile bool _disposed;

    /// <summary>
    /// 是否收到过任何 chunk — 用于检测不完整流（200 但无 SSE 事件）
    /// </summary>
    public bool ReceivedAnyChunk { get; private set; }

    /// <summary>
    /// 看门狗是否因空闲超时而触发中止
    /// </summary>
    public bool WasIdleAborted => _aborted;

    /// <summary>
    /// 合并的 CancellationToken — 链接原始令牌和看门狗超时令牌
    /// 流式循环应使用此令牌而非原始令牌
    /// </summary>
    public CancellationToken CombinedToken { get; }

    /// <summary>
    /// 创建流式空闲看门狗
    /// </summary>
    /// <param name="idleTimeoutMs">空闲超时毫秒数（默认 90000 = 90s）</param>
    /// <param name="originalToken">调用方的原始取消令牌</param>
    /// <param name="enabled">是否启用看门狗（禁用时 CombinedToken = originalToken）</param>
    public StreamIdleWatchdog(int idleTimeoutMs, CancellationToken originalToken, bool enabled = true)
    {
        _idleTimeoutMs = idleTimeoutMs;

        if (!enabled || originalToken.IsCancellationRequested)
        {
            CombinedToken = originalToken;
            _watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(originalToken);
            return;
        }

        _watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(originalToken);
        CombinedToken = _watchdogCts.Token;

        _originalRegistration = originalToken.Register(static state =>
        {
            var self = (StreamIdleWatchdog)state!;
            self.ClearTimer();
        }, this);

        _timer = new Timer(static state =>
        {
            var self = (StreamIdleWatchdog)state!;
            self.OnIdleTimeout();
        }, this, idleTimeoutMs, Timeout.Infinite);
    }

    /// <summary>
    /// 重置空闲计时器 — 每个 chunk 到达时调用
    /// </summary>
    public void Reset()
    {
        if (_disposed || _aborted) return;

        ReceivedAnyChunk = true;

        _timer?.Change(_idleTimeoutMs, Timeout.Infinite);
    }

    /// <summary>
    /// 看门狗超时触发 — 中止流
    /// </summary>
    private void OnIdleTimeout()
    {
        if (_disposed || _aborted) return;

        _aborted = true;

        CancelWatchdogCts();
    }

    private void CancelWatchdogCts()
    {
        try
        {
            if (!_watchdogCts.IsCancellationRequested)
                _watchdogCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            System.Diagnostics.Debug.WriteLine("StreamIdleWatchdog: CTS already disposed during cancel");
        }
    }

    private void ClearTimer()
    {
        try
        {
            _timer?.Dispose();
            _timer = null;
        }
        catch (ObjectDisposedException)
        {
            System.Diagnostics.Debug.WriteLine("StreamIdleWatchdog: Timer already disposed during clear");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ClearTimer();
        _originalRegistration.Dispose();

        CancelWatchdogCts();
        _watchdogCts.Dispose();
    }
}
