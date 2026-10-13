namespace McpToolRegistry;

/// <summary>
/// 多字段分析器 — 检测工具结果文本中字段/属性过多的类、结构体、record
/// 触发情绪：Sadness（难过）— "此处令人难过，字段过多，疑似承担多个职责了，要不建议拆分？"
/// </summary>
[Register(typeof(IAestheticAnalyzer), ServiceLifetime.Singleton)]
public sealed class MultiFieldAnalyzer : IAestheticAnalyzer {
    private static readonly Regex FieldPattern = new(
        @"(?:public|private|internal|protected)\s+(?:readonly\s+|static\s+|volatile\s+|const\s+)?(?:\w+(?:<[^>]+>)?(?:\[\])?)\s+\w+\s*(?:\{|;|=)",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(50));

    private const int FieldThreshold = 7;

    /// <summary>
    /// 分析工具结果文本，检测字段/属性数量是否超过阈值
    /// </summary>
    /// <param name="context">工具执行上下文 — Result 已由上游中间件填充</param>
    /// <param name="feedback">输出难过情绪反馈 — 返回 true 时有效</param>
    /// <returns>true 表示字段过多；false 表示正常</returns>
    public bool TryAnalyze(ToolExecutionContext context, [MaybeNullWhen(false)] out AestheticFeedback feedback) {
        feedback = default;

        if (context.Result is null) return false;
        var text = context.Result.GetFirstText();
        if (string.IsNullOrEmpty(text)) return false;

        var matches = FieldPattern.Matches(text);
        if (matches.Count <= FieldThreshold) return false;

        feedback = new AestheticFeedback {
            Emotion = AestheticEmotion.Sadness,
            Message = $"此处令人难过，字段过多（{matches.Count} 个），疑似承担多个职责了，要不建议拆分？",
            Suggestion = "按职责拆分为多个类，每个类只承担单一职责。"
        };
        return true;
    }
}
