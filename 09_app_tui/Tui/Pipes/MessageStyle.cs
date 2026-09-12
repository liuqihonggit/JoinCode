namespace JoinCode.Tui.Pipes;

/// <summary>
/// 消息渲染样式 — 驱动 Terminal.Gui 颜色和字体属性。
/// 所有颜色为 RGB 十六进制字符串（如 "#58a6ff"），由渲染层映射到 Terminal.Gui Color。
/// </summary>
public sealed record MessageStyle
{
    /// <summary>前景色 RGB（如 "#58a6ff"）。null 表示使用默认前景色。</summary>
    public string? Foreground { get; init; }

    /// <summary>背景色 RGB。null 表示使用默认背景色。</summary>
    public string? Background { get; init; }

    /// <summary>是否斜体（思考文本用）。</summary>
    public bool Italic { get; init; }

    /// <summary>是否粗体。</summary>
    public bool Bold { get; init; }

    /// <summary>是否半透明（待发送的用户消息用）。</summary>
    public bool Dim { get; init; }

    /// <summary>创建默认样式。</summary>
    public static MessageStyle Default { get; } = new();

    /// <summary>创建用户消息样式（亮蓝 #58a6ff）。</summary>
    public static MessageStyle User { get; } = new() { Foreground = "#58a6ff" };

    /// <summary>创建思考样式（灰色 #8b949e 斜体）。</summary>
    public static MessageStyle Thinking { get; } = new() { Foreground = "#8b949e", Italic = true };

    /// <summary>创建正文样式（亮绿 #7ee787）。</summary>
    public static MessageStyle Content { get; } = new() { Foreground = "#7ee787" };

    /// <summary>创建工具调用样式（橙色 #f0883e）。</summary>
    public static MessageStyle ToolCall { get; } = new() { Foreground = "#f0883e" };

    /// <summary>创建工具返回样式（亮蓝 #58a6ff）。</summary>
    public static MessageStyle ToolResult { get; } = new() { Foreground = "#58a6ff" };

    /// <summary>创建子代理卡片样式（紫色 #bc8cff）。</summary>
    public static MessageStyle SubAgentCard { get; } = new() { Foreground = "#bc8cff" };

    /// <summary>创建警告样式（黄色 #d2c81e）。</summary>
    public static MessageStyle Warning { get; } = new() { Foreground = "#d2c81e" };

    /// <summary>创建错误样式（红色 #f85149）。</summary>
    public static MessageStyle Error { get; } = new() { Foreground = "#f85149" };

    /// <summary>创建分隔线样式（暗灰 #30363d）。</summary>
    public static MessageStyle Separator { get; } = new() { Foreground = "#30363d" };

    /// <summary>创建待发送样式（蓝色半透明）。</summary>
    public static MessageStyle Pending { get; } = new() { Foreground = "#58a6ff", Dim = true };
}
