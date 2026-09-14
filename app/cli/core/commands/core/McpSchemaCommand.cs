namespace JoinCode.CliCommands;

/// <summary>
/// mcp_schema 元命令 — 查看工具参数 Schema。
/// <para>统一 handler 签名: Task&lt;int?&gt; ExecuteAsync(string[] args, CancellationToken ct)。</para>
/// </summary>
internal static class McpSchemaCommand
{
    /// <summary>
    /// 执行 mcp_schema — &lt;tool&gt; [--json]。
    /// </summary>
    public static async Task<int?> ExecuteAsync(string[] args, CancellationToken ct)
    {
        var toolName = FlatSubCommandRouter.GetPositional(args, 0);
        if (string.IsNullOrEmpty(toolName))
        {
            TerminalHelper.WriteError("用法: jcc mcp_schema <tool> [--json]");
            return 1;
        }
        var json = FlatSubCommandRouter.ShouldOutputJson(args);
        return await McpCliCommand.ExecuteSchemaAsync(toolName!, json, ct).ConfigureAwait(false);
    }
}
