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

        try
        {
            var hPen = Gdi32NativeMethods.CreatePen(0, 4, colorRef);
            var hBrush = Gdi32NativeMethods.GetStockObject(5);
            var oldPen = Gdi32NativeMethods.SelectObject(hdc, hPen);
            var oldBrush = Gdi32NativeMethods.SelectObject(hdc, hBrush);
            Gdi32NativeMethods.Rectangle(hdc, x, y, x + width, y + height);
            Gdi32NativeMethods.SelectObject(hdc, oldPen);
            Gdi32NativeMethods.SelectObject(hdc, oldBrush);
            Gdi32NativeMethods.DeleteObject(hPen);
        }
        finally
        {
            User32NativeMethods.ReleaseDC(IntPtr.Zero, hdc);
        }

        _logger?.LogInformation("桌面高亮框已绘制: ({X},{Y}) {Width}x{Height} 颜色={Color} 时长={Duration}ms", x, y, width, height, color, durationMs);

        try
        {
            await Task.Delay(durationMs, ct).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            ClearOverlay();
            return ToolResultBuilder.Success().WithText($"桌面高亮框已取消: ({x},{y}) {width}x{height}").Build();
        }

        ClearOverlay();

        return ToolResultBuilder.Success().WithText($"桌面高亮框已显示 {durationMs}ms 后自动清除: ({x},{y}) {width}x{height} 颜色={color}").Build();
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
        [McpToolParameter("最大半径（像素），默认120", Required = false)] int maxRadius = 120,
        [McpToolParameter("最小半径（像素），默认30", Required = false)] int minRadius = 30,
        [McpToolParameter("动画总时长（毫秒），超时自动关闭，默认5000", Required = false)] int durationMs = 5000,
        [McpToolParameter("帧间隔（毫秒），默认33约30fps", Required = false)] int frameMs = 33,
        [McpToolParameter("圆颜色: red/green/blue/yellow/cyan/magenta，默认yellow", Required = false)] string color = "yellow",
        CancellationToken ct = default)
    {
        if (maxRadius <= 0 || minRadius <= 0)
            return ToolResultBuilder.Error().WithText("[OVL200] 半径必须为正").Build();
        if (minRadius >= maxRadius)
            return ToolResultBuilder.Error().WithText("[OVL200] 最小半径必须小于最大半径").Build();
        if (durationMs <= 0)
            return ToolResultBuilder.Error().WithText("[OVL200] 动画时长必须为正").Build();

        var colorRef = ParseColor(color);
        _logger?.LogInformation("启动桌面脉冲圆: 中心({Cx},{Cy}) 半径{MinR}-{MaxR} 颜色={Color} 时长={Duration}ms", centerX, centerY, minRadius, maxRadius, color, durationMs);

        using var overlay = new DesktopPulseOverlay();
        var runTask = Task.Run(() => overlay.Run(centerX, centerY, maxRadius, minRadius, durationMs, frameMs, colorRef), ct);

        try
        {
            await Task.Delay(durationMs, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            overlay.Close();
            await runTask.ConfigureAwait(false);
            return ToolResultBuilder.Success().WithText($"桌面脉冲圆已取消: 中心({centerX},{centerY}) 半径{minRadius}-{maxRadius}").Build();
        }

        overlay.Close();
        await runTask.ConfigureAwait(false);

        return ToolResultBuilder.Success().WithText($"桌面脉冲圆已显示 {durationMs}ms: 中心({centerX},{centerY}) 半径{minRadius}-{maxRadius} 颜色={color}").Build();
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
