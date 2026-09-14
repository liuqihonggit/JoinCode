namespace JoinCode.CliCommands;

/// <summary>
/// mcp_call 元命令 — 调用 MCP 工具。
/// <para>统一 handler 签名: Task&lt;int?&gt; ExecuteAsync(string[] args, CancellationToken ct)。</para>
/// </summary>
internal static class McpCallCommand
{
    /// <summary>
    /// 执行 mcp_call — 位置参数 &lt;tool&gt; [key=value ... | &lt;argsJson&gt; | --args-file | --args-stdin] [--json]。
    /// </summary>
    public static async Task<int?> ExecuteAsync(string[] args, CancellationToken ct)
    {
        var toolName = FlatSubCommandRouter.GetPositional(args, 0);
        if (string.IsNullOrEmpty(toolName))
        {
            TerminalHelper.WriteError("用法: jcc mcp_call <tool> [key=value ... | <argsJson> | --args-file <path> | --args-stdin] [--json]");
            return 1;
        }
        var json = FlatSubCommandRouter.ShouldOutputJson(args);
        var argsFile = FlatSubCommandRouter.GetOptionValue(args, ToolCallArgConstants.ArgsFileLongName);
        var argsStdin = FlatSubCommandRouter.HasFlag(args, ToolCallArgConstants.ArgsStdinLongName);
        var vendor = FlatSubCommandRouter.GetOptionValue(args, CliArgConstants.VendorLongName);
        var model = FlatSubCommandRouter.GetOptionValue(args, CliArgConstants.ModelLongName);
        string? argsJson = null;
        string[]? kvArgs = null;
        if (!argsStdin && argsFile is null)
        {
            var allPositional = FlatSubCommandRouter.GetAllPositional(args, 0);
            if (allPositional is { Length: > 0 })
            {
                if (allPositional.Length > 1 && allPositional[1].StartsWith("{"))
                    argsJson = allPositional[1];
                else if (allPositional.Length > 1)
                    kvArgs = allPositional[1..];
            }
        }
        return await McpCliCommand.ExecuteCallAsync(toolName!, argsJson, kvArgs, argsFile, argsStdin, json, vendor, model, ct).ConfigureAwait(false);
    }
}
