namespace JoinCode.CliCommands;

/// <summary>
/// mcp_serve 元命令 — 启动 MCP 服务端。
/// <para>统一 handler 签名: Task&lt;int?&gt; ExecuteAsync(string[] args, CancellationToken ct)。</para>
/// </summary>
internal static class McpServeCommand
{
    /// <summary>
    /// 执行 mcp_serve — [--transport stdio|http] [--port N] [--host H] [--await N]。
    /// </summary>
    public static async Task<int?> ExecuteAsync(string[] args, CancellationToken ct)
    {
        var transport = FlatSubCommandRouter.GetOptionValue(args, McpServeArgConstants.TransportLongName) ?? "stdio";
        var port = int.TryParse(FlatSubCommandRouter.GetOptionValue(args, McpServeArgConstants.PortLongName), out var p) ? p : 9903;
        var host = FlatSubCommandRouter.GetOptionValue(args, McpServeArgConstants.HostLongName) ?? "localhost";
        var awaitSeconds = int.TryParse(FlatSubCommandRouter.GetOptionValue(args, CliArgConstants.AwaitLongName), out var a) ? a : (int?)null;
        return await McpCliCommand.ExecuteServeAsync(transport, port, host, ct, awaitSeconds).ConfigureAwait(false);
    }
}
