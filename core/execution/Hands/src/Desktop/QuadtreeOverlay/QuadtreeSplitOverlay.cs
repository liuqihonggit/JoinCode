namespace JoinCode.Hands.Desktop.QuadtreeOverlay;

/// <summary>
/// 四叉树分裂动画覆盖层 — 透明无边框顶层窗口 + GDI 绘制
/// 从屏幕边界开始递归四等分,逐层展开,区块半透明填充 + 微细线条 + 颜色从外到内淡化
/// 后台线程独占消息循环,工具线程通过 Close() 发送 WM_CLOSE 通知关闭
/// </summary>
internal sealed class QuadtreeSplitOverlay : IDisposable
{
    private IntPtr _hwnd;
    private string _className = string.Empty;
    private GCHandle _wndProcPin;
    private SplitState _state = new();
    private bool _disposed;

    private const int NullBrush = 5;
    private const uint HighlightFill = 0x0000A5FF;
    private const uint HighlightLine = 0x000064C8;
    private const uint NormalFill = 0x00505050;
    private const uint NormalLine = 0x00808080;

    /// <summary>启动透明窗口 + 消息循环(阻塞当前线程直到窗口关闭)</summary>
    /// <param name="screenW">屏幕宽度</param>
    /// <param name="screenH">屏幕高度</param>
    /// <param name="maxDepth">分裂深度</param>
    /// <param name="durationMs">动画总时长</param>
    /// <param name="frameMs">帧间隔(毫秒)</param>
    /// <param name="baseColor">基础颜色 COLORREF</param>
    /// <param name="highlightRect">高亮格子(可选,鼠标指向识别时用)</param>
    public void Run(int screenW, int screenH, int maxDepth, int durationMs, int frameMs, uint baseColor, QuadtreeRect? highlightRect = null)
    {
        _state = new SplitState
        {
            ScreenW = screenW,
            ScreenH = screenH,
            MaxDepth = maxDepth,
            DurationMs = durationMs,
            BaseColor = baseColor,
            StartTicks = Environment.TickCount64,
            HighlightRect = highlightRect,
        };

        _state.Layers = new List<List<QuadtreeRect>>(maxDepth + 1);
        for (var d = 0; d <= maxDepth; d++)
            _state.Layers.Add(QuadtreeSplitAnimator.GetLayerRects(0, 0, screenW, screenH, d));

        var hInstance = PulseNativeMethods.GetModuleHandle(null);
        _className = "JccQuadtreeSplit_" + Guid.NewGuid().ToString("N")[..8];

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

        _hwnd = PulseNativeMethods.CreateWindowEx(
            PulseNativeMethods.WS_EX_LAYERED | PulseNativeMethods.WS_EX_TOPMOST | PulseNativeMethods.WS_EX_TRANSPARENT | PulseNativeMethods.WS_EX_NOACTIVATE,
            _className, string.Empty,
            PulseNativeMethods.WS_POPUP,
            0, 0, screenW, screenH,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            PulseNativeMethods.UnregisterClass(_className, hInstance);
            return;
        }

        PulseNativeMethods.SetLayeredWindowAttributes(_hwnd, (uint)PulseNativeMethods.COLORREF_TRANSPARENT_KEY, 180, PulseNativeMethods.LWA_COLORKEY | PulseNativeMethods.LWA_ALPHA);
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

    /// <summary>请求关闭窗口(从其他线程调用)</summary>
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

        var layerTime = _state.DurationMs / (_state.MaxDepth + 1);
        _state.CurrentDepth = Math.Min(_state.MaxDepth, (int)(elapsed / layerTime));
        _state.LayerProgress = (double)(elapsed % layerTime) / layerTime;

        PulseNativeMethods.InvalidateRect(hwnd, IntPtr.Zero, true);
    }

    private void HandlePaint(IntPtr hwnd)
    {
        var ps = new PulseOverlay.PAINTSTRUCT();
        var hdc = PulseNativeMethods.BeginPaint(hwnd, ref ps);
        if (hdc == IntPtr.Zero)
            return;

        try
        {
            var rect = new PulseOverlay.RECT { Left = 0, Top = 0, Right = _state.ScreenW, Bottom = _state.ScreenH };

            var hKeyBrush = PulseNativeMethods.CreateSolidBrush((uint)PulseNativeMethods.COLORREF_TRANSPARENT_KEY);
            PulseNativeMethods.FillRect(hdc, ref rect, hKeyBrush);
            PulseNativeMethods.DeleteObject(hKeyBrush);

            for (var d = 0; d <= _state.CurrentDepth; d++)
            {
                DrawLayer(hdc, _state.Layers[d], d, _state.HighlightRect);
            }

            if (_state.CurrentDepth < _state.MaxDepth && _state.LayerProgress > 0.3)
            {
                var splitProgress = (_state.LayerProgress - 0.3) / 0.7;
                DrawSplitLines(hdc, _state.Layers[_state.CurrentDepth], splitProgress);
            }

            if (_state.HighlightRect.HasValue && _state.CurrentDepth >= _state.MaxDepth)
                DrawHighlight(hdc, _state.HighlightRect.Value);
        }
        finally
        {
            PulseNativeMethods.EndPaint(hwnd, ref ps);
        }
    }

