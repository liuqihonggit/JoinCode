namespace JoinCode.Cli.Output;

/// <summary>
/// 输入硬化器 — 防范路径穿越、控制字符、特殊字符注入
/// 对齐架构指南安全设计：输入硬化防幻觉
/// </summary>
public static class InputSanitizer
{
    /// <summary>路径穿越模式 AC 自动机 — 一次扫描检测所有穿越模式。</summary>
    private static readonly AhoCorasick<string> PathTraversalAc = AhoCorasick.Create(
        new[] { "../", "..\\", "..", "%2e%2e", "%2E%2E", "..%2f", "..%5c", "....//", "..;/" },
        ignoreCase: true);

    /// <summary>shell 注入模式 AC 自动机 — 检测 $(()) `${ 等注入模式。</summary>
    private static readonly AhoCorasick<string> ShellInjectionAc = AhoCorasick.Create(
        new[] { "$((", "))", "`", "${", "$(" },
        ignoreCase: false);

    /// <summary>危险控制字符 — 防止终端注入和日志注入</summary>
    private static readonly FrozenSet<char> ControlChars = FrozenSet.Create(
        '\0', '\x01', '\x02', '\x03', '\x04', '\x05', '\x06', '\x07',
        '\x08', '\x0b', '\x0c', '\x0e', '\x0f',
        '\x10', '\x11', '\x12', '\x13', '\x14', '\x15', '\x16', '\x17',
        '\x18', '\x19', '\x1a', '\x1b', '\x1c', '\x1d', '\x1e', '\x1f',
        '\x7f');

    /// <summary>
    /// 检查路径是否包含穿越模式
    /// </summary>
    /// <returns>如果路径安全返回 null，否则返回错误描述</returns>
    public static string? ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "路径不能为空";

        var match = PathTraversalAc.FindFirst(path.AsSpan());
        if (match is not null)
            return $"路径包含非法穿越模式: {match!.Value.Value}";

        return null;
    }

    /// <summary>
    /// 检查输入是否包含控制字符
    /// </summary>
    /// <returns>如果输入安全返回 null，否则返回错误描述</returns>
    public static string? ValidateNoControlChars(string input)
    {
        if (string.IsNullOrEmpty(input))
            return null;

        foreach (var c in input)
        {
            if (ControlChars.Contains(c))
                return $"输入包含非法控制字符: 0x{((int)c):X2}";
        }

        return null;
    }

    /// <summary>
    /// 清理输入 — 移除控制字符，规范化路径分隔符
    /// </summary>
    public static string Sanitize(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var sb = new System.Text.StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (!ControlChars.Contains(c))
                sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 检查命令参数是否包含注入风险
    /// 防范 shell 注入：分号、管道符、反引号、$() 等
    /// </summary>
    public static string? ValidateShellArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
            return null;

        // 检查控制字符
        var controlError = ValidateNoControlChars(argument);
        if (controlError is not null)
            return controlError;

        // 检查明显的 shell 注入模式（仅警告，不阻止 — 因为有些参数合法包含这些字符）
        var match = ShellInjectionAc.FindFirst(argument.AsSpan());
        if (match is not null)
            return $"参数可能包含 shell 注入模式: {match!.Value.Value}";

        return null;
    }
}
