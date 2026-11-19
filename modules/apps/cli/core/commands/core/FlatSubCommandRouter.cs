namespace JoinCode.CliCommands;

/// <summary>
/// 扁平元动词子命令的公共支撑 — 位置参数/选项扫描/未知选项检测/输出格式判定。
/// <para>ADR: 0069 — 扁平元动词风格，位置参数为主，不经过 System.CommandLine 嵌套子命令。</para>
/// <para>
/// TASK100 S4：此处的 16 个 <c>case</c> 已删除 —— 命令分发改由节点表
/// （<see cref="CliCommandNodes"/>）驱动，本类只保留各子命令共用的解析工具。
/// </para>
/// </summary>
internal static class FlatSubCommandRouter {

    internal static string? GetPositional(string[] args, int index) {
        var positionalIndex = 0;
        for (var i = 1; i < args.Length; i++) {
            if (args[i].StartsWith("--")) {
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
    internal static string[]? GetAllPositional(string[] args, int startIndex) {
        var result = new List<string>();
        var positionalIndex = 0;
        for (var i = 1; i < args.Length; i++) {
            if (args[i].StartsWith("--")) {
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
    private static bool ShouldConsumeNext(string[] args, int i) {
        if (CliArgCliOptionConstants.BooleanFlags.Contains(args[i])
            || ToolCallArgCliOptionConstants.BooleanFlags.Contains(args[i]))
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
    private static bool IsKeyValuePair(string token) {
        var eqIdx = token.IndexOf('=');
        return eqIdx > 0 && eqIdx < token.Length - 1;
    }

    /// <summary>
    /// 检测未知 --flag — 供各子命令内部调用，传入自己的合法选项集合（全局 + 子命令特有）。
    /// <para>全穿透架构：args[0] 为子命令名，从 args[1] 开始扫描。
    /// 未命中任何 allowedSets 的 --flag 返回 Rust 风格报错，不静默吞掉。</para>
    /// </summary>
    internal static string? DetectUnknownOptions(string[] args, params FrozenSet<string>[] allowedSets) {
        for (var i = 1; i < args.Length; i++) {
            if (!args[i].StartsWith("--"))
                continue;
            var matched = false;
            foreach (var set in allowedSets) {
                if (set.Contains(args[i])) {
                    matched = true;
                    break;
                }
            }
            if (matched)
                continue;
            return CliErrorCatalog.ArgUnknownOption(args[i]).ToRustStyleString(args, i);
        }
        return null;
    }

    internal static string? GetOptionValue(string[] args, string optionName) {
        for (var i = 1; i < args.Length - 1; i++) {
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
    /// <para>全局默认: 环境变量 JCC_OUTPUT_FORMAT=text 时默认纯文本(降 AI token),显式 --format 优先</para>
    /// </summary>
    internal static bool ShouldOutputJson(string[] args) {
        var formatValue = GetOptionValue(args, CliArgCliOptionConstants.FormatLongName);
        if (!string.IsNullOrEmpty(formatValue))
            return !string.Equals(formatValue, "text", StringComparison.OrdinalIgnoreCase);
        var envFormat = Environment.GetEnvironmentVariable("JCC_OUTPUT_FORMAT");
        if (string.Equals(envFormat, "text", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }
}