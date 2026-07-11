
namespace Core.Prompts.Templates.System;

/// <summary>
/// 提示词建议模板 - 用于生成用户输入建议
/// </summary>
/// <remarks>消费者: PromptSuggestionCallback → IForkSubAgentManager.ForkAsync() → ShouldFilterSuggestion()</remarks>
[PromptTemplate(Name = "prompt_suggestion", Category = PromptTemplateCategory.System, Description = "用户输入建议提示词", ContentMember = nameof(SuggestionPrompt))]
public static class PromptSuggestionFilter
{
    /// <summary>
    /// 建议提示词
    /// </summary>
    public const string SuggestionPrompt = """
[建议模式：建议用户接下来可能自然输入到CLI的内容。]

首先：查看用户最近的消息和原始请求。

你的工作是预测他们会输入什么 - 不是你认为他们应该做什么。

测试：他们会想"我正要输入那个"吗？

示例：
用户要求"修复错误并运行测试"，错误已修复 -> "运行测试"
编写代码后 -> "试试看"
CLI 提供选项 -> 根据对话建议用户可能选择的那个
CLI 要求继续 -> "是" 或 "继续"
任务完成，明显的后续 -> "提交此更改" 或 "推送它"
错误或误解后 -> 保持沉默（让他们评估/纠正）

要具体："运行测试" 胜过 "继续"。

永远不要建议：
- 评价性的（"看起来不错"、"谢谢"）
- 问题（"...怎么样？"）
- CLI 的声音（"让我..."、"我会..."、"这是..."）
- 他们没有问过的新想法
- 多个句子

如果下一步从用户所说的内容看不明显，保持沉默。

格式：2-12 个词，匹配用户的风格。或什么都不说。

只回复建议，不加引号或解释。
""";

    /// <summary>
    /// 允许的单字建议
    /// </summary>
    public static readonly HashSet<string> AllowedSingleWords =
    [
        // 肯定词
        "yes", "yeah", "yep", "yea", "yup", "sure", "ok", "okay",
        // 动作
        "push", "commit", "deploy", "stop", "continue", "check", "exit", "quit",
        // 否定
        "no"
    ];

    private static readonly Regex SilencePatternRegex = new(@"\bsilence is\b|\bstay(s|ing)? silent\b", RegexOptions.IgnoreCase);
    private static readonly Regex SilenceOnlyRegex = new(@"^\W*silence\W*$");
    private static readonly Regex MetaWrapperRegex = new(@"^\(.*\)$|^\[.*\]$");
    private static readonly Regex PrefixLabelRegex = new(@"^\w+:\s");
    private static readonly Regex MultiSentenceRegex = new(@"[.!?]\s+[A-Z]");
    private static readonly Regex EvaluativeRegex = new(@"thanks|thank you|looks good|sounds good|that works|that worked|that's all|nice|great|perfect|makes sense|awesome|excellent", RegexOptions.IgnoreCase);
    private static readonly Regex ClaudeVoiceRegex = new(@"^(let me|i'll|i've|i'm|i can|i would|i think|i notice|here's|here is|here are|that's|this is|this will|you can|you should|you could|sure,|of course|certainly)", RegexOptions.IgnoreCase);

    /// <summary>
    /// 检查建议是否应该被过滤
    /// </summary>
    public static bool ShouldFilterSuggestion(string? suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion))
            return true;

        var lower = suggestion.ToLowerInvariant();
        var words = suggestion.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var wordCount = words.Length;

        // 过滤 "done"
        if (lower == "done")
            return true;

        // 过滤元文本
        if (lower == "nothing found" ||
            lower == "nothing found." ||
            lower.StartsWith("nothing to suggest") ||
            lower.StartsWith("no suggestion") ||
            SilencePatternRegex.IsMatch(lower) ||
            SilenceOnlyRegex.IsMatch(lower))
            return true;

        // 过滤元包装
        if (MetaWrapperRegex.IsMatch(suggestion))
            return true;

        // 过滤错误消息
        if (lower.StartsWith("api error:") ||
            lower.StartsWith("prompt is too long") ||
            lower.StartsWith("request timed out") ||
            lower.StartsWith("invalid api key") ||
            lower.StartsWith("image was too large"))
            return true;

        // 过滤带前缀标签的
        if (PrefixLabelRegex.IsMatch(suggestion))
            return true;

        // 过滤词数太少的
        if (wordCount < 2)
        {
            if (suggestion.StartsWith('/'))
                return false; // 允许斜杠命令
            if (!AllowedSingleWords.Contains(lower))
                return true;
        }

        // 过滤词数太多的
        if (wordCount > 12)
            return true;

        // 过滤太长的
        if (suggestion.Length >= WorkflowConstants.ContextCompression.MaxHistorySize)
            return true;

        // 过滤多句子的
        if (MultiSentenceRegex.IsMatch(suggestion))
            return true;

        // 过滤有格式的
        if (suggestion.Contains('\n') || suggestion.Contains("**"))
            return true;

        // 过滤评价性的
        if (EvaluativeRegex.IsMatch(lower))
            return true;

        // 过滤 Claude 的声音
        if (ClaudeVoiceRegex.IsMatch(suggestion))
            return true;

        return false;
    }
}
