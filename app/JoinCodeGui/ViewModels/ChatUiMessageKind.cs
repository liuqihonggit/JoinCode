namespace JoinCode.Gui.ViewModels;

/// <summary>
/// UI 消息展示类型 — 区分思考过程 / 工具调用(含后台agent动作) / 工具结果 / 正文 / 子代理运行组。
/// </summary>
public enum ChatUiMessageKind
{
    Text,
    Thinking,
    ToolCall,
    ToolResult,

    /// <summary>子代理运行组卡片 — 内嵌多 subAgent 运行面板（D2 内嵌组合模型）</summary>
    AgentRunGroup
}