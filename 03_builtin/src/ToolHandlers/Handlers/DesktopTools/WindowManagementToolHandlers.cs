namespace Tools.Handlers;

/// <summary>
/// 窗口管理与截图工具处理器 — 暴露为 MCP 工具
/// </summary>
[McpToolDispatch(ToolCategory.DesktopControl)]
public class WindowManagementToolHandlers
{
    private readonly IWindowManagementService _windows;
    private readonly IScreenCaptureService _capture;
    private readonly ILogger<WindowManagementToolHandlers>? _logger;

    public WindowManagementToolHandlers(
        IWindowManagementService windows,
        IScreenCaptureService capture,
        ILogger<WindowManagementToolHandlers>? logger = null)
    {
        _windows = windows;
        _capture = capture;
        _logger = logger;
    }

    /// <summary>枚举所有可见窗口</summary>
    [McpTool("list_windows", "枚举所有可见顶层窗口", "desktop")]
    public async Task<ToolResult> ListWindowsAsync(CancellationToken ct = default)
    {
        var windows = await _windows.EnumerateAsync(ct).ConfigureAwait(false);
        var sb = new StringBuilder(256);
        sb.AppendLine($"共 {windows.Count} 个可见窗口:");
        foreach (var w in windows)
        {
            sb.AppendLine($"  [{w.ProcessName ?? "?"}] {w.Title} @ ({w.Rect.X},{w.Rect.Y}) {w.Rect.Width}x{w.Rect.Height}");
        }
        return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
    }

    /// <summary>激活窗口</summary>
    [McpTool("focus_window", "按标题或进程名激活窗口到前台", "desktop")]
    public async Task<ToolResult> FocusWindowAsync(
        [McpToolParameter("窗口标题或进程名（模糊匹配）", Required = true)] string title,
        CancellationToken ct = default)
    {
        var window = await _windows.FindAsync(title, ct).ConfigureAwait(false);
        if (window is null)
            return ToolResultBuilder.Error().WithText($"未找到窗口: {title}").Build();

        var ok = await _windows.FocusAsync(window.Handle, ct).ConfigureAwait(false);
        return ToolResultBuilder.Success().WithText($"激活窗口「{window.Title}」: {(ok ? "成功" : "失败")}").Build();
    }

    /// <summary>移动窗口</summary>
    [McpTool("move_window", "移动并调整窗口大小", "desktop")]
    public async Task<ToolResult> MoveWindowAsync(
        [McpToolParameter("窗口标题或进程名", Required = true)] string title,
        [McpToolParameter("X 坐标", Required = true)] int x,
        [McpToolParameter("Y 坐标", Required = true)] int y,
        [McpToolParameter("宽度", Required = true)] int width,
        [McpToolParameter("高度", Required = true)] int height,
        CancellationToken ct = default)
    {
        var window = await _windows.FindAsync(title, ct).ConfigureAwait(false);
        if (window is null)
            return ToolResultBuilder.Error().WithText($"未找到窗口: {title}").Build();

        var ok = await _windows.MoveAsync(window.Handle, x, y, width, height, ct).ConfigureAwait(false);
        return ToolResultBuilder.Success().WithText($"移动窗口「{window.Title}」到 ({x},{y}) {width}x{height}: {(ok ? "成功" : "失败")}").Build();
    }

    /// <summary>关闭窗口</summary>
    [McpTool("close_window", "关闭指定窗口（发送WM_CLOSE）", "desktop")]
    public async Task<ToolResult> CloseWindowAsync(
        [McpToolParameter("窗口标题或进程名", Required = true)] string title,
        CancellationToken ct = default)
    {
        var window = await _windows.FindAsync(title, ct).ConfigureAwait(false);
        if (window is null)
            return ToolResultBuilder.Error().WithText($"未找到窗口: {title}").Build();

        var ok = await _windows.CloseAsync(window.Handle, ct).ConfigureAwait(false);
        return ToolResultBuilder.Success().WithText($"关闭窗口「{window.Title}」: {(ok ? "成功" : "失败")}").Build();
    }

    /// <summary>截图</summary>
    [McpTool("screenshot", "截取屏幕/窗口/区域，返回 base64 PNG", "desktop")]
    public async Task<ToolResult> ScreenshotAsync(
        [McpToolParameter("范围: screen/window/region", Required = false)] string scope = "screen",
        [McpToolParameter("窗口标题（scope=window 时使用）", Required = false)] string? title = null,
        [McpToolParameter("区域 X（scope=region 时使用）", Required = false)] int x = 0,
        [McpToolParameter("区域 Y", Required = false)] int y = 0,
        [McpToolParameter("区域宽度", Required = false)] int width = 0,
        [McpToolParameter("区域高度", Required = false)] int height = 0,
        CancellationToken ct = default)
    {
        string base64;
        if (scope == "window" && title is not null)
        {
            var window = await _windows.FindAsync(title, ct).ConfigureAwait(false);
            if (window is null)
                return ToolResultBuilder.Error().WithText($"未找到窗口: {title}").Build();
            base64 = await _capture.CaptureWindowAsync(window.Handle, ct).ConfigureAwait(false);
        }
        else if (scope == "region")
        {
            base64 = await _capture.CaptureRegionAsync(x, y, width, height, ct).ConfigureAwait(false);
        }
        else
        {
            base64 = await _capture.CaptureFullScreenAsync(ct).ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(base64))
            return ToolResultBuilder.Error().WithText("截图失败").Build();

        return ToolResultBuilder.Success().WithImage(base64, "image/png").Build();
    }
}
