// show_desktop_pulse 的透明分层窗口脉冲动画 — 纯 user32/gdi32，无引擎/DI/状态。
// 健壮性设计（对比引擎原版的跨线程 Task.Run + PostMessage）：
//   主线程直接跑消息循环，WM_TIMER 到 duration 后自动 WM_CLOSE → WM_DESTROY → PostQuitMessage，
//   循环自然退出。没有后台线程、没有跨线程 PostMessage，从根上消除「Close 时窗口还没创建」的竞态。
// 确定性逻辑（ComputeRadius / Validate / Geometry）拆成 internal static，可独立断言。


namespace JoinCode.CliNode.Media;

/// <summary>脉冲动画的纯逻辑 — 与窗口解耦，可确定性验证。</summary>
internal static class PulseLogic {
    /// <summary>余弦缓动半径：t=0→max，t=0.5→min，t=1→max（一个周期内大→小→大）。</summary>
    internal static int ComputeRadius(long elapsedMs, int durationMs, int minRadius, int maxRadius) {
        if (durationMs <= 0) return maxRadius;
        var t = Math.Clamp((double)elapsedMs / durationMs, 0.0, 1.0);
        var wave = (1 + Math.Cos(t * 2 * Math.PI)) / 2; // 一个完整往返
        return minRadius + (int)Math.Round((maxRadius - minRadius) * wave);
    }

    /// <summary>参数校验 — 返回 null 表示合法，否则返回错误原因。</summary>
    internal static string? Validate(int maxRadius, int minRadius, int durationMs, int frameMs) {
        if (maxRadius <= 0 || minRadius <= 0) return "半径必须为正";
        if (minRadius >= maxRadius) return "最小半径必须小于最大半径";
        if (durationMs <= 0) return "动画时长必须为正";
        if (frameMs <= 0) return "帧间隔必须为正";
        return null;
    }

    /// <summary>窗口几何：以中心点为圆心，边长 = maxRadius*2。</summary>
    internal static (int x, int y, int size) Geometry(int centerX, int centerY, int maxRadius) =>
        (centerX - maxRadius, centerY - maxRadius, maxRadius * 2);

    internal static uint ParseColor(string name) => name.ToLowerInvariant() switch {
        "red" => 0x000000FF, "green" => 0x0000FF00, "blue" => 0x00FF0000,
        "cyan" => 0x00FFFF00, "magenta" => 0x00FF00FF, _ => 0x0000FFFF // yellow
    };
}

/// <summary>pulse 专用 Win32 声明（不与 MediaNative 的 Gdi32/User32 重复）。</summary>
internal static class PulseNative {
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int SW_SHOWNOACTIVATE = 4;
    public const int LWA_COLORKEY = 1;
    public const int LWA_ALPHA = 2;
    public const uint WM_PAINT = 0x000F;
    public const uint WM_TIMER = 0x0113;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_DESTROY = 0x0002;
    public const int COLORREF_TRANSPARENT_KEY = 0x010001;
    public const int NullBrush = 5;

