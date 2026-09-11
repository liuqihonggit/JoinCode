namespace Tools.Handlers;

/// <summary>
/// 桌面覆盖层工具处理器 — 在桌面上绘制高亮框标注区域
/// 用 GDI 在桌面 DC 画框，超时自动清除，供 LLM 向用户实际标注桌面位置
/// </summary>
[McpToolDispatch(ToolCategory.DesktopControl)]
public class DesktopOverlayToolHandlers
{
    private readonly ILogger<DesktopOverlayToolHandlers>? _logger;

    public DesktopOverlayToolHandlers(ILogger<DesktopOverlayToolHandlers>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>在桌面上显示高亮框 — 用 GDI 画框，超时自动清除</summary>
    [McpTool("show_desktop_overlay", "在桌面上显示高亮框标注区域位置。用GDI在桌面画框,超时自动清除。用于向用户实际标注桌面上找到的东西。前置:可用screenshot+detect_ui_elements获取坐标", "desktop")]
    public async Task<ToolResult> ShowDesktopOverlayAsync(
        [McpToolParameter("高亮框左上角X（屏幕坐标）", Required = true)] int x,
        [McpToolParameter("高亮框左上角Y（屏幕坐标）", Required = true)] int y,
        [McpToolParameter("高亮框宽度（像素）", Required = true)] int width,
        [McpToolParameter("高亮框高度（像素）", Required = true)] int height,
        [McpToolParameter("显示时长（毫秒），超时自动清除，默认3000", Required = false)] int durationMs = 3000,
        [McpToolParameter("边框颜色: red/green/blue/yellow/cyan/magenta，默认yellow", Required = false)] string color = "yellow",
        CancellationToken ct = default)
    {
        if (width <= 0 || height <= 0)
            return ToolResultBuilder.Error().WithText("[OVL100] 高亮框尺寸必须为正").Build();
        if (durationMs <= 0)
            return ToolResultBuilder.Error().WithText("[OVL101] 显示时长必须为正").Build();

        var colorRef = ParseColor(color);
        var hdc = User32NativeMethods.GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero)
            return ToolResultBuilder.Error().WithText("[OVL102] 无法获取桌面设备上下文").Build();

        var cancelled = false;
        try
        {
            var hPen = Gdi32NativeMethods.CreatePen(NativeConstants.PS_SOLID, 4, colorRef);
            var hBrush = Gdi32NativeMethods.GetStockObject(NativeConstants.NULL_BRUSH);
            var oldPen = Gdi32NativeMethods.SelectObject(hdc, hPen);
            var oldBrush = Gdi32NativeMethods.SelectObject(hdc, hBrush);

            // 定期重画防止 DWM 合成擦掉(DWM 下 GDI 直接画桌面 DC 非持久,一帧后消失)
            var intervals = Math.Max(1, durationMs / 50);
            for (var i = 0; i < intervals; i++)
            {
                Gdi32NativeMethods.Rectangle(hdc, x, y, x + width, y + height);
                try { await Task.Delay(50, ct).ConfigureAwait(false); }
                catch (TaskCanceledException) { cancelled = true; break; }
            }

            Gdi32NativeMethods.SelectObject(hdc, oldPen);
            Gdi32NativeMethods.SelectObject(hdc, oldBrush);
            Gdi32NativeMethods.DeleteObject(hPen);
        }
        finally
        {
            User32NativeMethods.ReleaseDC(IntPtr.Zero, hdc);
        }

        _logger?.LogInformation("桌面高亮框已绘制: ({X},{Y}) {Width}x{Height} 颜色={Color} 时长={Duration}ms", x, y, width, height, color, durationMs);

        ClearOverlay();

        return cancelled
            ? ToolResultBuilder.Success().WithText($"桌面高亮框已取消: ({x},{y}) {width}x{height}").Build()
            : ToolResultBuilder.Success().WithText($"桌面高亮框已显示 {durationMs}ms 后自动清除: ({x},{y}) {width}x{height} 颜色={color}").Build();
    }

    /// <summary>清除桌面高亮框 — 触发桌面重绘</summary>
    private static void ClearOverlay()
    {
        User32NativeMethods.InvalidateRect(IntPtr.Zero, IntPtr.Zero, true);
        User32NativeMethods.UpdateWindow(IntPtr.Zero);
    }

