namespace JoinCode.Abstractions.Shell;

/// <summary>
/// Shell 进程看护服务 — 检测僵尸进程，通知系统唤醒
/// 周期性检查所有注册的进程是否存活，不存活的触发回调回收资源
/// </summary>
public interface IShellProcessWatchdog : IDisposable
{
    /// <summary>
    /// 注册进程监控 — 进程死亡时触发回调
    /// </summary>
    /// <param name="processId">进程ID</param>
    /// <param name="onProcessDied">进程死亡回调（参数为 processId）</param>
    void Register(int processId, Action<int> onProcessDied);

    /// <summary>
    /// 取消进程监控
    /// </summary>
    void Unregister(int processId);

    /// <summary>
    /// 通知系统从睡眠中恢复 — 触发立即检查所有注册进程
    /// 由平台特定代码调用（如 Windows SystemEvents.PowerModeChanged）
    /// </summary>
    void NotifySystemResumed();
}
