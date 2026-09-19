namespace JoinCode.Gui.ViewModels;

/// <summary>
/// UI 消息展示类型 — 区分思考过程 / 工具调用(含后台agent动作) / 工具结果 / 正文 / 子代理运行组 / 系统提示词注入。
/// </summary>
public enum ChatUiMessageKind
{
    /// <summary>正文文本</summary>
    [EnumValue("text")]
    Text,
    /// <summary>思考过程</summary>
    [EnumValue("thinking")]
    Thinking,
    /// <summary>工具调用</summary>
    [EnumValue("toolCall")]
    ToolCall,
    /// <summary>工具执行结果</summary>
    [EnumValue("toolResult")]
    ToolResult,

    /// <summary>子代理运行组卡片 — 内嵌多 subAgent 运行面板（D2 内嵌组合模型）</summary>
    [EnumValue("agentRunGroup")]
    AgentRunGroup,

    /// <summary>系统提示词注入卡片 — 标题"系统提示词注入"，内容默认折叠（需求10）</summary>
    [EnumValue("systemPromptInjection")]
    SystemPromptInjection,
}