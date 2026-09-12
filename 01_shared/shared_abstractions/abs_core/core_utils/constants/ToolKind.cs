namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 工具类型枚举 — 区分系统内置/MCP远程/报错时动态注入
/// 对齐 [McpToolDispatch] 特性的 Kind 属性，源码生成器据此决定注入策略
/// </summary>
public enum ToolKind
{
    /// <summary>系统内置工具 — 始终注入系统提示词</summary>
    [EnumValue("system")] System,

    /// <summary>MCP远程工具 — 按分组注入，首次只展示组名</summary>
    [EnumValue("mcp")] Mcp,

    /// <summary>报错时动态注入 — 不出现在首次提示词，仅留函数名；首次报错时自动弹出工具说明</summary>
    [EnumValue("on_error")] OnError,

    /// <summary>斜杠命令 — 用户日常操作，默认不注入 AI 提示词，AI 可通过 tool_search 动态发现</summary>
    [EnumValue("slash")] Slash,
}