    /// <summary>在桌面上显示半透明脉冲圆动画 — 透明窗口 + GDI 绘制，从大到小循环收缩，超时自动关闭</summary>
    [McpTool("show_desktop_pulse", "在桌面上显示半透明脉冲圆动画标注目标位置。圆从大到小循环收缩(瞄准镜效果),引导用户视线聚焦。用透明窗口绘制不侵入桌面,超时自动关闭。前置:需先screenshot+detect_ui_elements或quadtree_build获取目标坐标", "desktop")]
    public async Task<ToolResult> ShowDesktopPulseAsync(
        [McpToolParameter("目标中心X（屏幕坐标）", Required = true)] int centerX,
        [McpToolParameter("目标中心Y（屏幕坐标）", Required = true)] int centerY,
        [McpToolParameter("最大半径（像素），默认120", Required = false)] int? maxRadius = 120,
        [McpToolParameter("最小半径（像素），默认30", Required = false)] int? minRadius = 30,
        [McpToolParameter("动画总时长（毫秒），超时自动关闭，默认5000", Required = false)] int? durationMs = 5000,
        [McpToolParameter("帧间隔（毫秒），默认33约30fps", Required = false)] int? frameMs = 33,
        [McpToolParameter("圆颜色: red/green/blue/yellow/cyan/magenta，默认yellow", Required = false)] string color = "yellow",
        CancellationToken ct = default)
    {
        var maxR = maxRadius ?? 120;
        var minR = minRadius ?? 30;
        var duration = durationMs ?? 5000;
        var frame = frameMs ?? 33;

        if (maxR <= 0 || minR <= 0)
            return ToolResultBuilder.Error().WithText("[OVL200] 半径必须为正").Build();
        if (minR >= maxR)
            return ToolResultBuilder.Error().WithText("[OVL200] 最小半径必须小于最大半径").Build();
        if (duration <= 0)
            return ToolResultBuilder.Error().WithText("[OVL200] 动画时长必须为正").Build();

        var colorRef = ParseColor(color);
        _logger?.LogInformation("启动桌面脉冲圆: 中心({Cx},{Cy}) 半径{MinR}-{MaxR} 颜色={Color} 时长={Duration}ms", centerX, centerY, minR, maxR, color, duration);

        using var overlay = new DesktopPulseOverlay();
        var runTask = Task.Run(() => overlay.Run(centerX, centerY, maxR, minR, duration, frame, colorRef), ct);

        try
        {
            await Task.Delay(duration, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            overlay.Close();
            await runTask.ConfigureAwait(false);
            return ToolResultBuilder.Success().WithText($"桌面脉冲圆已取消: 中心({centerX},{centerY}) 半径{minR}-{maxR}").Build();
        }

        overlay.Close();
        await runTask.ConfigureAwait(false);

        return ToolResultBuilder.Success().WithText($"桌面脉冲圆已显示 {duration}ms: 中心({centerX},{centerY}) 半径{minR}-{maxR} 颜色={color}").Build();
    }

    /// <summary>在桌面上显示四叉树分裂动画 — 从屏幕边界开始递归四等分,每层颜色淡化,分裂时分割线从中心展开</summary>
    [McpTool("show_quadtree_split", "在桌面上显示四叉树分裂动画。从屏幕边界开始,递归四等分,每层颜色从外到内淡化,分裂时分割线从中心展开。用于向用户直观展示四叉树的空间划分过程。前置:无需前置,直接锚定屏幕边界", "desktop")]
    public async Task<ToolResult> ShowQuadtreeSplitAsync(
        [McpToolParameter("分裂深度(1-5),默认3", Required = false)] int? maxDepth = 3,
        [McpToolParameter("动画总时长(毫秒),默认8000", Required = false)] int? durationMs = 8000,
        [McpToolParameter("基础颜色: red/green/blue/yellow/cyan/magenta,默认blue", Required = false)] string baseColor = "blue",
        CancellationToken ct = default)
    {
        var depth = maxDepth ?? 3;
        var duration = durationMs ?? 8000;

        if (depth < 1 || depth > 5)
            return ToolResultBuilder.Error().WithText("[OVL300] 分裂深度必须在1-5之间").Build();
        if (duration <= 0)
            return ToolResultBuilder.Error().WithText("[OVL301] 动画时长必须为正").Build();

        var colorRef = ParseColor(baseColor);

        var screenW = User32NativeMethods.GetSystemMetrics(NativeConstants.SM_CXSCREEN);
        var screenH = User32NativeMethods.GetSystemMetrics(NativeConstants.SM_CYSCREEN);
        if (screenW <= 0 || screenH <= 0)
            return ToolResultBuilder.Error().WithText("[OVL302] 无法获取屏幕尺寸").Build();

        var layers = new List<List<QuadtreeRect>>(depth + 1);
        for (var d = 0; d <= depth; d++)
            layers.Add(QuadtreeSplitAnimator.GetLayerRects(0, 0, screenW, screenH, d));

        var hdc = User32NativeMethods.GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero)
            return ToolResultBuilder.Error().WithText("[OVL303] 无法获取桌面设备上下文").Build();

        var cancelled = false;
        try
        {
            var layerTime = duration / (depth + 1);
            var displayedLayers = new List<(List<QuadtreeRect> Rects, uint Color, int LayerDepth)>();

            for (var d = 0; d <= depth; d++)
            {
                var layerColor = QuadtreeSplitAnimator.FadeColor(colorRef, d, depth);
                var rects = layers[d];

                if (d > 0)
                {
                    var parents = layers[d - 1];
                    var animDuration = layerTime / 3;
                    var animStart = Environment.TickCount64;
                    while (true)
                    {
                        var elapsed = Environment.TickCount64 - animStart;
                        if (elapsed >= animDuration) break;
                        var progress = (double)elapsed / animDuration;

                        RedrawAllLayers(hdc, displayedLayers);
                        DrawSplitLines(hdc, parents, progress, layerColor);

                        try { await Task.Delay(50, ct).ConfigureAwait(false); }
                        catch (TaskCanceledException) { cancelled = true; break; }
                    }
                    if (cancelled) break;
                }

                displayedLayers.Add((rects, layerColor, d));

                var displayDuration = d == 0 ? layerTime : layerTime * 2 / 3;
                var displayStart = Environment.TickCount64;
                while (true)
                {
                    var elapsed = Environment.TickCount64 - displayStart;
                    if (elapsed >= displayDuration) break;

                    RedrawAllLayers(hdc, displayedLayers);

                    try { await Task.Delay(50, ct).ConfigureAwait(false); }
                    catch (TaskCanceledException) { cancelled = true; break; }
                }
                if (cancelled) break;
            }
        }
        finally
        {
            User32NativeMethods.ReleaseDC(IntPtr.Zero, hdc);
        }

        ClearOverlay();

        _logger?.LogInformation("四叉树分裂动画: 深度={Depth} 屏幕={W}x{H} 颜色={Color} 时长={Duration}ms", depth, screenW, screenH, baseColor, duration);

        var totalRects = 0;
        for (var i = 0; i <= depth; i++) totalRects += layers[i].Count;

        return cancelled
            ? ToolResultBuilder.Success().WithText($"四叉树分裂动画已取消: 深度={depth} 屏幕={screenW}x{screenH}").Build()
            : ToolResultBuilder.Success().WithText($"四叉树分裂动画已完成: 深度={depth} 屏幕={screenW}x{screenH} 总框数={totalRects} 颜色={baseColor}").Build();
    }

