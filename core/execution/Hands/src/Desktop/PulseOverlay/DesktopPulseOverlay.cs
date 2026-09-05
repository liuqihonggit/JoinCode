namespace JoinCode.Hands.Desktop.PulseOverlay;

/// <summary>
/// 桌面脉冲圆覆盖层 — 透明无边框顶层窗口 + GDI 半透明圆动画
/// 后台线程独占消息循环，工具线程通过 Close() 发送 WM_CLOSE 通知关闭
/// </summary>
internal sealed class DesktopPulseOverlay : IDisposable
{
    private IntPtr _hwnd;
    private string _className = string.Empty;
    private GCHandle _wndProcPin;
    private PulseState _state = new();
    private bool _disposed;

    private const int FrameCount = 10;
    private const int NullBrush = 5;

    /// <summary>启动透明窗口 + 消息循环（阻塞当前线程直到窗口关闭）</summary>
    public void Run(int centerX, int centerY, int maxRadius, int minRadius, int durationMs, int frameMs, uint colorRef)
    {
        _state = new PulseState
        {
            CenterX = centerX,
            CenterY = centerY,
            MaxRadius = maxRadius,
            MinRadius = minRadius,
            DurationMs = durationMs,
            ColorRef = colorRef,
            FrameIndex = 0,
            StartTicks = Environment.TickCount64,
        };
        _state.CurrentRadius = maxRadius;

        var hInstance = PulseNativeMethods.GetModuleHandle(null);
        _className = "JccPulseOverlay_" + Guid.NewGuid().ToString("N")[..8];

        var wndProc = new PulseNativeMethods.WndProcDelegate(WndProc);
        _wndProcPin = GCHandle.Alloc(wndProc);

        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = wndProc,
            hInstance = hInstance,
            lpszClassName = _className,
            hbrBackground = PulseNativeMethods.GetStockObject(NullBrush),
        };

        var atom = PulseNativeMethods.RegisterClassEx(ref wc);
        if (atom == 0)
            return;

        var winX = centerX - maxRadius;
        var winY = centerY - maxRadius;
        var winSize = maxRadius * 2;

        _hwnd = PulseNativeMethods.CreateWindowEx(
            PulseNativeMethods.WS_EX_LAYERED | PulseNativeMethods.WS_EX_TOPMOST | PulseNativeMethods.WS_EX_TRANSPARENT | PulseNativeMethods.WS_EX_NOACTIVATE,
            _className, string.Empty,
            PulseNativeMethods.WS_POPUP,
            winX, winY, winSize, winSize,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            PulseNativeMethods.UnregisterClass(_className, hInstance);
            return;
        }

        PulseNativeMethods.SetLayeredWindowAttributes(_hwnd, (uint)PulseNativeMethods.COLORREF_TRANSPARENT_KEY, 200, PulseNativeMethods.LWA_COLORKEY | PulseNativeMethods.LWA_ALPHA);
        PulseNativeMethods.ShowWindow(_hwnd, PulseNativeMethods.SW_SHOWNOACTIVATE);
        PulseNativeMethods.SetTimer(_hwnd, (IntPtr)1, (uint)frameMs, IntPtr.Zero);

        while (PulseNativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            PulseNativeMethods.TranslateMessage(ref msg);
            PulseNativeMethods.DispatchMessage(ref msg);
        }

