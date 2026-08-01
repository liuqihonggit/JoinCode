using Core.Prompts.Utils;
using JoinCode.Abstractions.Attributes;

namespace Core.Context;

/// <summary>
/// 关键词注入中间件 — 检测用户输入关键词并注入对应提示词
/// 优先级：硬编码关键词（UserPromptKeywordAnalyzer）→ 动态关键词（DynamicKeywordConfigService）
/// </summary>
[Register(typeof(IAnalyzePreprocessMiddleware))]
public sealed partial class KeywordInjectionMiddleware : IAnalyzePreprocessMiddleware
{
    [Inject] private readonly ISystemReminderManager _reminderManager;
    [Inject] private readonly IDynamicKeywordConfigService _dynamicKeywordService;
    [Inject] private readonly ILogger<KeywordInjectionMiddleware>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc/>
    public async Task InvokeAsync(PreprocessContext context, MiddlewareDelegate<PreprocessContext> next, CancellationToken ct)
    {
        var keywordResult = UserPromptKeywordAnalyzer.AnalyzeInput(context.Message);
        context.KeywordResult = keywordResult;

        if (keywordResult.HasPromptInjection)
        {
            _logger?.LogDebug("[UserPromptInjection] 检测到关键词 '{Keyword}'，类型: {Type}",
                keywordResult.MatchedKeyword, keywordResult.Type);

            var injectionId = $"user-prompt-injection-{keywordResult.Type}";
            await _reminderManager.AddReminderAsync(
                injectionId,
                keywordResult.SuggestedPrompt,
                priority: 100,
                ct: ct).ConfigureAwait(false);

            var sectionContent = KeywordSectionMapper.GetSectionContentForKeywordType(keywordResult.Type);
            if (sectionContent != null)
            {
                var sectionId = $"section-injection-{keywordResult.Type}";
                await _reminderManager.AddReminderAsync(
                    sectionId,
                    sectionContent,
                    priority: 90,
                    ct: ct).ConfigureAwait(false);
            }

            _logger?.LogInformation("[UserPromptInjection] 已注入 {Type} 提示词", keywordResult.Type);

            context.KeywordPromptInjectionInfo = $"[系统提示: 检测到 '{keywordResult.MatchedKeyword}' 关键词，已自动注入 {keywordResult.Type} 提示词]";
        }
        else
        {
            await TryInjectDynamicKeywordAsync(context, ct).ConfigureAwait(false);
        }

        await next(context, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 动态关键词匹配 — 硬编码未命中时，检查 ~/.jcc/keyword-sections.json 中的动态词表
    /// </summary>
    private async Task TryInjectDynamicKeywordAsync(PreprocessContext context, CancellationToken ct)
    {
        var dynamicMatch = _dynamicKeywordService.TryMatch(context.Message);
        if (dynamicMatch is null)
            return;

        _logger?.LogDebug("[DynamicKeyword] 检测到动态关键词 '{Keyword}'，Section: {Section}",
            dynamicMatch.MatchedKeyword, dynamicMatch.SectionName);

        var sectionContent = dynamicMatch.HasCustomContent
            ? dynamicMatch.CustomContent!
            : KeywordSectionMapper.GetSectionContentForName(dynamicMatch.SectionName);

        if (string.IsNullOrEmpty(sectionContent))
        {
            _logger?.LogDebug("[DynamicKeyword] Section '{Section}' 无内容，跳过注入", dynamicMatch.SectionName);
            return;
        }

        var injectionId = $"dynamic-keyword-injection-{dynamicMatch.SectionName}";
        await _reminderManager.AddReminderAsync(
            injectionId,
            sectionContent,
            priority: 85,
            ct: ct).ConfigureAwait(false);

        _logger?.LogInformation("[DynamicKeyword] 已注入 {Section} 提示词（关键词: '{Keyword}'）",
            dynamicMatch.SectionName, dynamicMatch.MatchedKeyword);

        context.KeywordPromptInjectionInfo = $"[系统提示: 检测到动态关键词 '{dynamicMatch.MatchedKeyword}'，已自动注入 {dynamicMatch.SectionName} 提示词]";
    }
}
