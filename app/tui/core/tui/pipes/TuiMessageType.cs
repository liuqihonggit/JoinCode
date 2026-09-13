namespace JoinCode.Tui.Pipes;

/// <summary>
/// TUI 消息类型 — 对应输出区不同渲染样式。
/// </summary>
public enum TuiMessageType
{
    /// <summary>用户输入消息</summary>
    [EnumValue("user")] User,
    /// <summary>Agent 思考过程（灰色斜体）</summary>
    [EnumValue("agent_thinking")] AgentThinking,
    /// <summary>Agent 正文输出（绿色）</summary>
    [EnumValue("agent_content")] AgentContent,
    /// <summary>工具调用（橙色）</summary>
    [EnumValue("tool_call")] ToolCall,
    /// <summary>工具返回（蓝色）</summary>
    [EnumValue("tool_result")] ToolResult,
    /// <summary>子代理状态卡片（紫色，可点击展开）</summary>
    [EnumValue("sub_agent_card")] SubAgentCard,
    /// <summary>警告（黄色）</summary>
    [EnumValue("warning")] Warning,
    /// <summary>错误（红色）</summary>
    [EnumValue("error")] Error,
    /// <summary>系统分隔线</summary>
    [EnumValue("separator")] Separator,
}