        PulseNativeMethods.UnregisterClass(_className, hInstance);
        _wndProcPin.Free();
    }

    /// <summary>请求关闭窗口（从其他线程调用）</summary>
    public void Close()
    {
        if (_hwnd != IntPtr.Zero)
            PulseNativeMethods.PostMessage(_hwnd, PulseNativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case PulseNativeMethods.WM_TIMER:
                HandleTimer(hwnd);
                return IntPtr.Zero;

            case PulseNativeMethods.WM_PAINT:
                HandlePaint(hwnd);
                return IntPtr.Zero;

            case PulseNativeMethods.WM_CLOSE:
                PulseNativeMethods.DestroyWindow(hwnd);
                return IntPtr.Zero;

            case PulseNativeMethods.WM_DESTROY:
                PulseNativeMethods.PostQuitMessage(0);
                return IntPtr.Zero;

            default:
                return PulseNativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
        }
    }

    private void HandleTimer(IntPtr hwnd)
    {
        var elapsed = Environment.TickCount64 - _state.StartTicks;
        if (elapsed >= _state.DurationMs)
        {
            PulseNativeMethods.PostMessage(hwnd, PulseNativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            return;
        }

        // 非线性动画：余弦缓动实现大→小→大循环，整个时长内完成 2 次往返
        var t = (double)elapsed / _state.DurationMs * 2;
        var wave = (1 + Math.Cos(t * Math.PI)) / 2;
        _state.CurrentRadius = _state.MinRadius + (int)((_state.MaxRadius - _state.MinRadius) * wave);

        PulseNativeMethods.InvalidateRect(hwnd, IntPtr.Zero, true);
    }

    private void HandlePaint(IntPtr hwnd)
    {
        var ps = new PAINTSTRUCT();
        var hdc = PulseNativeMethods.BeginPaint(hwnd, ref ps);
        if (hdc == IntPtr.Zero)
            return;

        try
        {
            var rect = new RECT { Left = 0, Top = 0, Right = _state.MaxRadius * 2, Bottom = _state.MaxRadius * 2 };

            // 用颜色键颜色填充背景，LWA_COLORKEY 会让这部分变透明
            var hKeyBrush = PulseNativeMethods.CreateSolidBrush((uint)PulseNativeMethods.COLORREF_TRANSPARENT_KEY);
            PulseNativeMethods.FillRect(hdc, ref rect, hKeyBrush);
            PulseNativeMethods.DeleteObject(hKeyBrush);

            var r = _state.CurrentRadius;
            var cx = _state.MaxRadius;
            var cy = _state.MaxRadius;

            // 实心圆：用目标颜色填充
            var hBrush = PulseNativeMethods.CreateSolidBrush(_state.ColorRef);
            var hPen = PulseNativeMethods.CreatePen(0, 2, _state.ColorRef);
            var hOldPen = PulseNativeMethods.SelectObject(hdc, hPen);
            var hOldBrush = PulseNativeMethods.SelectObject(hdc, hBrush);

            PulseNativeMethods.Ellipse(hdc, cx - r, cy - r, cx + r, cy + r);

            PulseNativeMethods.SelectObject(hdc, hOldPen);
            PulseNativeMethods.SelectObject(hdc, hOldBrush);
            PulseNativeMethods.DeleteObject(hPen);
            PulseNativeMethods.DeleteObject(hBrush);

            // 十字瞄准星标：固定大小，不随圆缩放，用红色与黄色圆强反差
            const int crossSize = 20;
            const uint crossColor = 0x000000FF; // 红色 COLORREF
            var crossPen = PulseNativeMethods.CreatePen(0, 3, crossColor);
            var oldCrossPen = PulseNativeMethods.SelectObject(hdc, crossPen);
            PulseNativeMethods.MoveToEx(hdc, cx - crossSize, cy, IntPtr.Zero);
            PulseNativeMethods.LineTo(hdc, cx + crossSize, cy);
            PulseNativeMethods.MoveToEx(hdc, cx, cy - crossSize, IntPtr.Zero);
            PulseNativeMethods.LineTo(hdc, cx, cy + crossSize);
            PulseNativeMethods.SelectObject(hdc, oldCrossPen);
            PulseNativeMethods.DeleteObject(crossPen);
        }
        finally
        {
            PulseNativeMethods.EndPaint(hwnd, ref ps);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Close();
    }

    private sealed class PulseState
    {
        public int CenterX;
        public int CenterY;
        public int MaxRadius;
        public int MinRadius;
        public int CurrentRadius;
        public int FrameIndex;
        public int DurationMs;
        public uint ColorRef;
        public long StartTicks;
    }
}
