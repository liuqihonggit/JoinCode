namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 窗口震动服务 — 执行实际的窗口震动/任务栏闪烁操作。
/// 实现位于 <c>kit/hands/desktop/</c>（Win32 API 封装），通过 DI 注入到 MCP 工具。
/// </summary>
public interface IWindowShakeService
{
    /// <summary>
    /// 震动当前进程的控制台窗口 — X 轴阻尼偏移动画。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task ShakeWindowAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 闪烁任务栏图标 — 通过 <c>FlashWindowEx</c> 提醒用户。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task FlashTaskbarAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取可震动窗口的诊断信息 — 句柄、标题、矩形、遍历深度。
    /// </summary>
    /// <returns>窗口诊断信息字符串，供工具返回给用户排查震动不可见问题。</returns>
    string GetWindowInfo();
}