    public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassEx(ref WndClassEx lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowEx(int exStyle, string className, string name, int style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte alpha, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr SetTimer(IntPtr hwnd, IntPtr id, uint elapse, IntPtr proc);

    [DllImport("user32.dll")]
    public static extern bool KillTimer(IntPtr hwnd, IntPtr id);

    [DllImport("user32.dll")]
    public static extern int GetMessage(out Msg lpMsg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr BeginPaint(IntPtr hwnd, ref PaintStruct ps);

    [DllImport("user32.dll")]
    public static extern bool EndPaint(IntPtr hwnd, ref PaintStruct ps);

    [DllImport("user32.dll")]
    public static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool UnregisterClass(string className, IntPtr instance);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hwnd, int cmd);

    [DllImport("user32.dll")]
    public static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateSolidBrush(uint color);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreatePen(int style, int width, uint color);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    public static extern bool Ellipse(IntPtr hdc, int left, int top, int right, int bottom);

    // FillRect 属于 user32.dll（不是 gdi32）——实测 gdi32 里无此入口，标错会 EntryPointNotFoundException。
    [DllImport("user32.dll")]
    public static extern int FillRect(IntPtr hdc, ref RECT rect, IntPtr brush);

    [DllImport("gdi32.dll")]
    public static extern bool MoveToEx(IntPtr hdc, int x, int y, IntPtr point);

    [DllImport("gdi32.dll")]
    public static extern bool LineTo(IntPtr hdc, int x, int y);

    [DllImport("gdi32.dll")]
    public static extern IntPtr GetStockObject(int obj);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? name);
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WndClassEx {
    public int cbSize;
    public int style;
    public PulseNative.WndProc lpfnWndProc;
    public int cbClsExtra;
    public int cbWndExtra;
    public IntPtr hInstance;
    public IntPtr hIcon;
    public IntPtr hCursor;
    public IntPtr hbrBackground;
    public string? lpszMenuName;
    public string lpszClassName;
    public IntPtr hIconSm;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Msg {
    public IntPtr hwnd;
    public uint message;
    public IntPtr wParam;
    public IntPtr lParam;
    public uint time;
    public POINT pt;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PaintStruct {
    public IntPtr hdc;
    public bool fErase;
    public RECT rcPaint;
    public bool fRestore;
    public bool fIncUpdate;
    public int reserved1; public int reserved2; public int reserved3; public int reserved4;
    public int reserved5; public int reserved6;
}

/// <summary>透明分层窗口脉冲动画 — 主线程独占消息循环，duration 到期自动退出，无跨线程竞态。</summary>
internal sealed class PulseOverlayWindow : IDisposable {
    private IntPtr _hwnd;
    private string _className = string.Empty;
    private GCHandle _wndProcPin;
    private long _startTicks;
    private int _durationMs, _frameMs, _minRadius, _maxRadius;
    private uint _colorRef;
    private int _radius;
    private bool _disposed;

    /// <summary>创建窗口并跑消息循环，直到 duration 到期自动关闭。阻塞当前线程。</summary>
    public void Run(int centerX, int centerY, int maxRadius, int minRadius, int durationMs, int frameMs, uint colorRef) {
        _durationMs = durationMs; _frameMs = frameMs;
        _minRadius = minRadius; _maxRadius = maxRadius;
        _colorRef = colorRef; _radius = maxRadius;
        _startTicks = Environment.TickCount64;

        var instance = PulseNative.GetModuleHandle(null);
        _className = "JccPulse_" + Guid.NewGuid().ToString("N")[..8];
        var wndProc = new PulseNative.WndProc(WndProc);
        _wndProcPin = GCHandle.Alloc(wndProc);

        var wc = new WndClassEx {
            cbSize = Marshal.SizeOf<WndClassEx>(),
            lpfnWndProc = wndProc,
            hInstance = instance,
            lpszClassName = _className,
            hbrBackground = PulseNative.GetStockObject(PulseNative.NullBrush)
        };
        if (PulseNative.RegisterClassEx(ref wc) == 0) return;

        var (wx, wy, size) = PulseLogic.Geometry(centerX, centerY, maxRadius);
        _hwnd = PulseNative.CreateWindowEx(
            PulseNative.WS_EX_LAYERED | PulseNative.WS_EX_TOPMOST | PulseNative.WS_EX_TRANSPARENT | PulseNative.WS_EX_NOACTIVATE,
            _className, string.Empty, PulseNative.WS_POPUP,
            wx, wy, size, size, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero) {
            PulseNative.UnregisterClass(_className, instance);
            _wndProcPin.Free();
            return;
        }

        PulseNative.SetLayeredWindowAttributes(_hwnd, PulseNative.COLORREF_TRANSPARENT_KEY, 200,
            PulseNative.LWA_COLORKEY | PulseNative.LWA_ALPHA);
        PulseNative.ShowWindow(_hwnd, PulseNative.SW_SHOWNOACTIVATE);
        PulseNative.SetTimer(_hwnd, (IntPtr)1, (uint)frameMs, IntPtr.Zero);

        // 主线程消息循环：WM_TIMER 到期自动发 WM_CLOSE → WM_DESTROY → PostQuitMessage → GetMessage 返回 0
        while (PulseNative.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0) {
            PulseNative.TranslateMessage(ref msg);
            PulseNative.DispatchMessage(ref msg);
        }

        PulseNative.KillTimer(_hwnd, (IntPtr)1);
        PulseNative.UnregisterClass(_className, instance);
        _wndProcPin.Free();
        _hwnd = IntPtr.Zero;
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam) {
        switch (msg) {
            case PulseNative.WM_TIMER: {
                var elapsed = Environment.TickCount64 - _startTicks;
                if (elapsed >= _durationMs) {
                    PulseNative.PostMessage(hwnd, PulseNative.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    return IntPtr.Zero;
                }
                _radius = PulseLogic.ComputeRadius(elapsed, _durationMs, _minRadius, _maxRadius);
                PulseNative.InvalidateRect(hwnd, IntPtr.Zero, true);
                return IntPtr.Zero;
            }
            case PulseNative.WM_PAINT:
                Paint(hwnd);
                return IntPtr.Zero;
            case PulseNative.WM_CLOSE:
                PulseNative.DestroyWindow(hwnd);
                return IntPtr.Zero;
            case PulseNative.WM_DESTROY:
                PulseNative.PostQuitMessage(0);
                return IntPtr.Zero;
            default:
                return PulseNative.DefWindowProc(hwnd, msg, wParam, lParam);
        }
    }

    private void Paint(IntPtr hwnd) {
        var ps = new PaintStruct();
        var hdc = PulseNative.BeginPaint(hwnd, ref ps);
        if (hdc == IntPtr.Zero) return;
        try {
            var rect = new RECT { Left = 0, Top = 0, Right = _maxRadius * 2, Bottom = _maxRadius * 2 };
            var keyBrush = PulseNative.CreateSolidBrush(PulseNative.COLORREF_TRANSPARENT_KEY);
            PulseNative.FillRect(hdc, ref rect, keyBrush);
            PulseNative.DeleteObject(keyBrush);

            var r = _radius;
            var c = _maxRadius;
            var brush = PulseNative.CreateSolidBrush(_colorRef);
            var pen = PulseNative.CreatePen(0, 2, _colorRef);
            var oldPen = PulseNative.SelectObject(hdc, pen);
            var oldBrush = PulseNative.SelectObject(hdc, brush);
            PulseNative.Ellipse(hdc, c - r, c - r, c + r, c + r);
            PulseNative.SelectObject(hdc, oldPen);
            PulseNative.SelectObject(hdc, oldBrush);
            PulseNative.DeleteObject(pen);
            PulseNative.DeleteObject(brush);

            const int cross = 20;
            var crossPen = PulseNative.CreatePen(0, 3, 0x000000FF);
            var oldCross = PulseNative.SelectObject(hdc, crossPen);
            PulseNative.MoveToEx(hdc, c - cross, c, IntPtr.Zero);
            PulseNative.LineTo(hdc, c + cross, c);
            PulseNative.MoveToEx(hdc, c, c - cross, IntPtr.Zero);
            PulseNative.LineTo(hdc, c, c + cross);
            PulseNative.SelectObject(hdc, oldCross);
            PulseNative.DeleteObject(crossPen);
        } finally {
            PulseNative.EndPaint(hwnd, ref ps);
        }
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        if (_hwnd != IntPtr.Zero) PulseNative.PostMessage(_hwnd, PulseNative.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }
}
