namespace Core.Utils;

/// <summary>
/// PipeTo 扩展 — Task 完成后把结果作为消息发给 Actor(Akka 对齐)。
/// <para>fire-and-forget:Task 完成后调 Tell,不阻塞,不等待 Actor 处理。</para>
/// <para>Actor 已释放时静默忽略(Task 晚于 Actor 释放完成)。</para>
/// </summary>
public static class ActorPipeToExtensions {
    /// <summary>
    /// Task 完成后把结果发给 Actor — 成功 Tell 结果,失败/取消忽略(Akka 对齐)。
    /// <para>⚠️ fire-and-forget:不等待 Actor 处理,不传播 Task 异常。</para>
    /// </summary>
    /// <typeparam name="TCommand">命令类型(=Task 结果类型)</typeparam>
    /// <param name="task">异步任务</param>
    /// <param name="target">目标 Actor</param>
    /// <param name="sender">发送者引用(null=无 sender)</param>
    public static void PipeTo<TCommand>(this Task<TCommand> task, IActorTell<TCommand> target, object? sender = null) {
        _ = task.ContinueWith(t => {
            if (t.IsCompletedSuccessfully) {
                try { target.Tell(t.Result, sender); } catch (ObjectDisposedException) { AsyncStderrWriter.Enqueue("[PipeTo] Actor 已释放,忽略"); }
            }
        }, TaskContinuationOptions.ExecuteSynchronously);
    }
}
