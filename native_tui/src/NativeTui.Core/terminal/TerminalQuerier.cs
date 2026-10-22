namespace NativeTui.Core.Terminal;

/// <summary>
/// 终端查询路由器 — 基准 ink/terminal-querier.ts + parse-keypress.ts:484-487 路由语义：
/// 发送探测查询序列（DA1/DECRQM/kitty flags/DSR/XTVERSION…）→ 注册 pending 等待 →
/// 回复经 Route 命中同 Kind 时 resolve（一次性条目 — Route 即移除，晚到回复不误 resolve 后续等待）。
/// 超时兜底：无回复时任务完成得 null（探测不可靠时无害降级 — 对齐基准：SSH 环境探测失败走保守回退）。
/// 输出经注入 TextWriter（宿主接渲染流；测试注入捕获）。
/// </summary>
public sealed class TerminalQuerier(TextWriter output) : IDisposable
{
    private readonly Dictionary<TerminalResponseKind, TaskCompletionSource<TerminalResponse>> _pending = new();
    private bool _disposed;

    /// <summary>发送查询序列并注册等待 — 回复由 Route 命中同 Kind resolve；超时完成得 null。</summary>
    public async Task<TerminalResponse?> QueryAsync(string sequence, TerminalResponseKind expected, TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var tcs = new TaskCompletionSource<TerminalResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[expected] = tcs;
        output.Write(sequence);
        output.Flush();

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout)).ConfigureAwait(false);
        if (completed != tcs.Task)
        {
            return null;
        }

        try
        {
            return await tcs.Task.ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            // Dispose 取消路径 — 降级 null（调用方拿探测失败语义，不悬挂不抛）
            return null;
        }
    }

    /// <summary>路由回复到等待方（一次性 — 命中即移除条目并 resolve）；
    /// 无等待方返回 false（静默丢弃）。</summary>
    public bool Route(TerminalResponse response)
    {
        if (!_pending.Remove(response.Kind, out var tcs))
        {
            return false;
        }

        return tcs.TrySetResult(response);
    }

    /// <summary>释放 — 剩余等待方全部超时置空完成（不悬挂调用方）。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var tcs in _pending.Values)
        {
            tcs.TrySetCanceled();
        }

        _pending.Clear();
    }
}
