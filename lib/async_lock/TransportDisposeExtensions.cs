namespace Core.Utils;

/// <summary>
/// 传输层释放安全扩展 — 吞掉 Dispose 主动 Cancel 后台任务时的 OperationCanceledException。
/// </summary>
internal static class TransportDisposeExtensions
{
    /// <summary>
    /// 安全等待后台任务完成：吞 <see cref="OperationCanceledException"/>（Dispose 主动 Cancel 后台任务 CTS 时的预期异常）。
    /// </summary>
    /// <param name="task">后台任务，可为 null（no-op）。</param>
    public static async Task AwaitBackgroundTaskSafe(this Task? task)
    {
        if (task is null) return;
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }
}
