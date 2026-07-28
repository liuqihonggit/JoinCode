namespace Core.Context;

/// <summary>
/// 预处理器接口 — 关键词/同义词注入、上下文准备、清理
/// </summary>
public interface IChatPreprocessor
{
    /// <summary>
    /// 分析消息并注入关键词/同义词
    /// </summary>
    Task<PreprocessResult> AnalyzeAndInjectAsync(string message, CancellationToken ct);

    /// <summary>
    /// 准备上下文（系统提示词构建）
    /// </summary>
    Task PrepareContextAsync(string message, bool isDryRun = false, CancellationToken ct = default);

    /// <summary>
    /// 清理注入
    /// </summary>
    Task CleanupInjectionsAsync(UserPromptKeywordResult keywordResult, List<string> synonymInjectionIds, CancellationToken ct);
}
