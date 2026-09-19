namespace JoinCode.CliCommands;

/// <summary>
/// mcp_list 元命令 — 列出所有 MCP 工具。
/// <para>统一 handler 签名: Task&lt;int?&gt; ExecuteAsync(string[] args, CancellationToken ct)。</para>
/// </summary>
internal static class McpListCommand
{
    /// <summary>
    /// 执行 mcp_list — [--category &lt;分类&gt;] [--json]。
    /// </summary>
    public static async Task<int?> ExecuteAsync(string[] args, CancellationToken ct)
    {
        var category = FlatSubCommandRouter.GetOptionValue(args, McpListArgCliOptionConstants.CategoryLongName);
        var json = FlatSubCommandRouter.ShouldOutputJson(args);
        return await McpCliCommand.ExecuteListAsync(category, json, ct).ConfigureAwait(false);
    }
}
