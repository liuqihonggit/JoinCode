namespace Tools.Handlers;

/// <summary>
/// 桌面覆盖层工具处理器 — 在桌面上绘制高亮框标注区域
/// 用 GDI 在桌面 DC 画框，超时自动清除，供 LLM 向用户实际标注桌面位置
/// </summary>
[McpToolDispatch(ToolCategory.DesktopControl)]
public class DesktopOverlayToolHandlers
{
    private readonly IScreenCaptureService _capture;
    private readonly ILogger<DesktopOverlayToolHandlers>? _logger;

    public DesktopOverlayToolHandlers(
        IScreenCaptureService capture,
        ILogger<DesktopOverlayToolHandlers>? logger = null)
    {
        _capture = capture;
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

    /// <summary>鼠标指向识别 — 获取鼠标位置,四叉树递归确定范围,截图返回给LLM分析。可选显示四叉树分裂动画</summary>
    [McpTool("look_at_cursor", "获取鼠标位置,四叉树递归截图,让LLM看见鼠标指向的内容。以鼠标所在格子为截图范围,depth控制粒度(depth=0全屏,depth=1四分之一屏)。可选显示四叉树分裂动画。如果截图太小,返回提示建议减小depth重试", "desktop")]
    public async Task<ToolResult> LookAtCursorAsync(
        [McpToolParameter("四叉树深度(0-5),depth=0截全屏,depth=1截1/4屏,默认1", Required = false)] int? depth = 1,
        [McpToolParameter("最小截图像素(宽或高),默认100,小于此值返回提示", Required = false)] int? minPixels = 100,
        [McpToolParameter("是否显示四叉树分裂动画,默认true", Required = false)] bool? showAnimation = true,
        [McpToolParameter("动画时长(毫秒),默认2000", Required = false)] int? animationDurationMs = 2000,
        [McpToolParameter("基础颜色: red/green/blue/yellow/cyan/magenta,默认cyan", Required = false)] string baseColor = "cyan",
        CancellationToken ct = default)
    {
        var d = depth ?? 1;
        var minP = minPixels ?? 100;
        var showAnim = showAnimation ?? true;
        var animDuration = animationDurationMs ?? 2000;

        if (d < 0 || d > 5)
            return ToolResultBuilder.Error().WithText("[CUR100] 深度必须在0-5之间").Build();
        if (minP <= 0)
            return ToolResultBuilder.Error().WithText("[CUR101] 最小像素必须为正").Build();

        if (!User32NativeMethods.GetCursorPos(out var pt))
            return ToolResultBuilder.Error().WithText("[CUR102] 无法获取鼠标位置").Build();

        var screenW = User32NativeMethods.GetSystemMetrics(NativeConstants.SM_CXSCREEN);
        var screenH = User32NativeMethods.GetSystemMetrics(NativeConstants.SM_CYSCREEN);
        if (screenW <= 0 || screenH <= 0)
            return ToolResultBuilder.Error().WithText("[CUR103] 无法获取屏幕尺寸").Build();

        var cellW = screenW >> d;
        var cellH = screenH >> d;
        var cellX = (pt.X / Math.Max(1, cellW)) * cellW;
        var cellY = (pt.Y / Math.Max(1, cellH)) * cellH;

        if (cellW < minP || cellH < minP)
            return ToolResultBuilder.Error()
                .WithText($"[CUR104] 截图范围太小({cellW}x{cellH}),小于最小像素{minP}。建议减小depth重试(当前depth={d},尝试depth={Math.Max(0, d - 1)})")
                .Build();

        if (showAnim && d > 0)
        {
            var colorRef = ParseColor(baseColor);
            var highlightRect = new QuadtreeRect(cellX, cellY, cellW, cellH);

            using var overlay = new QuadtreeSplitOverlay();
            var runTask = Task.Run(() => overlay.Run(screenW, screenH, d, animDuration, 33, colorRef, highlightRect), ct);

            try { await Task.Delay(animDuration, ct).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                overlay.Close();
                await runTask.ConfigureAwait(false);
                return ToolResultBuilder.Success().WithText($"鼠标指向识别已取消: 鼠标({pt.X},{pt.Y}) 深度={d}").Build();
            }

            overlay.Close();
            await runTask.ConfigureAwait(false);
        }

        var base64 = await _capture.CaptureRegionAsync(cellX, cellY, cellW, cellH, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(base64))
            return ToolResultBuilder.Error().WithText("[CUR105] 截图失败").Build();

        _logger?.LogInformation("鼠标指向识别: 鼠标({Mx},{My}) 格子({Cx},{Cy}) {W}x{H} 深度={Depth}", pt.X, pt.Y, cellX, cellY, cellW, cellH, d);

        var text = $"鼠标位置: ({pt.X},{pt.Y})\n截图范围: ({cellX},{cellY}) {cellW}x{cellH}\n四叉树深度: {d}";
        return ToolResultBuilder.Success().WithImage(base64, "image/png").WithText(text).Build();
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
