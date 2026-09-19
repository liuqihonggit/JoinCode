namespace JoinCode.CliCommands;

/// <summary>
/// mcp_search 元命令 — 搜索 MCP 工具。
/// <para>统一 handler 签名: Task&lt;int?&gt; ExecuteAsync(string[] args, CancellationToken ct)。</para>
/// </summary>
internal static class McpSearchCommand {
    /// <summary>
    /// 执行 mcp_search — &lt;query&gt; [--json]。
    /// </summary>
    public static async Task<int?> ExecuteAsync(string[] args, CancellationToken ct) {
        var query = FlatSubCommandRouter.GetPositional(args, 0);
        if (string.IsNullOrEmpty(query)) {
            TerminalHelper.WriteError("用法: jcc mcp_search <query> [--json]");
            return 1;
        }
        var json = FlatSubCommandRouter.ShouldOutputJson(args);
        return await McpCliCommand.ExecuteSearchAsync(query!, json, ct).ConfigureAwait(false);
    }
}