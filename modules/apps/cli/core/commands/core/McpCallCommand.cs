namespace JoinCode.CliCommands;

/// <summary>
/// mcp_call 元命令 — 调用 MCP 工具。
/// <para>统一 handler 签名: Task&lt;int?&gt; ExecuteAsync(string[] args, CancellationToken ct)。</para>
/// </summary>
internal static class McpCallCommand {
    /// <summary>
    /// 执行 mcp_call — 位置参数 &lt;tool&gt; [key=value ... | &lt;argsJson&gt; | --args-file | --args-stdin] [--json]。
    /// <para>另支持 <c>--参数 值</c> 形式的工具参数（与 <c>jcc tool call</c> 的约定一致）：
    /// 未被元命令占用的 <c>--xxx</c> 一律视为工具参数名，其值取下一个 token。
    /// 参数名是否合法改由宿主内对着工具参数表判定 —— 那时才知道该工具真实有哪些参数，
    /// 比在 CLI 层一刀切拒绝要准确。</para>
    /// </summary>

    public static async Task<int?> ExecuteAsync(string[] args, CancellationToken ct) {
        var reservedOptions = CliArgCliOptionConstants.AllOptionNames
            .Union(ToolCallArgCliOptionConstants.AllOptionNames)
            .ToFrozenSet(StringComparer.Ordinal);
        var reservedBooleanFlags = CliArgCliOptionConstants.BooleanFlags
            .Union(ToolCallArgCliOptionConstants.BooleanFlags)
            .ToFrozenSet(StringComparer.Ordinal);
        var (flagArgs, remaining) = McpToolArgumentBinder.ExtractFlagArguments(
            args, startIndex: 1, reservedOptions, reservedBooleanFlags);

        var toolName = FlatSubCommandRouter.GetPositional(remaining, 0);
        if (string.IsNullOrEmpty(toolName)) {
            TerminalHelper.WriteError("用法: jcc mcp_call <tool> [key=value ... | --参数 值 ... | <argsJson> | --args-file <path> | --args-stdin] [--json]");
            return 1;
        }
        var json = FlatSubCommandRouter.ShouldOutputJson(remaining);
        var argsFile = FlatSubCommandRouter.GetOptionValue(remaining, ToolCallArgCliOptionConstants.ArgsFileLongName);
        var argsStdin = FlatSubCommandRouter.HasFlag(remaining, ToolCallArgCliOptionConstants.ArgsStdinLongName);
        var vendor = FlatSubCommandRouter.GetOptionValue(remaining, CliArgCliOptionConstants.VendorLongName);
        var model = FlatSubCommandRouter.GetOptionValue(remaining, CliArgCliOptionConstants.ModelLongName);
        string? argsJson = null;
        string[]? kvArgs = null;
        if (!argsStdin && argsFile is null) {
            var allPositional = FlatSubCommandRouter.GetAllPositional(remaining, 0);
            if (allPositional is { Length: > 1 }) {
                argsJson = allPositional[1].StartsWith("{") ? allPositional[1] : null;
                kvArgs = argsJson is null ? allPositional[1..] : null;
            }
        }
        return await McpCliCommand.ExecuteCallAsync(toolName, argsJson, kvArgs, flagArgs, argsFile, argsStdin, json, vendor, model, ct).ConfigureAwait(false);
    }
}
