namespace Tools.Handlers;

/// <summary>
/// 复合操作工具处理器 — 右键菜单链/拖拽悬停等多步原子操作序列（PRD M-04/M-05）
/// </summary>
[McpToolDispatch(ToolCategory.DesktopControl)]
public class CompoundOperationToolHandlers
{
    private readonly IDesktopInputService _input;
    private readonly ILogger<CompoundOperationToolHandlers>? _logger;

    public CompoundOperationToolHandlers(IDesktopInputService input, ILogger<CompoundOperationToolHandlers>? logger = null)
    {
        _input = input;
        _logger = logger;
    }

    /// <summary>右键菜单链（M-05）— 右键唤起→等待菜单渲染→点击菜单项</summary>
    [McpTool("right_click_menu", "右键点击指定坐标,等待菜单渲染后点击菜单项坐标", "desktop")]
    public async Task<ToolResult> RightClickMenuAsync(
        [McpToolParameter("右键目标 X 坐标", Required = true)] int targetX,
        [McpToolParameter("右键目标 Y 坐标", Required = true)] int targetY,
        [McpToolParameter("菜单项 X 坐标", Required = true)] int menuItemX,
        [McpToolParameter("菜单项 Y 坐标", Required = true)] int menuItemY,
        [McpToolParameter("等待菜单渲染毫秒数", Required = false)] int menuRenderDelayMs = 500,
        CancellationToken ct = default)
    {
        var rightClick = await _input.ClickAsync(targetX, targetY, MouseAction.RightClick, ct).ConfigureAwait(false);
        if (!rightClick.Succeeded)
            return ToolResultBuilder.Error().WithText($"右键点击失败: {rightClick.Error}").Build();

        _logger?.LogDebug("右键点击 ({X},{Y}) 成功,等待 {Delay}ms 菜单渲染", targetX, targetY, menuRenderDelayMs);
        await Task.Delay(menuRenderDelayMs, ct).ConfigureAwait(false);

        var leftClick = await _input.ClickAsync(menuItemX, menuItemY, MouseAction.Click, ct).ConfigureAwait(false);
        if (!leftClick.Succeeded)
            return ToolResultBuilder.Error().WithText($"点击菜单项失败: {leftClick.Error}").Build();

        return ToolResultBuilder.Success()
            .WithText($"右键菜单链完成: 右键({targetX},{targetY}) → 等待{menuRenderDelayMs}ms → 点击({menuItemX},{menuItemY})")
            .Build();
    }

    /// <summary>拖拽悬停（M-04）— 按下→移动→悬停等待弹出→松开</summary>
    [McpTool("drag_with_hover", "拖拽到目标位置并悬停等待弹出菜单,然后可选点击弹出项", "desktop")]
    public async Task<ToolResult> DragWithHoverAsync(
        [McpToolParameter("起点 X", Required = true)] int fromX,
        [McpToolParameter("起点 Y", Required = true)] int fromY,
        [McpToolParameter("终点 X", Required = true)] int toX,
        [McpToolParameter("终点 Y", Required = true)] int toY,
        [McpToolParameter("悬停等待毫秒数（等待弹出）", Required = false)] int hoverMs = 800,
        [McpToolParameter("弹出项 X 坐标（不传则仅拖拽悬停）", Required = false)] int? popupItemX = null,
        [McpToolParameter("弹出项 Y 坐标", Required = false)] int? popupItemY = null,
        CancellationToken ct = default)
    {
        var drag = await _input.DragAsync(fromX, fromY, toX, toY, hoverMs, ct).ConfigureAwait(false);
        if (!drag.Succeeded)
            return ToolResultBuilder.Error().WithText($"拖拽失败: {drag.Error}").Build();

        var sb = new StringBuilder(128);
        sb.AppendLine($"拖拽完成: ({fromX},{fromY}) → ({toX},{toY}),悬停 {hoverMs}ms");

        if (popupItemX is not null && popupItemY is not null)
        {
            await Task.Delay(300, ct).ConfigureAwait(false);
            var popupClick = await _input.ClickAsync(popupItemX.Value, popupItemY.Value, MouseAction.Click, ct).ConfigureAwait(false);
            if (popupClick.Succeeded)
                sb.AppendLine($"点击弹出项 ({popupItemX},{popupItemY}) 成功");
            else
                sb.AppendLine($"点击弹出项失败: {popupClick.Error}");
        }

        return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
    }

    /// <summary>多步点击序列 — 按顺序点击多个坐标（如向导式操作）</summary>
    [McpTool("multi_click", "按顺序点击多个坐标,每步之间可配置等待时间", "desktop")]
    public async Task<ToolResult> MultiClickAsync(
        [McpToolParameter("坐标列表,格式:x1,y1;x2,y2;x3,y3", Required = true)] string coordinates,
        [McpToolParameter("每步间隔毫秒数", Required = false)] int stepDelayMs = 500,
        CancellationToken ct = default)
    {
        var points = ParseCoordinateList(coordinates);
        if (points.Count == 0)
            return ToolResultBuilder.Error().WithText("坐标列表解析失败,格式应为 x1,y1;x2,y2").Build();

        var sb = new StringBuilder(128);
        sb.AppendLine($"开始 {points.Count} 步点击序列:");

        for (var i = 0; i < points.Count; i++)
        {
            var (x, y) = points[i];
            var click = await _input.ClickAsync(x, y, MouseAction.Click, ct).ConfigureAwait(false);
            sb.AppendLine($"  [{i + 1}] ({x},{y}): {(click.Succeeded ? "成功" : $"失败({click.Error})")}");

            if (i < points.Count - 1)
                await Task.Delay(stepDelayMs, ct).ConfigureAwait(false);
        }

        return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
    }

    internal static List<(int X, int Y)> ParseCoordinateList(string coordinates)
    {
        var result = new List<(int, int)>();
        var pairs = coordinates.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var pair in pairs)
        {
            var parts = pair.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y))
                result.Add((x, y));
        }

        return result;
    }
}
