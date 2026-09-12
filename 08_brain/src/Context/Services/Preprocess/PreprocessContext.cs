namespace Core.Context;

/// <summary>
/// 预处理上下文 — 在中间件管道中流转的共享状态
/// </summary>
public sealed class PreprocessContext
{
    /// <summary>原始用户消息</summary>
    public required string Message { get; init; }

    // --- 关键词注入结果 ---

    /// <summary>关键词分析结果</summary>
    public UserPromptKeywordResult KeywordResult { get; set; } = new();

    /// <summary>关键词注入后的提示信息</summary>
    public string? KeywordPromptInjectionInfo { get; set; }

    // --- 同义词注入结果 ---

    /// <summary>同义词注入 ID 列表（用于后续清理）</summary>
    public List<string> SynonymInjectionIds { get; set; } = [];

    /// <summary>同义词注入后的提示信息（追加到关键词提示之后）</summary>
    public string? SynonymPromptInjectionInfo { get; set; }

    // --- 系统提示构建结果 ---

    /// <summary>静态前缀部分</summary>
    public string? StaticPrefix { get; set; }

    /// <summary>动态后缀部分</summary>
    public string? DynamicSuffix { get; set; }

    // --- 提醒注入结果 ---

    /// <summary>格式化后的系统提醒文本</summary>
    public string? FormattedReminders { get; set; }

    // --- LSP 诊断结果 ---

    /// <summary>待处理的 LSP 诊断信息</summary>
    public string? LspDiagnosticText { get; set; }

    // --- 合并后的注入提示信息 ---

    /// <summary>最终合并的注入提示信息（展示给用户）</summary>
    public string? PromptInjectionInfo { get; set; }
}
