namespace Core.Utils;

/// <summary>
/// 定时调度器接口 — 延迟/重复执行动作(Akka IScheduler 对齐)。
/// </summary>
public interface IScheduler {
    /// <summary>
    /// 延迟执行一次 — 返回 IDisposable 用于取消(using 模式)。
    /// </summary>
    /// <param name="delay">延迟时间</param>
    /// <param name="action">执行动作</param>
    /// <returns>调度句柄,Dispose 取消</returns>
    IDisposable ScheduleOnce(TimeSpan delay, Action action);

    /// <summary>
    /// 重复执行 — 初始延迟后按间隔重复,返回 IDisposable 用于取消。
    /// </summary>
    /// <param name="initialDelay">初始延迟</param>
    /// <param name="interval">重复间隔</param>
    /// <param name="action">执行动作</param>
    /// <returns>调度句柄,Dispose 取消</returns>
    IDisposable ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action);
}

/// <summary>
/// Actor 调度器 — 用 Timer 实现延迟/重复调度(Akka 对齐)。
/// <para>Timer 回调在 ThreadPool 上执行,action 需线程安全。</para>
/// <para>Dispose 取消 Timer,停止后续调度。</para>
/// </summary>
public sealed class ActorScheduler : IScheduler {
    /// <summary>延迟执行一次 — Timer 单次触发(delay, Timeout.Infinite)</summary>
    public IDisposable ScheduleOnce(TimeSpan delay, Action action) {
        ArgumentNullException.ThrowIfNull(action);
        return new Timer(_ => SafeInvoke(action), null, delay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>重复执行 — Timer 周期触发(initialDelay, interval)</summary>
    public IDisposable ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action) {
        ArgumentNullException.ThrowIfNull(action);
        return new Timer(_ => SafeInvoke(action), null, initialDelay, interval);
    }

    private static void SafeInvoke(Action action) {
        try { action(); } catch (Exception ex) { AsyncStderrWriter.Enqueue($"[Scheduler] 调度动作异常忽略: {ex.Message}"); }
    }
}

/// <summary>
/// Scheduler 扩展 — ScheduleTellOnce/ScheduleTellRepeatedly 直接发消息给 Actor(Akka 对齐)。
/// </summary>
public static class SchedulerExtensions {
    /// <summary>
    /// 延迟后发消息给 Actor — Actor 释放时静默忽略。
    /// </summary>
    /// <typeparam name="TCommand">命令类型</typeparam>
    /// <param name="scheduler">调度器</param>
    /// <param name="delay">延迟时间</param>
    /// <param name="target">目标 Actor</param>
    /// <param name="msg">消息</param>
    /// <param name="sender">发送者引用(null=无 sender)</param>
    /// <returns>调度句柄,Dispose 取消</returns>
    public static IDisposable ScheduleTellOnce<TCommand>(this IScheduler scheduler, TimeSpan delay, IActorTell<TCommand> target, TCommand msg, object? sender = null) {
        return scheduler.ScheduleOnce(delay, () => { try { target.Tell(msg, sender); } catch (ObjectDisposedException) { AsyncStderrWriter.Enqueue("[Scheduler] Actor 已释放,忽略"); } });
    }

    /// <summary>
    /// 重复发消息给 Actor — Actor 释放时静默忽略。
    /// </summary>
    /// <typeparam name="TCommand">命令类型</typeparam>
    /// <param name="scheduler">调度器</param>
    /// <param name="initialDelay">初始延迟</param>
    /// <param name="interval">重复间隔</param>
    /// <param name="target">目标 Actor</param>
    /// <param name="msg">消息</param>
    /// <param name="sender">发送者引用(null=无 sender)</param>
    /// <returns>调度句柄,Dispose 取消</returns>
    public static IDisposable ScheduleTellRepeatedly<TCommand>(this IScheduler scheduler, TimeSpan initialDelay, TimeSpan interval, IActorTell<TCommand> target, TCommand msg, object? sender = null) {
        return scheduler.ScheduleRepeatedly(initialDelay, interval, () => { try { target.Tell(msg, sender); } catch (ObjectDisposedException) { AsyncStderrWriter.Enqueue("[Scheduler] Actor 已释放,忽略"); } });
    }
}
