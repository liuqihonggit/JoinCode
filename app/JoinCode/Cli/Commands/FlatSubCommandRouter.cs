namespace JoinCode.CliCommands;

/// <summary>
/// 扁平元动词子命令路由器 — 处理 mcp_call/mcp_list/mcp_schema/mcp_search/mcp_serve/slash_call/slash_list/slash_schema/doctor 等元命令。
/// <para>ADR: 0069 — 扁平元动词风格，位置参数为主，不经过 System.CommandLine 嵌套子命令。</para>
/// </summary>
internal static class FlatSubCommandRouter
{
    /// <summary>
    /// 尝试执行扁平元命令；非元命令返回 null 交还调用方处理。
    /// </summary>
    public static async Task<int?> TryExecuteAsync(CliSubCommand subCommand, string[] args, CancellationToken ct)
    {
        switch (subCommand)
        {
            case CliSubCommand.McpCall:
                return await ExecuteMcpCallAsync(args, ct).ConfigureAwait(false);
            case CliSubCommand.McpList:
                return await ExecuteMcpListAsync(args, ct).ConfigureAwait(false);
            case CliSubCommand.McpSchema:
                return await ExecuteMcpSchemaAsync(args, ct).ConfigureAwait(false);
            case CliSubCommand.McpSearch:
                return await ExecuteMcpSearchAsync(args, ct).ConfigureAwait(false);
            case CliSubCommand.McpServe:
                return await ExecuteMcpServeAsync(args, ct).ConfigureAwait(false);
            case CliSubCommand.SlashCall:
                return await ExecuteSlashCallAsync(args, ct).ConfigureAwait(false);
            case CliSubCommand.SlashList:
                return await ExecuteSlashListAsync(args, ct).ConfigureAwait(false);
            case CliSubCommand.SlashSchema:
                return await ExecuteSlashSchemaAsync(args, ct).ConfigureAwait(false);
            case CliSubCommand.Doctor:
                return await ExecuteDoctorAsync(args, ct).ConfigureAwait(false);
            default:
                return null;
        }
    }

    private static async Task<int?> ExecuteMcpCallAsync(string[] args, CancellationToken ct)
    {
        var toolName = GetPositional(args, 0);
        if (string.IsNullOrEmpty(toolName))
        {
            TerminalHelper.WriteError("用法: jcc mcp_call <tool> <argsJson> [--json] [--args-file <path>] [--args-stdin]");
            return 1;
        }
        var json = HasFlag(args, "--json");
        var argsFile = GetOptionValue(args, "--args-file");
        var argsStdin = HasFlag(args, "--args-stdin");
        var argsJson = !argsStdin && argsFile is null ? GetPositional(args, 1) : null;
        return await McpCliCommand.ExecuteCallAsync(toolName!, argsJson, argsFile, argsStdin, json, ct).ConfigureAwait(false);
    }

    private static async Task<int?> ExecuteMcpListAsync(string[] args, CancellationToken ct)
    {
        var category = GetOptionValue(args, "--category");
        var json = HasFlag(args, "--json");
        return await McpCliCommand.ExecuteListAsync(category, json, ct).ConfigureAwait(false);
    }

    private static async Task<int?> ExecuteMcpSchemaAsync(string[] args, CancellationToken ct)
    {
        var toolName = GetPositional(args, 0);
        if (string.IsNullOrEmpty(toolName))
        {
            TerminalHelper.WriteError("用法: jcc mcp_schema <tool> [--json]");
            return 1;
        }
        var json = HasFlag(args, "--json");
        return await McpCliCommand.ExecuteSchemaAsync(toolName!, json, ct).ConfigureAwait(false);
    }

    private static async Task<int?> ExecuteMcpSearchAsync(string[] args, CancellationToken ct)
    {
        var query = GetPositional(args, 0);
        if (string.IsNullOrEmpty(query))
        {
            TerminalHelper.WriteError("用法: jcc mcp_search <query> [--json]");
            return 1;
        }
        var json = HasFlag(args, "--json");
        return await McpCliCommand.ExecuteSearchAsync(query!, json, ct).ConfigureAwait(false);
    }

    private static async Task<int?> ExecuteMcpServeAsync(string[] args, CancellationToken ct)
    {
        var transport = GetOptionValue(args, "--transport") ?? "stdio";
        var port = int.TryParse(GetOptionValue(args, "--port"), out var p) ? p : 9903;
        var host = GetOptionValue(args, "--host") ?? "localhost";
        return await McpCliCommand.ExecuteServeAsync(transport, port, host, ct).ConfigureAwait(false);
    }

    private static Task<int?> ExecuteSlashCallAsync(string[] args, CancellationToken ct)
        => SlashCallExecutor.ExecuteAsync(args, ct);

    private static Task<int?> ExecuteSlashListAsync(string[] args, CancellationToken ct)
        => SlashListExecutor.ExecuteAsync(args, ct);

    private static Task<int?> ExecuteSlashSchemaAsync(string[] args, CancellationToken ct)
        => SlashSchemaExecutor.ExecuteAsync(args, ct);

    private static async Task<int?> ExecuteDoctorAsync(string[] args, CancellationToken ct)
        => await DoctorSubCommand.ExecuteAsync(args, ct).ConfigureAwait(false);

    internal static string? GetPositional(string[] args, int index)
    {
        var positionalIndex = 0;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i].StartsWith("--"))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    i++;
                continue;
            }
            if (positionalIndex == index)
                return args[i];
            positionalIndex++;
        }
        return null;
    }

    internal static string? GetOptionValue(string[] args, string optionName)
    {
        for (var i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == optionName)
                return args[i + 1];
        }
        return null;
    }

    internal static bool HasFlag(string[] args, string flagName)
        => Array.IndexOf(args, flagName) >= 0;
}
