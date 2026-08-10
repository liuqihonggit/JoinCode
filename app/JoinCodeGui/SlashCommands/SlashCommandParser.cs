namespace JoinCode.Gui.SlashCommands;

/// <summary>补全模式 — 命令名/命令参数/文件/工具</summary>
public enum SlashCompletionMode
{
    /// <summary>命令名补全（/xxx）</summary>
    Command,

    /// <summary>命令参数补全（/model xxx）</summary>
    Argument,

    /// <summary>文件补全（@path）</summary>
    File,

    /// <summary>工具补全（#tool）</summary>
    Tool
}

/// <summary>
/// 补全光标解析结果 — 描述当前光标位置是否触发补全及查询前缀。
/// </summary>
public readonly record struct SlashParseResult
{
    /// <summary>是否触发补全</summary>
    public bool ShouldComplete { get; init; }

    /// <summary>查询前缀（命令模式含 /，如 "/ap"；文件/工具模式不含触发符）；不触发时为空</summary>
    public string Prefix { get; init; } = string.Empty;

    /// <summary>触发符在文本中的索引（回填替换用）；不触发时为 -1</summary>
    public int SlashIndex { get; init; } = -1;

    /// <summary>前缀结束位置（即光标位置，回填替换区间右边界）；不触发时为 -1</summary>
    public int PrefixEnd { get; init; } = -1;

    /// <summary>补全模式</summary>
    public SlashCompletionMode Mode { get; init; } = SlashCompletionMode.Command;

    /// <summary>触发字符（'/' '@' '#'）</summary>
    public char TriggerChar { get; init; } = '/';

    /// <summary>参数模式下已识别的命令名（如 "/model"）；其他模式为空</summary>
    public string CommandName { get; init; } = string.Empty;

    /// <summary>参数模式下已输入的参数前缀；其他模式为空</summary>
    public string ArgumentPrefix { get; init; } = string.Empty;

    /// <summary>参数替换区间起点（参数模式下回填用）；其他模式为 -1</summary>
    public int ArgumentStart { get; init; } = -1;

    /// <summary>不触发的空结果</summary>
    public static SlashParseResult None => new();

    public SlashParseResult() { }
}

/// <summary>
/// 补全光标解析器 — 从光标位置向前查找最近的触发符（/ @ #），提取查询前缀。
/// / → 命令名补全或命令参数补全；@ → 文件补全；# → 工具补全。
/// </summary>
public static class SlashCommandParser
{
    /// <summary>支持参数补全的命令集</summary>
    private static readonly string[] ArgumentCompletableCommands =
        ["/model", "/theme", "/config", "/effort", "/provider"];

    /// <summary>
    /// 解析文本中光标位置的补全上下文。
    /// </summary>
    /// <param name="text">输入框完整文本</param>
    /// <param name="cursor">光标位置（0 到 text.Length）</param>
    /// <returns>解析结果；不触发时 ShouldComplete=false</returns>
    public static SlashParseResult Parse(string text, int cursor)
    {
        if (string.IsNullOrEmpty(text) || cursor <= 0 || cursor > text.Length)
            return SlashParseResult.None;

        var slice = text.AsSpan(0, cursor);

        var lineStart = slice.LastIndexOf('\n') + 1;
        var lineSlice = slice[lineStart..];

        var slashIdx = lineSlice.LastIndexOf('/');
        var atIdx = lineSlice.LastIndexOf('@');
        var hashIdx = lineSlice.LastIndexOf('#');

        var triggerIdx = Math.Max(slashIdx, Math.Max(atIdx, hashIdx));
        if (triggerIdx < 0)
            return SlashParseResult.None;

        char triggerChar;
        if (triggerIdx == slashIdx) triggerChar = '/';
        else if (triggerIdx == atIdx) triggerChar = '@';
        else triggerChar = '#';

        var triggerIndex = lineStart + triggerIdx;
        var afterTrigger = slice[(triggerIndex + 1)..];
        var spaceInAfter = afterTrigger.IndexOf(' ');

        if (triggerChar == '/')
            return ParseSlash(slice, triggerIndex, afterTrigger, spaceInAfter, cursor);

        if (spaceInAfter >= 0)
            return SlashParseResult.None;

        var prefixAfter = afterTrigger.ToString();
        var mode = triggerChar == '@' ? SlashCompletionMode.File : SlashCompletionMode.Tool;
        return new SlashParseResult
        {
            ShouldComplete = true,
            Mode = mode,
            TriggerChar = triggerChar,
            Prefix = prefixAfter,
            SlashIndex = triggerIndex,
            PrefixEnd = cursor
        };
    }

    private static SlashParseResult ParseSlash(
        ReadOnlySpan<char> slice, int triggerIndex, ReadOnlySpan<char> afterSlash, int spaceInAfter, int cursor)
    {
        if (spaceInAfter < 0)
        {
            var prefix = slice[triggerIndex..].ToString();
            return new SlashParseResult
            {
                ShouldComplete = true,
                Mode = SlashCompletionMode.Command,
                TriggerChar = '/',
                Prefix = prefix,
                SlashIndex = triggerIndex,
                PrefixEnd = cursor
            };
        }

        var commandName = slice[triggerIndex..(triggerIndex + 1 + spaceInAfter)].ToString();
        if (!IsArgumentCompletable(commandName))
            return SlashParseResult.None;

        var argStart = triggerIndex + 1 + spaceInAfter + 1;
        if (argStart > cursor)
            return SlashParseResult.None;

        var argPrefix = slice[argStart..].ToString();
        if (argPrefix.Contains(' '))
            return SlashParseResult.None;

        return new SlashParseResult
        {
            ShouldComplete = true,
            Mode = SlashCompletionMode.Argument,
            TriggerChar = '/',
            CommandName = commandName,
            ArgumentPrefix = argPrefix,
            ArgumentStart = argStart,
            SlashIndex = triggerIndex,
            PrefixEnd = cursor
        };
    }

    private static bool IsArgumentCompletable(string commandName)
        => ArgumentCompletableCommands.Contains(commandName, StringComparer.OrdinalIgnoreCase);
}