    /// <summary>重画所有已显示的层(防 DWM 合成擦掉)</summary>
    private static void RedrawAllLayers(IntPtr hdc, List<(List<QuadtreeRect> Rects, uint Color, int LayerDepth)> layers)
    {
        foreach (var (rects, color, layerDepth) in layers)
            DrawRectLayer(hdc, rects, color, layerDepth);
    }

    /// <summary>在桌面 DC 上画一层矩形框</summary>
    private static void DrawRectLayer(IntPtr hdc, List<QuadtreeRect> rects, uint colorRef, int depth)
    {
        var penWidth = QuadtreeSplitAnimator.GetPenWidth(depth);
        var hPen = Gdi32NativeMethods.CreatePen(NativeConstants.PS_SOLID, penWidth, colorRef);
        var hBrush = Gdi32NativeMethods.GetStockObject(NativeConstants.NULL_BRUSH);
        var oldPen = Gdi32NativeMethods.SelectObject(hdc, hPen);
        var oldBrush = Gdi32NativeMethods.SelectObject(hdc, hBrush);

        foreach (var r in rects)
            Gdi32NativeMethods.Rectangle(hdc, r.X, r.Y, r.X + r.Width, r.Y + r.Height);

        Gdi32NativeMethods.SelectObject(hdc, oldPen);
        Gdi32NativeMethods.SelectObject(hdc, oldBrush);
        Gdi32NativeMethods.DeleteObject(hPen);
    }

    /// <summary>画分裂分割线 — 从每个父框中心向外延伸,progress=0~1</summary>
    private static void DrawSplitLines(IntPtr hdc, List<QuadtreeRect> parents, double progress, uint colorRef)
    {
        var hPen = Gdi32NativeMethods.CreatePen(NativeConstants.PS_SOLID, 2, colorRef);
        var oldPen = Gdi32NativeMethods.SelectObject(hdc, hPen);

        foreach (var p in parents)
        {
            var cx = p.X + p.Width / 2;
            var cy = p.Y + p.Height / 2;
            var halfW = (int)(p.Width / 2 * progress);
            var halfH = (int)(p.Height / 2 * progress);

            Gdi32NativeMethods.MoveToEx(hdc, cx - halfW, cy, IntPtr.Zero);
            Gdi32NativeMethods.LineTo(hdc, cx + halfW, cy);

            Gdi32NativeMethods.MoveToEx(hdc, cx, cy - halfH, IntPtr.Zero);
            Gdi32NativeMethods.LineTo(hdc, cx, cy + halfH);
        }

        Gdi32NativeMethods.SelectObject(hdc, oldPen);
        Gdi32NativeMethods.DeleteObject(hPen);
    }

    /// <summary>颜色名称 → Win32 COLORREF (0x00BBGGRR)</summary>
    private static uint ParseColor(string color) => color.ToLowerInvariant() switch
    {
        "red" => 0x000000FF,
        "green" => 0x0000FF00,
        "blue" => 0x00FF0000,
        "yellow" => 0x0000FFFF,
        "cyan" => 0x00FFFF00,
        "magenta" => 0x00FF00FF,
        "white" => 0x00FFFFFF,
        "orange" => 0x0000A5FF,
        _ => 0x0000FFFF,
    };
}
