namespace Core.Context;

/// <summary>
/// 关键词注入中间件 — 检测用户输入关键词并注入对应提示词
/// 优先级：动态关键词（DynamicKeywordConfigService，~/.jcc/keyword-sections.json）→ 硬编码关键词（UserPromptKeywordAnalyzer，fallback）
/// 未命中时记录 miss 事件，供后台 Agent 分析优化词表
/// </summary>
[Register(typeof(IAnalyzePreprocessMiddleware), ServiceLifetime.Singleton)]
public sealed partial class KeywordInjectionMiddleware : ServiceEntity, IAnalyzePreprocessMiddleware
{

    public KeywordInjectionMiddleware(ISystemReminderManager reminderManager, IDynamicKeywordConfigService dynamicKeywordService, IFileSystem fs, ILogger<KeywordInjectionMiddleware>? logger = null)
    {
        _reminderManager = reminderManager;
        _dynamicKeywordService = dynamicKeywordService;
        _fs = fs;
        _logger = logger;
    }
    private readonly ISystemReminderManager _reminderManager;
    private readonly IDynamicKeywordConfigService _dynamicKeywordService;
    private readonly IFileSystem _fs;
    private readonly ILogger<KeywordInjectionMiddleware>? _logger;

    private const string MissLogFileName = "keyword-misses.jsonl";
    private const int MaxMissLogSize = 1024 * 1024;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc/>
    public async Task InvokeAsync(PreprocessContext context, MiddlewareDelegate<PreprocessContext> next, CancellationToken ct)
    {
        var dynamicMatch = _dynamicKeywordService.TryMatch(context.Message);
        if (dynamicMatch is not null)
        {
            await InjectDynamicKeywordAsync(context, dynamicMatch, ct).ConfigureAwait(false);
        }
        else
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
                RecordKeywordMiss(context.Message);
            }
        }

        await next(context, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 动态关键词注入
    /// </summary>
    private async Task InjectDynamicKeywordAsync(PreprocessContext context, DynamicKeywordMatchResult dynamicMatch, CancellationToken ct)
    {
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

    /// <summary>
    /// 记录关键词未命中事件 — 供后台 Agent 分析优化词表
    /// </summary>
    private void RecordKeywordMiss(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 200)
            return;

        try
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var dir = Path.Combine(userProfile, AppDataConstants.AppDataFolder);
            var filePath = Path.Combine(dir, MissLogFileName);

            if (!_fs.DirectoryExists(dir))
                _fs.CreateDirectory(dir);

            if (_fs.FileExists(filePath) && _fs.GetFileLength(filePath) > MaxMissLogSize)
                return;

            var entry = $"{{\"timestamp\":\"{DateTime.UtcNow:O}\",\"input\":\"{JsonEncode(input)}\"}}\n";
            _fs.AppendAllText(filePath, entry);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "记录关键词 miss 失败");
        }
    }

    private static string JsonEncode(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
}
