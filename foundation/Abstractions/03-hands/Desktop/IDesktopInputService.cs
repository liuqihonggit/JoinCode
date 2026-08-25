namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 桌面输入模拟服务 — 鼠标键盘事件注入（Win32 SendInput 封装）
/// </summary>
public interface IDesktopInputService
{
    /// <summary>移动光标到绝对坐标</summary>
    Task<DesktopOperation> MoveToAsync(int x, int y, CancellationToken cancellationToken = default);

    /// <summary>执行鼠标动作（单击/双击/右键/中键/按下/松开）</summary>
    Task<DesktopOperation> ClickAsync(int x, int y, MouseAction action, CancellationToken cancellationToken = default);

    /// <summary>拖拽：按下→移动到目标→松开，支持中途悬停等待弹出</summary>
    Task<DesktopOperation> DragAsync(int fromX, int fromY, int toX, int toY, int? hoverMsAtTarget = null, CancellationToken cancellationToken = default);

    /// <summary>按键（单键或组合键），virtualKey 为 Win32 虚拟键码</summary>
    Task<DesktopOperation> KeyPressAsync(int virtualKey, KeyModifier modifiers = KeyModifier.None, CancellationToken cancellationToken = default);

    /// <summary>输入文本（Unicode，逐字符 SendInput 注入，支持中文）</summary>
    Task<DesktopOperation> TypeTextAsync(string text, CancellationToken cancellationToken = default);
}