    /// <summary>绘制一层:鼠标格子橙色半透明填充,其他格子淡灰色</summary>
    private static void DrawLayer(IntPtr hdc, List<QuadtreeRect> rects, int depth, QuadtreeRect? highlightRect)
    {
        for (var i = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            var isHighlight = highlightRect.HasValue && r == highlightRect.Value;
            var fill = isHighlight ? HighlightFill : NormalFill;
            var line = isHighlight ? HighlightLine : NormalLine;
            var penWidth = isHighlight ? 4 : Math.Max(1, 3 - depth);
            DrawSingleRect(hdc, r, fill, line, penWidth);
        }
    }

    /// <summary>绘制单个格子:半透明填充 + 边框线条</summary>
    private static void DrawSingleRect(IntPtr hdc, QuadtreeRect r, uint fillColor, uint lineColor, int penWidth)
    {
        var hBrush = PulseNativeMethods.CreateSolidBrush(fillColor);
        var fillRect = new PulseOverlay.RECT { Left = r.X, Top = r.Y, Right = r.X + r.Width, Bottom = r.Y + r.Height };
        PulseNativeMethods.FillRect(hdc, ref fillRect, hBrush);
        PulseNativeMethods.DeleteObject(hBrush);

        var hPen = PulseNativeMethods.CreatePen(NativeConstants.PS_SOLID, penWidth, lineColor);
        var oldPen = PulseNativeMethods.SelectObject(hdc, hPen);
        var nullBrush = PulseNativeMethods.GetStockObject(NullBrush);
        var oldBrush = PulseNativeMethods.SelectObject(hdc, nullBrush);

        Gdi32NativeMethods.Rectangle(hdc, r.X, r.Y, r.X + r.Width, r.Y + r.Height);

        PulseNativeMethods.SelectObject(hdc, oldPen);
        PulseNativeMethods.SelectObject(hdc, oldBrush);
        PulseNativeMethods.DeleteObject(hPen);
    }

    /// <summary>绘制分裂分割线 — 从每个父框中心向外延伸,淡灰色</summary>
    private static void DrawSplitLines(IntPtr hdc, List<QuadtreeRect> parents, double progress)
    {
        var hPen = PulseNativeMethods.CreatePen(NativeConstants.PS_SOLID, 1, NormalLine);
        var oldPen = PulseNativeMethods.SelectObject(hdc, hPen);

        foreach (var p in parents)
        {
            var cx = p.X + p.Width / 2;
            var cy = p.Y + p.Height / 2;
            var halfW = (int)(p.Width / 2 * progress);
            var halfH = (int)(p.Height / 2 * progress);

            PulseNativeMethods.MoveToEx(hdc, cx - halfW, cy, IntPtr.Zero);
            PulseNativeMethods.LineTo(hdc, cx + halfW, cy);
            PulseNativeMethods.MoveToEx(hdc, cx, cy - halfH, IntPtr.Zero);
            PulseNativeMethods.LineTo(hdc, cx, cy + halfH);
        }

        PulseNativeMethods.SelectObject(hdc, oldPen);
        PulseNativeMethods.DeleteObject(hPen);
    }

    /// <summary>高亮特定格子(鼠标指向识别时用)</summary>
    private static void DrawHighlight(IntPtr hdc, QuadtreeRect r)
    {
        var hPen = PulseNativeMethods.CreatePen(NativeConstants.PS_SOLID, 5, HighlightLine);
        var oldPen = PulseNativeMethods.SelectObject(hdc, hPen);
        var nullBrush = PulseNativeMethods.GetStockObject(NullBrush);
        var oldBrush = PulseNativeMethods.SelectObject(hdc, nullBrush);

        Gdi32NativeMethods.Rectangle(hdc, r.X, r.Y, r.X + r.Width, r.Y + r.Height);

        PulseNativeMethods.SelectObject(hdc, oldPen);
        PulseNativeMethods.SelectObject(hdc, oldBrush);
        PulseNativeMethods.DeleteObject(hPen);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Close();
    }

    private sealed class SplitState
    {
        public int ScreenW;
        public int ScreenH;
        public int MaxDepth;
        public int DurationMs;
        public uint BaseColor;
        public long StartTicks;
        public int CurrentDepth;
        public double LayerProgress;
        public QuadtreeRect? HighlightRect;
        public List<List<QuadtreeRect>> Layers = [];
    }
}
