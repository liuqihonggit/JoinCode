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
        var unknownError = DetectUnknownOptions(args);
        if (unknownError is not null)
        {
            TerminalHelper.WriteError(unknownError);
            return (int)ExitCode.ArgumentParseError;
        }

        switch (subCommand)
        {
            case CliSubCommand.McpCall:
                return await McpCallCommand.ExecuteAsync(args, ct).ConfigureAwait(false);
            case CliSubCommand.McpList:
                return await McpListCommand.ExecuteAsync(args, ct).ConfigureAwait(false);
            case CliSubCommand.McpSchema:
                return await McpSchemaCommand.ExecuteAsync(args, ct).ConfigureAwait(false);
            case CliSubCommand.McpSearch:
                return await McpSearchCommand.ExecuteAsync(args, ct).ConfigureAwait(false);
            case CliSubCommand.McpServe:
                return await McpServeCommand.ExecuteAsync(args, ct).ConfigureAwait(false);
            case CliSubCommand.SlashCall:
                return await SlashCallExecutor.ExecuteAsync(args, ct).ConfigureAwait(false);
            case CliSubCommand.SlashList:
                return await SlashListExecutor.ExecuteAsync(args, ct).ConfigureAwait(false);
            case CliSubCommand.SlashSchema:
                return await SlashSchemaExecutor.ExecuteAsync(args, ct).ConfigureAwait(false);
            case CliSubCommand.Rg:
                return await RgSubCommand.ExecuteAsync(args, ct).ConfigureAwait(false);
            case CliSubCommand.Gh:
                return await GhSubCommand.ExecuteAsync(args, ct).ConfigureAwait(false);
            default:
                return null;
        }
    }

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
    /// 双保险：① 全局+子命令 BooleanFlags 白名单（源码生成器自动维护）② key=value 格式检测
    /// </summary>
    private static bool ShouldConsumeNext(string[] args, int i)
    {
        if (CliArgConstants.BooleanFlags.Contains(args[i])
            || ToolCallArgConstants.BooleanFlags.Contains(args[i]))
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

    /// <summary>
    /// 统一判断是否输出 JSON — 默认 JSON 输出,--format text 显式请求彩色文本。
    /// <para>ADR 0069 决策6 + 统一返回结构: 所有子命令默认输出 JSON(结构化),
    /// --format text 显式请求彩色文本,--json 保持作为别名(默认即 JSON)。</para>
    /// </summary>
    internal static bool ShouldOutputJson(string[] args)
    {
        var formatValue = GetOptionValue(args, CliArgConstants.FormatLongName);
        if (string.Equals(formatValue, "text", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }
}
