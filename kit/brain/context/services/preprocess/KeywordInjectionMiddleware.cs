namespace Core.Context;

/// <summary>
/// 关键词注入中间件 — 检测用户输入关键词并注入对应提示词
/// 优先级：动态关键词（DynamicKeywordConfigService，~/.jcc/keyword-sections.json）→ 硬编码关键词（UserPromptKeywordAnalyzer，fallback）
/// 未命中时记录 miss 事件，供后台 Agent 分析优化词表
/// </summary>
[Register(typeof(IAnalyzePreprocessMiddleware), ServiceLifetime.Singleton)]
public sealed partial class KeywordInjectionMiddleware : ServiceEntity, IAnalyzePreprocessMiddleware {

    /// <summary>
    /// 初始化关键词注入中间件
    /// </summary>
    /// <param name="reminderManager">系统提醒管理器</param>
    /// <param name="dynamicKeywordService">动态关键词配置服务</param>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="logger">可选日志记录器</param>
    public KeywordInjectionMiddleware(ISystemReminderManager reminderManager, IDynamicKeywordConfigService dynamicKeywordService, IFileSystem fs, ILogger<KeywordInjectionMiddleware>? logger = null) {
        _reminderManager = reminderManager;
        _dynamicKeywordService = dynamicKeywordService;
        _fs = fs;
        _logger = logger;
    }
    private readonly ISystemReminderManager _reminderManager;
    private readonly IDynamicKeywordConfigService _dynamicKeywordService;
    private readonly IFileSystem _fs;
    private readonly ILogger<KeywordInjectionMiddleware>? _logger;

    private const string MissLogFileName = "keyword-misses.json";
    private const int MaxMissLogSize = 1024 * 1024;

    /// <summary>错误行为策略：继续执行后续中间件</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc/>
    public async Task InvokeAsync(PreprocessContext context, MiddlewareDelegate<PreprocessContext> next, CancellationToken ct) {
        var dynamicMatch = _dynamicKeywordService.TryMatch(context.Message);
        if (dynamicMatch is not null) {
            await InjectDynamicKeywordAsync(context, dynamicMatch, ct).ConfigureAwait(false);
        } else {
            var keywordResult = UserPromptKeywordAnalyzer.AnalyzeInput(context.Message);
            context.KeywordResult = keywordResult;

            if (!keywordResult.HasPromptInjection) {
                await RecordKeywordMiss(context.Message).ConfigureAwait(false);
                await next(context, ct).ConfigureAwait(false);
                return;
            }

            var keywordCooldownKey = $"keyword-{keywordResult.Type}";
            if (!CooldownService.ShouldTrigger(keywordCooldownKey)) {
                _logger?.LogDebug("[UserPromptInjection] 关键词 '{Keyword}' 在冷却期内，跳过注入", keywordResult.MatchedKeyword);
                await next(context, ct).ConfigureAwait(false);
                return;
            }
            CooldownService.RecordTrigger(keywordCooldownKey);

            _logger?.LogDebug("[UserPromptInjection] 检测到关键词 '{Keyword}'，类型: {Type}",
                keywordResult.MatchedKeyword, keywordResult.Type);

            var injectionId = $"user-prompt-injection-{keywordResult.Type}";
            await _reminderManager.AddReminderAsync(
                injectionId,
                keywordResult.SuggestedPrompt,
                priority: 100,
                ct: ct).ConfigureAwait(false);

            var sectionContent = KeywordSectionMapper.GetSectionContentForKeywordType(keywordResult.Type);
            if (sectionContent != null) {
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

        await next(context, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 动态关键词注入
    /// </summary>
    private async Task InjectDynamicKeywordAsync(PreprocessContext context, DynamicKeywordMatchResult dynamicMatch, CancellationToken ct) {
        _logger?.LogDebug("[DynamicKeyword] 检测到动态关键词 '{Keyword}'，Section: {Section}",
            dynamicMatch.MatchedKeyword, dynamicMatch.SectionName);

        var sectionContent = dynamicMatch.HasCustomContent
            ? dynamicMatch.CustomContent!
            : KeywordSectionMapper.GetSectionContentForName(dynamicMatch.SectionName);

        if (string.IsNullOrEmpty(sectionContent)) {
            _logger?.LogDebug("[DynamicKeyword] Section '{Section}' 无内容，跳过注入", dynamicMatch.SectionName);
            return;
        }

        var cooldownKey = $"dynamic-{dynamicMatch.SectionName}";
        if (!CooldownService.ShouldTrigger(cooldownKey)) {
            _logger?.LogDebug("[DynamicKeyword] Section '{Section}' 在冷却期内，跳过注入", dynamicMatch.SectionName);
            return;
        }
        CooldownService.RecordTrigger(cooldownKey);

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
    private async ValueTask RecordKeywordMiss(string input) {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 200)
            return;

        try {
            var dir = AppDataConstants.Paths.SessionsDirectory;
            var filePath = Path.Combine(dir, MissLogFileName);

            if (!_fs.DirectoryExists(dir))
                _fs.CreateDirectory(dir);

            if (_fs.FileExists(filePath) && _fs.GetFileLength(filePath) > MaxMissLogSize)
                return;

            var dto = new KeywordMissLogDto { Timestamp = DateTime.UtcNow, Input = input };
            var entry = JsonSerializer.Serialize(dto, ChatServiceJsonContext.Default.KeywordMissLogDto) + "\n";
            await _fs.AppendAllText(filePath, entry).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogDebug(ex, "记录关键词 miss 失败");
        }
    }
}

/// <summary>
/// 关键词未命中日志 DTO — 用于 JSONL 序列化的紧凑结构，字段名与原手写拼接保持一致
/// </summary>
public sealed record KeywordMissLogDto {
    /// <summary>时间戳（ISO 8601 round-trip 格式）</summary>
    [JsonPropertyName("timestamp")]
    public required DateTime Timestamp { get; init; }
    /// <summary>用户输入文本</summary>
    [JsonPropertyName("input")]
    public required string Input { get; init; }
}