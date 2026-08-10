namespace JoinCode.Gui.ViewModels;

/// <summary>
/// UI 消息展示类型 — 区分思考过程 / 工具调用(含后台agent动作) / 工具结果 / 正文。
/// </summary>
public enum ChatUiMessageKind
{
    Text,
    Thinking,
    ToolCall,
    ToolResult
}