namespace JoinCode.Abstractions.Utils;

/// <summary>
/// CLI 入口级子命令 — 源码生成器自动生成 CliSubCommandConstants + CliSubCommandExtensions
/// 适用范围: jcc [mcp_call|mcp_list|mcp_schema|mcp_search|mcp_serve|slash_call|slash_list|slash_schema|doctor|schema|remote-control|rc|remote] [子参数]
///
/// 使用示例:
/// - FromValue("mcp_call")       → CliSubCommand.McpCall
/// - FromValue("slash_call")     → CliSubCommand.SlashCall
/// - FromValue("RC")             → CliSubCommand.RemoteControl (OrdinalIgnoreCase)
/// - CliSubCommand.RemoteControl.ToValue() → "remote-control"
/// </summary>
public enum CliSubCommand
{
    /// <summary>MCP 工具管理（旧设计，阶段4清理时移除）</summary>
    [EnumValue("tool")] Tool,

    /// <summary>智能体管理（旧设计，阶段4清理时移除）</summary>
    [EnumValue("agent")] Agent,

    /// <summary>代码操作（旧设计，阶段4清理时移除）</summary>
    [EnumValue("code")] Code,

    /// <summary>MCP 子命令（旧设计，由 mcp_call/mcp_list/mcp_schema/mcp_search/mcp_serve 取代）</summary>
    [EnumValue("mcp")] Mcp,

    /// <summary>Schema 自省 — 输出 CLI 参数定义 JSON，供 Agent 动态查询</summary>
    [EnumValue("schema")] Schema,

    /// <summary>远程控制（主名称）</summary>
    [EnumValue("remote-control")] RemoteControl,

    /// <summary>远程控制别名</summary>
    [EnumValue("rc")] Rc,

    /// <summary>远程控制别名</summary>
    [EnumValue("remote")] Remote,

    /// <summary>MCP 工具直调 — jcc mcp_call &lt;tool&gt; &lt;argsJson&gt;</summary>
    [EnumValue("mcp_call")] McpCall,

    /// <summary>MCP 工具列表 — jcc mcp_list [--category &lt;cat&gt;]</summary>
    [EnumValue("mcp_list")] McpList,

    /// <summary>MCP 工具参数 schema — jcc mcp_schema &lt;tool&gt;</summary>
    [EnumValue("mcp_schema")] McpSchema,

    /// <summary>MCP 工具搜索 — jcc mcp_search &lt;query&gt;</summary>
    [EnumValue("mcp_search")] McpSearch,

    /// <summary>MCP 服务端 — jcc mcp_serve [--port 9903]</summary>
    [EnumValue("mcp_serve")] McpServe,

    /// <summary>斜杠命令直调 — jcc slash_call &lt;cmd&gt; &lt;argsJson&gt;</summary>
    [EnumValue("slash_call")] SlashCall,

    /// <summary>斜杠命令列表 — jcc slash_list [--category &lt;cat&gt;]</summary>
    [EnumValue("slash_list")] SlashList,

    /// <summary>斜杠命令参数 schema — jcc slash_schema &lt;cmd&gt;</summary>
    [EnumValue("slash_schema")] SlashSchema,

    /// <summary>医生模式 — jcc doctor [--server] [--port &lt;n&gt;]</summary>
    [EnumValue("doctor")] Doctor,
}
