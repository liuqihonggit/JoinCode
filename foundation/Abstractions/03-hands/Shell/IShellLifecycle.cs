namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Shell 生命周期管理接口 — 上下文压缩时的链式清理
/// 所有 Shell 相关资源（进程、后台任务、前台注册表）实现此接口
/// 压缩时遍历所有注册项，通过多态调用 CompactAsync/TerminateAsync
/// </summary>
public interface IShellLifecycle : IAsyncDisposable
{
    /// <summary>
    /// 当前生命周期状态
    /// </summary>
    ShellLifecycleState LifecycleState { get; }

    /// <summary>
    /// 上下文压缩时调用 — Running→后台化; Backgrounded→保留但截断输出; Completed→释放
    /// </summary>
    Task CompactAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 强制终止 — 杀进程树、回收内存
    /// </summary>
    Task TerminateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Shell 生命周期状态
/// </summary>
public enum ShellLifecycleState
{
    /// <summary>活跃运行中</summary>
    Active,
    /// <summary>已后台化，仍占用资源</summary>
    Backgrounded,
    /// <summary>已完成，可释放</summary>
    Completed,
    /// <summary>已终止</summary>
    Terminated,
}
