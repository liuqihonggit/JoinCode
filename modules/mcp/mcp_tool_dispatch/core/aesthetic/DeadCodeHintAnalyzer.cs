namespace McpToolRegistry;

/// <summary>
/// 死代码提示分析器 — 检测工具结果文本中暗示死代码的关键词
/// 触发情绪：Confusion（疑惑）— "函数关系图谱好像连接不起来，疑似死代码"
/// </summary>
[Register(typeof(IAestheticAnalyzer), ServiceLifetime.Singleton)]
public sealed class DeadCodeHintAnalyzer : IAestheticAnalyzer {
    private const string CooldownKey = "dead-code-hint";
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);
    private readonly CooldownService _cooldown;
    private static readonly Regex DeadCodeKeywordsRegex = new(
        @"\b(unreferenced|never\s+called|dead\s+code|unused\s+method|unused\s+function|orphaned|no\s+callers)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(50));

    /// <summary>
    /// 构造死代码提示分析器 — 注入可选冷却服务，默认用全局单例
    /// </summary>
    /// <param name="cooldown">冷却服务实例 — DI 注入或测试传入独立实例</param>
    public DeadCodeHintAnalyzer(CooldownService? cooldown = null) {
        _cooldown = cooldown ?? CooldownService.Instance;
    }

    /// <summary>
    /// 分析工具结果文本，检测死代码相关关键词
    /// </summary>
    /// <param name="context">工具执行上下文 — Result 已由上游中间件填充</param>
    /// <param name="feedback">输出疑惑情绪反馈 — 返回 true 时有效</param>
    /// <returns>true 表示检测到死代码线索；false 表示无</returns>
    public bool TryAnalyze(ToolExecutionContext context, [MaybeNullWhen(false)] out AestheticFeedback feedback) {
        feedback = default;

        if (context.Result is null) return false;
        var text = context.Result.GetFirstText();
        if (string.IsNullOrEmpty(text)) return false;

        if (!DeadCodeKeywordsRegex.IsMatch(text)) return false;

        if (!_cooldown.TryTrigger(CooldownKey, Cooldown)) return false;

        feedback = new AestheticFeedback {
            Emotion = AestheticEmotion.Confusion,
            Message = "我还想查看更多，这里函数的关系图谱好像连接不起来，疑似死代码。",
            Suggestion = "用 GraphDetectDeadCode 工具确认，或检查调用链是否完整。"
        };
        return true;
    }
}
