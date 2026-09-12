namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 窗口管理服务 — 枚举/激活/移动/关闭（Win32 EnumWindows 封装）
/// </summary>
public interface IWindowManagementService
{
    /// <summary>枚举所有可见顶层窗口</summary>
    Task<IReadOnlyList<WindowInfo>> EnumerateAsync(CancellationToken cancellationToken = default);

    /// <summary>按标题或进程名查找窗口（模糊匹配，返回第一个命中）</summary>
    Task<WindowInfo?> FindAsync(string titleOrProcessName, CancellationToken cancellationToken = default);

    /// <summary>激活窗口到前台</summary>
    Task<bool> FocusAsync(IntPtr hWnd, CancellationToken cancellationToken = default);

    /// <summary>移动并调整窗口大小</summary>
    Task<bool> MoveAsync(IntPtr hWnd, int x, int y, int width, int height, CancellationToken cancellationToken = default);

    /// <summary>关闭窗口（发送 WM_CLOSE，温和关闭优于 TerminateProcess）</summary>
    Task<bool> CloseAsync(IntPtr hWnd, CancellationToken cancellationToken = default);
}
