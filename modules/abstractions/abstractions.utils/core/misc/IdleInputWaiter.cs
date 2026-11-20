namespace JoinCode.Abstractions.Utils;

/// <summary>空闲输入等待器 — 轮询检查输入是否就绪，超过空闲时限返回 false（不阻塞线程）。</summary>
public sealed class IdleInputWaiter {
    private readonly Func<bool> _isInputAvailable;
    private readonly Func<CancellationToken, Task> _pollDelay;

    /// <summary>构造空闲输入等待器。</summary>
    /// <param name="isInputAvailable">输入是否已就绪（非阻塞检查）</param>
    /// <param name="pollDelay">两次检查之间的等待委托</param>
    public IdleInputWaiter(Func<bool> isInputAvailable, Func<CancellationToken, Task> pollDelay) {
        _isInputAvailable = isInputAvailable ?? throw new ArgumentNullException(nameof(isInputAvailable));
        _pollDelay = pollDelay ?? throw new ArgumentNullException(nameof(pollDelay));
    }

    /// <summary>等待输入就绪，超过空闲时限返回 false。</summary>
    /// <param name="idleTimeout">空闲时限</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>输入已就绪返回 true，超时返回 false</returns>
    public async Task<bool> WaitAsync(TimeSpan idleTimeout, CancellationToken ct) {
        var started = Environment.TickCount64;
        var timeoutMs = (long)idleTimeout.TotalMilliseconds;

        while (Environment.TickCount64 - started < timeoutMs) {
            ct.ThrowIfCancellationRequested();
            if (_isInputAvailable())
                return true;
            await _pollDelay(ct).ConfigureAwait(false);
        }

        return _isInputAvailable();
    }
}
