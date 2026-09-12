namespace JoinCode.Gui.SlashCommands;

/// <summary>补全提供者接口 — 每个触发符一个实现，统一注册到 CompletionTriggerRegistry。</summary>
public interface ICompletionProvider
{
    /// <summary>触发字符（如 '/' '@' '#'）</summary>
    char TriggerChar { get; }

    /// <summary>补全模式</summary>
    SlashCompletionMode Mode { get; }

    /// <summary>模式徽章文本（如 "斜杠命令"、"代理补全"、"文件补全"）</summary>
    string Label { get; }

    /// <summary>获取补全候选 — prefix 为 SlashParseResult.Prefix 原样传入（命令模式含 / 如 "/c"，代理/文件模式不含触发符如 "ag"/"src"）</summary>
    IReadOnlyList<SlashCommandItem> GetCandidates(string prefix, CompletionContext context);
}

/// <summary>补全上下文 — 传给 Provider 的运行时数据，避免各 Provider 各自持有缓存字段。</summary>
public sealed class CompletionContext
{
    /// <summary>GUI 与引擎解耦的会话门面（可能为 null，Provider 需容忍）</summary>
    public IJccChatSession? Session { get; init; }

    /// <summary>斜杠命令缓存（/ 命令补全消费；空时 Provider 可回退内置列表）</summary>
    public IReadOnlyList<SlashCommandItem> SlashCommandCache { get; init; } = [];

    /// <summary>引擎可用子代理缓存（@ 代理补全消费；空时 Provider 可回退占位列表）</summary>
    public IReadOnlyList<SubAgentSummary> AvailableSubAgentsCache { get; init; } = [];
}

/// <summary>
/// 补全触发符注册表 — 按 TriggerChar 索引，Parser 和 ViewModel 统一查询。
/// 未来加新符号（如 '$'）只需写 Provider + 在 Build() 加一行，零改 Parser、零改 ViewModel。
/// </summary>
public static class CompletionTriggerRegistry
{
    private static readonly FrozenDictionary<char, ICompletionProvider> _providers = Build();

    private static FrozenDictionary<char, ICompletionProvider> Build()
    {
        var list = new ICompletionProvider[]
        {
            new CommandCompletionProvider(),
            new AgentCompletionProvider(),
            new FileCompletionProvider()
        };
        return list.ToFrozenDictionary(p => p.TriggerChar);
    }

    /// <summary>按触发符查 Provider；未注册返回 null</summary>
    public static ICompletionProvider? TryGet(char triggerChar)
        => _providers.TryGetValue(triggerChar, out var p) ? p : null;

    /// <summary>全部已注册 Provider（供 Parser 遍历触发符用）</summary>
    public static IReadOnlyCollection<ICompletionProvider> All => _providers.Values;
}
