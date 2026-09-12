namespace Tools.Handlers;

/// <summary>
/// 桌面输入工具处理器 — 鼠标键盘操作暴露为 MCP 工具
/// </summary>
[McpToolDispatch(ToolCategory.DesktopControl)]
public class DesktopInputToolHandlers
{
    private readonly IDesktopInputService _input;
    private readonly ILogger<DesktopInputToolHandlers>? _logger;

    public DesktopInputToolHandlers(IDesktopInputService input, ILogger<DesktopInputToolHandlers>? logger = null)
    {
        _input = input;
        _logger = logger;
    }

    /// <summary>鼠标点击</summary>
    [McpTool("mouse_click", "在指定屏幕坐标执行鼠标点击", "desktop")]
    public async Task<ToolResult> MouseClickAsync(
        [McpToolParameter("X 坐标（像素）", Required = true)] int x,
        [McpToolParameter("Y 坐标（像素）", Required = true)] int y,
        [McpToolParameter("动作: click/right_click/double_click/middle", Required = false)] string action = "click",
        CancellationToken ct = default)
    {
        var mouseAction = ParseMouseAction(action);
        var op = await _input.ClickAsync(x, y, mouseAction, ct).ConfigureAwait(false);
        return ToolResultBuilder.Success().WithText($"鼠标{action} ({x},{y}): {(op.Succeeded ? "成功" : op.Error)}").Build();
    }

    /// <summary>鼠标移动</summary>
    [McpTool("mouse_move", "移动光标到指定坐标", "desktop")]
    public async Task<ToolResult> MouseMoveAsync(
        [McpToolParameter("X 坐标", Required = true)] int x,
        [McpToolParameter("Y 坐标", Required = true)] int y,
        CancellationToken ct = default)
    {
        var op = await _input.MoveToAsync(x, y, ct).ConfigureAwait(false);
        return ToolResultBuilder.Success().WithText($"移动到 ({x},{y}): {(op.Succeeded ? "成功" : "失败")}").Build();
    }

    /// <summary>鼠标拖拽</summary>
    [McpTool("mouse_drag", "从起点拖拽到终点", "desktop")]
    public async Task<ToolResult> MouseDragAsync(
        [McpToolParameter("起点 X", Required = true)] int fromX,
        [McpToolParameter("起点 Y", Required = true)] int fromY,
        [McpToolParameter("终点 X", Required = true)] int toX,
        [McpToolParameter("终点 Y", Required = true)] int toY,
        [McpToolParameter("终点悬停毫秒（等待弹出）", Required = false)] int? hoverMs = null,
        CancellationToken ct = default)
    {
        var op = await _input.DragAsync(fromX, fromY, toX, toY, hoverMs, ct).ConfigureAwait(false);
        return ToolResultBuilder.Success().WithText($"拖拽 ({fromX},{fromY})→({toX},{toY}): {(op.Succeeded ? "成功" : "失败")}").Build();
    }

    /// <summary>按键</summary>
    [McpTool("key_press", "按下按键（虚拟键码）", "desktop")]
    public async Task<ToolResult> KeyPressAsync(
        [McpToolParameter("Win32 虚拟键码（如 0x0D=回车）", Required = true)] int virtualKey,
        [McpToolParameter("修饰键: none/shift/control/alt/win（可组合用 |）", Required = false)] string modifiers = "none",
        CancellationToken ct = default)
    {
        var mod = ParseKeyModifier(modifiers);
        var op = await _input.KeyPressAsync(virtualKey, mod, ct).ConfigureAwait(false);
        return ToolResultBuilder.Success().WithText($"按键 VK_{virtualKey:X2}+{modifiers}: {(op.Succeeded ? "成功" : "失败")}").Build();
    }

    /// <summary>输入文本</summary>
    [McpTool("type_text", "输入文本（支持 Unicode 中文）", "desktop")]
    public async Task<ToolResult> TypeTextAsync(
        [McpToolParameter("要输入的文本", Required = true)] string text,
        CancellationToken ct = default)
    {
        var op = await _input.TypeTextAsync(text, ct).ConfigureAwait(false);
        return ToolResultBuilder.Success().WithText($"输入文本「{text}」: {(op.Succeeded ? "成功" : "失败")}").Build();
    }

    /// <summary>解析鼠标动作字符串</summary>
    internal static MouseAction ParseMouseAction(string action) => action.ToLowerInvariant() switch
    {
        "click" => MouseAction.Click,
        "right_click" or "rightclick" => MouseAction.RightClick,
        "double_click" or "doubleclick" => MouseAction.DoubleClick,
        "middle" or "middle_click" or "middleclick" => MouseAction.MiddleClick,
        "left_down" or "leftdown" => MouseAction.LeftDown,
        "left_up" or "leftup" => MouseAction.LeftUp,
        _ => MouseAction.Click,
    };

    /// <summary>解析修饰键字符串（支持 shift|control 组合）</summary>
    internal static KeyModifier ParseKeyModifier(string modifiers)
    {
        var result = KeyModifier.None;
        var parts = modifiers.ToLowerInvariant().Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            result |= part switch
            {
                "shift" => KeyModifier.Shift,
                "control" or "ctrl" => KeyModifier.Control,
                "alt" => KeyModifier.Alt,
                "win" or "windows" => KeyModifier.Win,
                _ => KeyModifier.None,
            };
        }
        return result;
    }
}
