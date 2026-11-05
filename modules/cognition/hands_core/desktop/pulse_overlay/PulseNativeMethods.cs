namespace JoinCode.Hands.Desktop.PulseOverlay;

/// <summary>
/// 脉冲覆盖层 Win32 P/Invoke 声明 — 透明窗口创建/消息循环/GDI 绘制
/// </summary>
internal static class PulseNativeMethods {
    public const int WS_POPUP = -2147483648;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int SW_SHOWNOACTIVATE = 4;
    public const int LWA_COLORKEY = 1;
    public const int LWA_ALPHA = 2;
    public const uint WM_PAINT = 0x000F;
    public const uint WM_TIMER = 0x0113;
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_CLOSE = 0x0010;
    public const int COLORREF_TRANSPARENT_KEY = 0x010001;

    public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>注册窗口类。</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    /// <summary>创建扩展窗口。</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowEx(
        int dwExStyle, string lpClassName, string lpWindowName,
        int dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    /// <summary>设置分层窗口属性。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    /// <summary>设置定时器。</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr SetTimer(IntPtr hWnd, IntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);

    /// <summary>销毁定时器。</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool KillTimer(IntPtr hWnd, IntPtr nIDEvent);

    /// <summary>从消息队列获取消息。</summary>
    [DllImport("user32.dll")]
    public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    /// <summary>翻译虚拟键消息为字符消息。</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    /// <summary>分发消息给窗口过程。</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(ref MSG lpMsg);

    /// <summary>调用默认窗口过程。</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProc(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    /// <summary>开始绘制。</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr BeginPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);

    /// <summary>结束绘制。</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);

    /// <summary>销毁窗口。</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    /// <summary>投递退出消息。</summary>
    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int nExitCode);

    /// <summary>注销窗口类。</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    /// <summary>用画刷填充矩形。</summary>
    [DllImport("user32.dll")]
    public static extern int FillRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);

    /// <summary>投递消息到窗口。</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    /// <summary>获取模块句柄。</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    /// <summary>设置窗口显示状态。</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>使窗口矩形区域失效。</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, [MarshalAs(UnmanagedType.Bool)] bool bErase);

    /// <summary>创建画笔。</summary>
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreatePen(int fnPenStyle, int nWidth, uint crColor);

    /// <summary>选入 GDI 对象。</summary>
    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    /// <summary>删除 GDI 对象。</summary>
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr hObject);

    /// <summary>绘制椭圆。</summary>
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Ellipse(IntPtr hdc, int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

    /// <summary>获取库存对象。</summary>
    [DllImport("gdi32.dll")]
    public static extern IntPtr GetStockObject(int fnObject);

    /// <summary>创建实心画刷。</summary>
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateSolidBrush(uint crColor);

    /// <summary>移动当前位置。</summary>
    [DllImport("gdi32.dll")]
    public static extern bool MoveToEx(IntPtr hdc, int x, int y, IntPtr lpPoint);

    /// <summary>从当前位置绘制直线到指定点。</summary>
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool LineTo(IntPtr hdc, int x, int y);

    /// <summary>获取控制台窗口句柄。</summary>
    [DllImport("kernel32.dll")]
    public static extern IntPtr GetConsoleWindow();
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WNDCLASSEX {
    public int cbSize;
    public int style;
    public PulseNativeMethods.WndProcDelegate lpfnWndProc;
    public int cbClsExtra;
    public int cbWndExtra;
    public IntPtr hInstance;
    public IntPtr hIcon;
    public IntPtr hCursor;
    public IntPtr hbrBackground;
    public string lpszMenuName;
    public string lpszClassName;
    public IntPtr hIconSm;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSG {
    public IntPtr hwnd;
    public uint message;
    public IntPtr wParam;
    public IntPtr lParam;
    public uint time;
    public POINT pt;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PAINTSTRUCT {
    public IntPtr hdc;
    public bool fErase;
    public RECT rcPaint;
    public bool fRestore;
    public bool fIncUpdate;
    public int reserved1;
    public int reserved2;
    public int reserved3;
    public int reserved4;
    public int reserved5;
    public int reserved6;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RECT {
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINT {
    public int X;
    public int Y;
}
