namespace JoinCode.Cli;

/// <summary>
/// Rust 风格 CLI 参数错误格式化 — 显示错误参数、位置指示线(^)、修复建议
/// <para>示例输出:</para>
/// <para>error: 未知选项 '--unknown-flag'</para>
/// <para>  |</para>
/// <para>  | mcp_call gh_pr_checks --unknown-flag value pr_number=201</para>
/// <para>  |                      ^^^^^^^^^^^^^^^ 未知选项</para>
/// <para>  |</para>
/// <para>hint: 可用选项见 jcc --help</para>
/// </summary>
internal static class CliErrorFormatter
{
    /// <summary>
    /// 格式化参数错误 — Rust 风格位置指示，显示完整命令行并用 ^ 指向错误参数
    /// </summary>
    /// <param name="args">原始参数数组（args[0] 通常是子命令名）</param>
    /// <param name="errorArgIndex">错误参数在 args 中的索引</param>
    /// <param name="errorType">错误类型描述（如 "未知选项"）</param>
    /// <param name="errorDetail">错误详情，显示在 ^ 指示线后</param>
    /// <param name="hint">修复建议（可选）</param>
    public static string FormatError(string[] args, int errorArgIndex, string errorType, string errorDetail, string? hint = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"error: {errorType}");
        sb.AppendLine("  |");

        var cmdLine = string.Join(" ", args);
        var pos = 0;
        for (var i = 0; i < errorArgIndex; i++)
            pos += args[i].Length + 1;

        sb.AppendLine($"  | {cmdLine}");
        var arrow = new string(' ', pos) + new string('^', args[errorArgIndex].Length);
        sb.AppendLine($"  | {arrow} {errorDetail}");
        sb.AppendLine("  |");
        if (!string.IsNullOrEmpty(hint))
            sb.AppendLine($"hint: {hint}");
        return sb.ToString();
    }

    /// <summary>
    /// 格式化 key=value 参数错误 — 单 token 级别位置指示
    /// </summary>
    /// <param name="token">格式错误的 key=value token</param>
    /// <param name="errorDetail">错误详情（如 "缺少 '=' 分隔符"）</param>
    /// <param name="hint">修复建议（可选）</param>
    public static string FormatKeyValueError(string token, string errorDetail, string? hint = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("error: 参数格式错误");
        sb.AppendLine("  |");
        sb.AppendLine($"  | {token}");
        var arrow = new string('^', token.Length);
        sb.AppendLine($"  | {arrow} {errorDetail}");
        sb.AppendLine("  |");
        if (!string.IsNullOrEmpty(hint))
            sb.AppendLine($"hint: {hint}");
        return sb.ToString();
    }
}
