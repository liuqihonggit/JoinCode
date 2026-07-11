namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 前台任务注册表 — 对齐 TS registerForeground/backgroundAll
/// 管理当前前台运行的 Shell 命令上下文，支持 Ctrl+B 后台化
/// </summary>
public interface IForegroundTaskRegistry
{
    /// <summary>
    /// 注册前台任务 — 对齐 TS registerForeground
    /// </summary>
    void Register(IShellCommandContext context);

    /// <summary>
    /// 注销前台任务
    /// </summary>
    void Unregister(string taskId);

    /// <summary>
    /// 后台化所有前台任务 — 对齐 TS backgroundAll
    /// </summary>
    /// <returns>被后台化的任务 ID 列表</returns>
    IReadOnlyList<string> BackgroundAll();

    /// <summary>
    /// 是否有前台任务正在运行 — 对齐 TS hasForegroundTasks
    /// </summary>
    bool HasForegroundTasks { get; }

    /// <summary>
    /// 获取所有前台任务
    /// </summary>
    IReadOnlyList<IShellCommandContext> GetForegroundTasks();
}
