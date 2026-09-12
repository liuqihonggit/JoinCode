namespace JoinCode.Abstractions.Cmd;

/// <summary>
/// 命令来源 — 斜杠命令或 MCP 工具
/// </summary>
public enum CmdSource
{
    /// <summary>斜杠命令（[ChatCommand] 类级注册）</summary>
    Slash,

    /// <summary>MCP 工具（[McpTool] 方法级注册）</summary>
    Mcp,
}
