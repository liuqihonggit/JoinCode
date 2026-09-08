namespace JoinCode.CliCommands;

/// <summary>
/// 扁平元动词子命令路由器 — 处理 mcp_call/mcp_list/mcp_schema/mcp_search/mcp_serve/slash_call/slash_list/slash_schema/doctor/rg/gh 等元命令。
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
            case CliSubCommand.Rg:
                return await RgSubCommand.ExecuteAsync(args, ct).ConfigureAwait(false);
            case CliSubCommand.Gh:
                return await GhSubCommand.ExecuteAsync(args, ct).ConfigureAwait(false);
            default:
                return null;
        }
    }

    private static async Task<int?> ExecuteMcpCallAsync(string[] args, CancellationToken ct)
    {
        var unknownError = DetectUnknownOptions(args);
        if (unknownError is not null)
        {
            TerminalHelper.WriteError(unknownError);
            return 1;
        }
        var toolName = GetPositional(args, 0);
        if (string.IsNullOrEmpty(toolName))
        {
            TerminalHelper.WriteError("用法: jcc mcp_call <tool> [key=value ... | <argsJson> | --args-file <path> | --args-stdin] [--json]");
            return 1;
        }
        var json = HasFlag(args, CliArgConstants.JsonLongName);
        var argsFile = GetOptionValue(args, CliArgConstants.ArgsFileLongName);
        var argsStdin = HasFlag(args, CliArgConstants.ArgsStdinLongName);
        var vendor = GetOptionValue(args, CliArgConstants.VendorLongName);
        var model = GetOptionValue(args, CliArgConstants.ModelLongName);
        // 判断参数格式: JSON (以{开头) vs key=value (包含=)
        string? argsJson = null;
        string[]? kvArgs = null;
        if (!argsStdin && argsFile is null)
        {
            var allPositional = GetAllPositional(args, 0);
            if (allPositional is { Length: > 0 })
            {
                // 第一个位置参数是 toolName，跳过；剩余的按格式分发
                if (allPositional.Length > 1 && allPositional[1].StartsWith("{"))
                    argsJson = allPositional[1];
                else if (allPositional.Length > 1)
                    kvArgs = allPositional[1..];
            }
        }
        return await McpCliCommand.ExecuteCallAsync(toolName!, argsJson, kvArgs, argsFile, argsStdin, json, vendor, model, ct).ConfigureAwait(false);
    }

    private static async Task<int?> ExecuteMcpListAsync(string[] args, CancellationToken ct)
    {
        var category = GetOptionValue(args, CliArgConstants.CategoryLongName);
        var json = HasFlag(args, CliArgConstants.JsonLongName);
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
        var json = HasFlag(args, CliArgConstants.JsonLongName);
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
        var json = HasFlag(args, CliArgConstants.JsonLongName);
        return await McpCliCommand.ExecuteSearchAsync(query!, json, ct).ConfigureAwait(false);
    }

    private static async Task<int?> ExecuteMcpServeAsync(string[] args, CancellationToken ct)
    {
        var transport = GetOptionValue(args, CliArgConstants.TransportLongName) ?? "stdio";
        var port = int.TryParse(GetOptionValue(args, CliArgConstants.PortLongName), out var p) ? p : 9903;
        var host = GetOptionValue(args, CliArgConstants.HostLongName) ?? "localhost";
        var awaitSeconds = int.TryParse(GetOptionValue(args, CliArgConstants.AwaitLongName), out var a) ? a : (int?)null;
        return await McpCliCommand.ExecuteServeAsync(transport, port, host, ct, awaitSeconds).ConfigureAwait(false);
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
                if (ShouldConsumeNext(args, i))
                    i++;
                continue;
            }
            if (positionalIndex == index)
                return args[i];
            positionalIndex++;
        }
        return null;
    }

    /// <summary>
    /// 获取从指定索引开始的所有位置参数（跳过 --option 及其值）。
    /// </summary>
    internal static string[]? GetAllPositional(string[] args, int startIndex)
    {
        var result = new List<string>();
        var positionalIndex = 0;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i].StartsWith("--"))
            {
                if (ShouldConsumeNext(args, i))
                    i++;
                continue;
            }
            if (positionalIndex >= startIndex)
                result.Add(args[i]);
            positionalIndex++;
        }
        return result.Count > 0 ? result.ToArray() : null;
    }

    /// <summary>
    /// 判断 --option 是否应吞掉下一个 token 作为其值。
    /// 布尔标志（AcceptsValue=false）不吞值；key=value 形式的 token 永远不被吞（保护 MCP 工具参数）。
    /// 双保险：① CliArgConstants.BooleanFlags 白名单（源码生成器自动维护）② key=value 格式检测
    /// </summary>
    private static bool ShouldConsumeNext(string[] args, int i)
    {
        if (CliArgConstants.BooleanFlags.Contains(args[i]))
            return false;
        if (i + 1 >= args.Length)
            return false;
        if (args[i + 1].StartsWith("--"))
            return false;
        if (IsKeyValuePair(args[i + 1]))
            return false;
        return true;
    }

    /// <summary>
    /// 判断 token 是否为 key=value 形式（= 不在首位和末位）
    /// </summary>
    private static bool IsKeyValuePair(string token)
    {
        var eqIdx = token.IndexOf('=');
        return eqIdx > 0 && eqIdx < token.Length - 1;
    }

    /// <summary>
    /// 检测未知 --flag — Rust 风格报错，不静默吞掉
    /// AllOptionNames 由源码生成器从 [CliOption] 特性自动提取，零双向维护
    /// </summary>
    internal static string? DetectUnknownOptions(string[] args)
    {
        for (var i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--"))
                continue;
            if (CliArgConstants.AllOptionNames.Contains(args[i]))
                continue;
            return CliErrorCatalog.ArgUnknownOption(args[i]).ToRustStyleString(args, i);
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
