namespace JoinCode.Hands.Desktop.Native;

/// <summary>
/// Win32 桌面操作常量 — SendInput/MOUSEEVENTF/KEYEVENTF/窗口消息/BitBlt 光栅操作
/// </summary>
internal static class NativeConstants
{
    // SendInput 类型
    public const uint INPUT_MOUSE = 0;
    public const uint INPUT_KEYBOARD = 1;

    // MOUSEEVENTF 标志
    public const uint MOUSEEVENTF_MOVE = 0x0001;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    // KEYEVENTF 标志
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;

    // 窗口消息
    public const uint WM_CLOSE = 0x0010;

    // ShowWindow 命令
    public const int SW_RESTORE = 9;
    public const int SW_MINIMIZE = 6;
    public const int SW_MAXIMIZE = 3;
    public const int SW_HIDE = 0;
    public const int SW_SHOW = 5;

    // BitBlt 光栅操作码 — SRCCOPY
    public const int SRCCOPY = 0x00CC0020;

    // 屏幕坐标系缩放（SendInput 绝对坐标 0-65535）
    public const double AbsoluteScale = 65535.0;

    // GetSystemMetrics 索引
    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;

    // GetDIBits 颜色格式
    public const uint DIB_RGB_COLORS = 0;
}
