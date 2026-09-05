namespace JoinCode.Gui.SlashCommands;

/// <summary>
/// 斜杠命令补全提供器 — / 触发符调用，从命令缓存 + Trie 匹配 + 排序提供候选。
/// 实现 ICompletionProvider 统一接口，注册到 CompletionTriggerRegistry。
/// / 命令的 Argument 模式（/model xxx）由 Parser 特殊分支处理，不走此 Provider。
/// </summary>
public sealed class CommandCompletionProvider : ICompletionProvider
{
    /// <inheritdoc/>
    public char TriggerChar => '/';

    /// <inheritdoc/>
    public SlashCompletionMode Mode => SlashCompletionMode.Command;

    /// <inheritdoc/>
    public string Label => "斜杠命令";

    /// <inheritdoc/>
    /// <remarks>prefix 含触发符（如 "/c"，对齐 SlashCommandParser 命令模式 Prefix 语义），直接传给 Filter/Rank</remarks>
    public IReadOnlyList<SlashCommandItem> GetCandidates(string prefix, CompletionContext context)
    {
        var cache = context.SlashCommandCache.Count > 0
            ? context.SlashCommandCache
            : SlashCommandItem.BuiltInCommands;
        var matched = SlashCommandItem.Filter(prefix, cache);
        return SlashCommandRanker.Rank(matched, prefix);
    }
}
