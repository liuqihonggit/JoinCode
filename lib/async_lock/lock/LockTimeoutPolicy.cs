namespace Core.Utils;

/// <summary>
/// 锁超时策略 — <see cref="AsyncLock.TryLockWithRetry"/> 重试 16 次×500ms 全失败后的恢复行为。
/// </summary>
/// <remarks>
/// 生产环境推荐 <see cref="Crash"/> — 锁不可获取说明系统已不安全(如持有者卡在 I/O),继续运行会导致状态不一致。
/// 测试环境推荐 <see cref="Throw"/> — 抛异常便于断言和诊断。
/// 可选操作推荐 <see cref="Degrade"/> — 跳过锁保护操作,降级运行。
/// </remarks>
public enum LockTimeoutPolicy {
    /// <summary>
    /// 重试全失败后抛 <see cref="TimeoutException"/> — 调用方需自行处理,适合测试/诊断场景。
    /// </summary>
    Throw,

    /// <summary>
    /// 重试全失败后调 <see cref="Environment.Exit(int)"/>(99) — 系统不可恢复时立即终止,适合生产场景。
    /// <para>退出前通过 <see cref="AsyncStderrWriter"/> 输出诊断信息并 Flush。</para>
    /// </summary>
    Crash,

    /// <summary>
    /// 重试全失败后返回 null — 调用方跳过锁保护操作(降级),适合可选操作。
    /// </summary>
    Degrade,
}
