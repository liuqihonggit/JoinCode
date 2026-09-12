namespace JoinCode.Abstractions.Security.Shell;

public sealed record BashPreCheckItem(
    BashSecurityCheckId CheckId,
    string NodeType,
    string Message,
    Func<string, bool> IsMatch);

public static class BashPreCheckRegistry
{
    public static readonly BashPreCheckItem ControlCharacters = new(
        BashSecurityCheckId.ControlCharacters,
        "CONTROL_CHAR",
        "命令包含控制字符",
        cmd => BashSecurityRegex.ControlCharRegex().IsMatch(cmd));

    public static readonly BashPreCheckItem UnicodeWhitespace = new(
        BashSecurityCheckId.UnicodeWhitespace,
        "UNICODE_WS",
        "命令包含Unicode空白字符",
        cmd => BashSecurityRegex.UnicodeWhitespaceRegex().IsMatch(cmd));

    public static readonly BashPreCheckItem BackslashEscapedWhitespace = new(
        BashSecurityCheckId.BackslashEscapedWhitespace,
        "BACKSLASH_WS",
        "命令包含反斜杠转义空白",
        cmd => BashSecurityRegex.BackslashWhitespaceRegex().IsMatch(cmd));

    public static readonly BashPreCheckItem ZshTildeBracket = new(
        BashSecurityCheckId.ZshDangerousCommands,
        "ZSH_TILDE_BRACKET",
        "命令包含Zsh动态目录语法 ~[",
        cmd => BashSecurityRegex.ZshTildeBracketRegex().IsMatch(cmd));

    public static readonly BashPreCheckItem ZshEqualsExpansion = new(
        BashSecurityCheckId.ZshDangerousCommands,
        "ZSH_EQUALS_EXPANSION",
        "命令包含Zsh等号展开 =cmd",
        cmd => BashSecurityRegex.ZshEqualsExpansionRegex().IsMatch(cmd));

    public static readonly BashPreCheckItem BraceWithQuote = new(
        BashSecurityCheckId.BraceExpansion,
        "BRACE_WITH_QUOTE",
        "命令包含花括号+引号混淆",
        cmd => BashSecurityRegex.BraceWithQuoteRegex().IsMatch(cmd));

    public static readonly BashPreCheckItem[] All =
    [
        ControlCharacters,
        UnicodeWhitespace,
        BackslashEscapedWhitespace,
        ZshTildeBracket,
        ZshEqualsExpansion,
        BraceWithQuote,
    ];
}
