using JoinCode.Abstractions.Attributes;

namespace Core.Context;

/// <summary>
/// 同义词注入中间件 — 检测同义词并注入补充上下文
/// </summary>
[Register(typeof(IAnalyzePreprocessMiddleware))]
public sealed partial class SynonymInjectionMiddleware : IAnalyzePreprocessMiddleware
{
    private readonly ISynonymMap _synonymMap;
    private readonly ISystemReminderManager _reminderManager;
    [Inject] private readonly ILogger<SynonymInjectionMiddleware>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public SynonymInjectionMiddleware(
        ISynonymMap synonymMap,
        ISystemReminderManager reminderManager,
        ILogger<SynonymInjectionMiddleware>? logger = null)
    {
        _synonymMap = synonymMap;
        _reminderManager = reminderManager;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task InvokeAsync(PreprocessContext context, MiddlewareDelegate<PreprocessContext> next, CancellationToken ct)
    {
        var synonymMatches = SynonymAnalyzer.Analyze(context.Message, _synonymMap);
        var synonymInjectionIds = new List<string>();

        foreach (var match in synonymMatches)
        {
            var synonymId = $"synonym-injection-{Guid.NewGuid():N}";
            synonymInjectionIds.Add(synonymId);
            _logger?.LogInformation("[SynonymInjection] 检测到同义词 '{Key}'，已注入补充内容", match.MatchedKey);
        }

        if (synonymMatches.Count > 0)
        {
            await Task.WhenAll(synonymMatches.Zip(synonymInjectionIds, (match, id) =>
                _reminderManager.AddReminderAsync(id, match.SupplementaryContent, priority: 50, ct: ct))).ConfigureAwait(false);

            var synonymKeys = string.Join("', '", synonymMatches.Select(m => m.MatchedKey));
            var synonymInfo = context.KeywordPromptInjectionInfo != null
                ? $"{context.KeywordPromptInjectionInfo}\n[同义词补充: 检测到 '{synonymKeys}'，已自动注入补充上下文]"
                : $"[同义词补充: 检测到 '{synonymKeys}'，已自动注入补充上下文]";

            context.SynonymPromptInjectionInfo = synonymInfo;
            context.PromptInjectionInfo = synonymInfo;
        }
        else
        {
            context.PromptInjectionInfo = context.KeywordPromptInjectionInfo;
        }

        context.SynonymInjectionIds = synonymInjectionIds;

        await next(context, ct).ConfigureAwait(false);
    }
}
