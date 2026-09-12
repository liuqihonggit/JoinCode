namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 窗口矩形坐标
/// </summary>
public readonly record struct WindowRect(int X, int Y, int Width, int Height);

/// <summary>
/// 顶层窗口信息 — EnumWindows 收集结果
/// </summary>
public sealed record WindowInfo(
    IntPtr Handle,
    string Title,
    string? ProcessName,
    WindowRect Rect,
    bool IsVisible);
