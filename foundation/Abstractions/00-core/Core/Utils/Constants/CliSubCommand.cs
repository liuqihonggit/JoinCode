namespace JoinCode.Abstractions.Utils;

/// <summary>
/// CLI 入口级子命令 — 源码生成器自动生成 CliSubCommandConstants + CliSubCommandExtensions
/// 适用范围: jcc [tool|agent|code|mcp|schema|remote-control|rc|remote] [子参数]
///
/// 使用示例:
/// - FromValue("tool")           → CliSubCommand.Tool
/// - FromValue("mcp")            → CliSubCommand.Mcp
/// - FromValue("RC")             → CliSubCommand.RemoteControl (OrdinalIgnoreCase)
/// - CliSubCommand.RemoteControl.ToValue() → "remote-control"
/// </summary>
public enum CliSubCommand
{
    /// <summary>MCP 工具管理</summary>
    [EnumValue("tool")] Tool,

    /// <summary>智能体管理</summary>
    [EnumValue("agent")] Agent,

    /// <summary>代码操作</summary>
    [EnumValue("code")] Code,

    /// <summary>MCP 工具调用 — bash 直调内部 MCP 工具（call/list/search/schema）</summary>
    [EnumValue("mcp")] Mcp,

    /// <summary>Schema 自省 — 输出 CLI 参数定义 JSON，供 Agent 动态查询</summary>
    [EnumValue("schema")] Schema,

    /// <summary>远程控制（主名称）</summary>
    [EnumValue("remote-control")] RemoteControl,

    /// <summary>远程控制别名</summary>
    [EnumValue("rc")] Rc,

    /// <summary>远程控制别名</summary>
    [EnumValue("remote")] Remote,
}
