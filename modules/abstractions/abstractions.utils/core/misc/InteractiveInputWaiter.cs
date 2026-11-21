namespace JoinCode.Abstractions.Utils;

/// <summary>输入等待结果 — 输入就绪或收到后台唤醒信号。</summary>
public enum InputWaitOutcome {
    /// <summary>输入已就绪，可以读取。</summary>
    InputReady,
    /// <summary>收到后台唤醒信号（如后台任务回调），调用方应交还控制权。</summary>
    Interrupted
}

/// <summary>交互输入等待器 — 轮询检查输入就绪或后台唤醒信号，事件驱动而非超时驱动。</summary>
public sealed class InteractiveInputWaiter {
    private readonly Func<bool> _isInputAvailable;
    private readonly Func<bool> _shouldInterrupt;
    private readonly Func<CancellationToken, Task> _pollDelay;

    /// <summary>构造交互输入等待器。</summary>
    /// <param name="isInputAvailable">输入是否已就绪（非阻塞检查）</param>
    /// <param name="shouldInterrupt">是否收到后台唤醒信号（如存在待处理后通知）</param>
    /// <param name="pollDelay">两次检查之间的等待委托</param>
    public InteractiveInputWaiter(Func<bool> isInputAvailable, Func<bool> shouldInterrupt, Func<CancellationToken, Task> pollDelay) {
        _isInputAvailable = isInputAvailable ?? throw new ArgumentNullException(nameof(isInputAvailable));
        _shouldInterrupt = shouldInterrupt ?? throw new ArgumentNullException(nameof(shouldInterrupt));
        _pollDelay = pollDelay ?? throw new ArgumentNullException(nameof(pollDelay));
    }

    /// <summary>等待输入就绪或后台唤醒信号。</summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>输入就绪或收到中断信号</returns>
    public async Task<InputWaitOutcome> WaitAsync(CancellationToken ct) {
        while (true) {
            ct.ThrowIfCancellationRequested();
            if (_isInputAvailable())
                return InputWaitOutcome.InputReady;
            if (_shouldInterrupt())
                return InputWaitOutcome.Interrupted;
            await _pollDelay(ct).ConfigureAwait(false);
        }
    }
}
