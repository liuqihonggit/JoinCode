namespace JoinCode.Tui.Rendering;

/// <summary>
/// 消息块渲染器 — 将 TuiMessage 渲染为 Terminal.Gui View（Label）。
/// 根据消息类型添加前缀图标，应用颜色方案。
/// 所有渲染通过此入口，禁止直接创建 Label。
/// </summary>
public static class MessageRenderer
{
    /// <summary>将 TuiMessage 渲染为 Label View。</summary>
    public static View Render(TuiMessage message)
    {
        var (prefix, text) = FormatMessage(message);
        var style = ResolveStyle(message);
        var attribute = ColorMapper.ToAttribute(style);

        var label = new Label
        {
            Text = prefix + text,
            X = 0,
            Width = Dim.Fill(),
            Height = 1,
        };
        label.SetAttribute(attribute);
        return label;
    }

    /// <summary>渲染分隔线。</summary>
    public static View RenderSeparator(int width)
    {
        var line = new string('─', Math.Max(1, width));
        var attribute = ColorMapper.ToAttribute(MessageStyle.Separator);
        var label = new Label
        {
            Text = line,
            X = 0,
            Width = Dim.Fill(),
            Height = 1,
        };
        label.SetAttribute(attribute);
        return label;
    }

    /// <summary>渲染子代理状态卡片（折叠状态）。</summary>
    public static View RenderSubAgentCard(string agentName, JoinCode.Tui.Pipes.AgentState state, bool isExpanded = false)
    {
        var stateIcon = state switch
        {
            JoinCode.Tui.Pipes.AgentState.Running => "● 运行中",
            JoinCode.Tui.Pipes.AgentState.Waiting => "● 等待中",
            JoinCode.Tui.Pipes.AgentState.Completed => "● 已完成",
            JoinCode.Tui.Pipes.AgentState.Error => "● 错误",
            JoinCode.Tui.Pipes.AgentState.Stopped => "● 已停止",
            _ => "● 未知",
        };
        var action = isExpanded ? "[− 折叠]" : "[点击展开 →]";
        var text = $"🧩 [SubAgent] {agentName} ({stateIcon})  {action}";

        var attribute = ColorMapper.ToAttribute(MessageStyle.SubAgentCard);
        var label = new Label
        {
            Text = text,
            X = 0,
            Width = Dim.Fill(),
            Height = 1,
            CanFocus = true,
        };
        label.SetAttribute(attribute);
        return label;
    }

    /// <summary>格式化消息文本，返回 (前缀, 正文)。</summary>
    private static (string prefix, string text) FormatMessage(TuiMessage message)
    {
        var time = message.Timestamp.ToLocalTime().ToString("HH:mm:ss");
        return message.Type switch
        {
            TuiMessageType.User => ("👤 ", $"[User]  {time}\n{message.Content}"),
            TuiMessageType.AgentThinking => ("💭 ", $"Thinking: {message.Content}"),
            TuiMessageType.AgentContent => ("", message.Content),
            TuiMessageType.ToolCall => ("🔧 ", $"调用工具: {message.Content}"),
            TuiMessageType.ToolResult => ("📄 ", $"工具返回: {message.Content}"),
            TuiMessageType.Warning => ("⚠️ ", message.Content),
            TuiMessageType.Error => ("❌ ", message.Content),
            TuiMessageType.Separator => ("", new string('─', 60)),
            _ => ("", message.Content),
        };
    }

    /// <summary>解析消息样式。message.Style 优先，否则按 Type 自动选择。</summary>
    private static MessageStyle ResolveStyle(TuiMessage message)
    {
        if (message.Style is not null) return message.Style;
        return message.Type switch
        {
            TuiMessageType.User => MessageStyle.User,
            TuiMessageType.AgentThinking => MessageStyle.Thinking,
            TuiMessageType.AgentContent => MessageStyle.Content,
            TuiMessageType.ToolCall => MessageStyle.ToolCall,
            TuiMessageType.ToolResult => MessageStyle.ToolResult,
            TuiMessageType.SubAgentCard => MessageStyle.SubAgentCard,
            TuiMessageType.Warning => MessageStyle.Warning,
            TuiMessageType.Error => MessageStyle.Error,
            TuiMessageType.Separator => MessageStyle.Separator,
            _ => MessageStyle.Default,
        };
    }
}
