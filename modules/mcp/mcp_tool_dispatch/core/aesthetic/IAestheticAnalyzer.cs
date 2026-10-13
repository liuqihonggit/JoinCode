namespace McpToolRegistry;

/// <summary>
/// 美学分析器接口 — 分析工具执行结果，生成情绪反馈
/// 实现者通过分析 ToolExecutionContext.Result 的文本内容，检测代码品味问题并返回情绪化提示
/// </summary>
public interface IAestheticAnalyzer {
    /// <summary>
    /// 分析工具执行上下文，尝试生成情绪反馈
    /// </summary>
    /// <param name="context">工具执行上下文 — Result 已由上游中间件填充</param>
    /// <param name="feedback">输出情绪反馈 — 返回 true 时有效，false 时为 default</param>
    /// <returns>true 表示检测到品味问题并生成反馈；false 表示结果正常无需反馈</returns>
    bool TryAnalyze(ToolExecutionContext context, [MaybeNullWhen(false)] out AestheticFeedback feedback);
}
