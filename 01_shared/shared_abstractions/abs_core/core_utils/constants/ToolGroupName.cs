namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 工具分组名称常量 — 用于两阶段工具加载
/// 核心工具发完整 schema，MCP 工具首次只发分组+名称，LLM 通过 ToolSearch 按需加载
/// </summary>
public static class ToolGroupNameConstants
{
    /// <summary>核心工具组 — 系统内置工具（File/Shell/Search 等），始终发送完整 schema</summary>
    public const string CoreTools = "core_tools";

    /// <summary>MCP 工具组 — 远程/斜杠工具，首次只发分组+名称，按需加载完整描述</summary>
    public const string McpTools = "mcp_tools";
}
