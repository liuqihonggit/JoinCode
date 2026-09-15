namespace JoinCode;

/// <summary>
/// mcp_serve 子命令参数 — [CliOption] 由 CliOptionGenerator 自动生成 McpServeArgParser + McpServeArgCliOptionConstants
/// </summary>
public enum McpServeArg
{
    /// <summary>MCP 服务端传输协议 (stdio/http)</summary>
    [CliOption(JccCliArgEnumConstants.Transport, "", "MCP 服务端传输协议 (stdio/http)", AcceptsValue = true, Category = "服务端", Example = "jcc mcp_serve --transport http")]
    Transport,

    /// <summary>MCP 服务端监听端口</summary>
    [CliOption(JccCliArgEnumConstants.Port, "", "MCP 服务端监听端口", AcceptsValue = true, Category = "服务端", Example = "jcc mcp_serve --port 9903")]
    Port,

    /// <summary>MCP 服务端监听主机</summary>
    [CliOption(JccCliArgEnumConstants.Host, "", "MCP 服务端监听主机", AcceptsValue = true, Category = "服务端", Example = "jcc mcp_serve --host localhost")]
    Host,
}
