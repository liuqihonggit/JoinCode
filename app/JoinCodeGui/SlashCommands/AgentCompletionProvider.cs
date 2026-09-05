namespace JoinCode.Gui.SlashCommands;

/// <summary>
/// 子代理补全提供器 — @ 触发符调用，提供引擎可用子代理列表。
/// 优先使用引擎 SubAgentSummary 动态列表，引擎未就绪时回退内置代理占位列表。
/// 实现 ICompletionProvider 统一接口，注册到 CompletionTriggerRegistry。
/// </summary>
public sealed class AgentCompletionProvider : ICompletionProvider
{
    /// <inheritdoc/>
    public char TriggerChar => '@';

    /// <inheritdoc/>
    public SlashCompletionMode Mode => SlashCompletionMode.Agent;

    /// <inheritdoc/>
    public string Label => "代理补全";

    /// <inheritdoc/>
    public IReadOnlyList<SlashCommandItem> GetCandidates(string prefix, CompletionContext context)
        => GetAgents(prefix, context.AvailableSubAgentsCache);

    /// <summary>获取子代理补全候选（按前缀过滤；优先引擎真实代理，回退占位）</summary>
    public static IReadOnlyList<SlashCommandItem> GetAgents(
        string prefix, IReadOnlyList<SubAgentSummary>? availableAgents = null)
    {
        var source = BuildSource(availableAgents);
        if (string.IsNullOrEmpty(prefix))
            return source;
        return source
            .Where(a => a.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>构建子代理源 — 引擎代理非空时用引擎列表（经 SlashCommandItem.FromAgents 统一构造），否则回退占位</summary>
    private static IReadOnlyList<SlashCommandItem> BuildSource(IReadOnlyList<SubAgentSummary>? availableAgents)
    {
        if (availableAgents is null || availableAgents.Count == 0)
            return BuiltInAgents;
        return SlashCommandItem.FromAgents(availableAgents);
    }

    /// <summary>内置代理占位列表 — 引擎未就绪时的回退（对齐 AgentDefinitionProvider.GetBuiltInDefinitions 的 9 个内置代理）</summary>
    private static readonly IReadOnlyList<SlashCommandItem> BuiltInAgents =
    [
        new() { Name = "coordinator",                  Description = "Coordinator agent — manages Goal lifecycle" },
        new() { Name = "executor:code",                Description = "Code agent focused on code reading, writing and editing" },
        new() { Name = "executor:search",              Description = "Search agent focused on code search and navigation" },
        new() { Name = "executor:explore",             Description = "Explore agent — read-only, for searching and understanding code" },
        new() { Name = "executor:plan",                Description = "Plan agent — read-only, for designing implementation plans" },
        new() { Name = "executor:doctor",              Description = "Doctor agent — 自举修复，后台运行" },
        new() { Name = "executor:verification",        Description = "Verification agent — checks code correctness" },
        new() { Name = "executor:joincodeguide",       Description = "JoinCode Guide agent" },
        new() { Name = "executor:contextcompression",  Description = "Context Compression agent" }
    ];
}
