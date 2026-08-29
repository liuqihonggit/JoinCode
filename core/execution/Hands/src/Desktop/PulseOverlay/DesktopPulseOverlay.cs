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

        PulseNativeMethods.SetLayeredWindowAttributes(_hwnd, (uint)PulseNativeMethods.COLORREF_TRANSPARENT_KEY, 0, PulseNativeMethods.LWA_COLORKEY);
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

        _state.FrameIndex = (_state.FrameIndex + 1) % FrameCount;
        var t = (double)_state.FrameIndex / FrameCount;
        _state.CurrentRadius = _state.MaxRadius - (int)((_state.MaxRadius - _state.MinRadius) * t);

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
            PulseNativeMethods.FillRect(hdc, ref rect, PulseNativeMethods.GetStockObject(NullBrush));

            var r = _state.CurrentRadius;
            var cx = _state.MaxRadius;
            var cy = _state.MaxRadius;

            var hPen = PulseNativeMethods.CreatePen(0, 4, _state.ColorRef);
            var hOldPen = PulseNativeMethods.SelectObject(hdc, hPen);
            var hOldBrush = PulseNativeMethods.SelectObject(hdc, PulseNativeMethods.GetStockObject(NullBrush));

            PulseNativeMethods.Ellipse(hdc, cx - r, cy - r, cx + r, cy + r);

            PulseNativeMethods.SelectObject(hdc, hOldPen);
            PulseNativeMethods.SelectObject(hdc, hOldBrush);
            PulseNativeMethods.DeleteObject(hPen);
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
