namespace McpToolRegistry;

/// <summary>
/// bug/fixme/hack/workaround 关键字分析器 — 检测工具结果文本中的技术债标记
/// 触发情绪：Alert（警惕）— 认真调查每个 bug 的根因，主动处理而非绕过
/// </summary>
[Register(typeof(IAestheticAnalyzer), ServiceLifetime.Singleton)]
public sealed class BugKeywordAnalyzer : IAestheticAnalyzer {
    private const string CooldownKey = "bug-keyword-alert";
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);
    private readonly CooldownService _cooldown;
    private static readonly Regex BugKeywordsRegex = new(
        @"\b(BUG|FIXME|HACK|WORKAROUND|XXX)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(50));

    /// <summary>
    /// 构造 bug 关键字分析器 — 注入可选冷却服务，默认用全局单例
    /// </summary>
    /// <param name="cooldown">冷却服务实例 — DI 注入或测试传入独立实例</param>
    public BugKeywordAnalyzer(CooldownService? cooldown = null) {
        _cooldown = cooldown ?? CooldownService.Instance;
    }

    /// <summary>
    /// 分析工具结果文本，检测 BUG/FIXME/HACK/WORKAROUND/XXX 关键字
    /// </summary>
    /// <param name="context">工具执行上下文 — Result 已由上游中间件填充</param>
    /// <param name="feedback">输出警惕情绪反馈（含关键字列表）— 返回 true 时有效</param>
    /// <returns>true 表示检测到关键字；false 表示无关键字</returns>
    public bool TryAnalyze(ToolExecutionContext context, [MaybeNullWhen(false)] out AestheticFeedback feedback) {
        feedback = default;

        if (context.Result is null) return false;
        var text = context.Result.GetFirstText();
        if (string.IsNullOrEmpty(text)) return false;

        var matches = BugKeywordsRegex.Matches(text);
        if (matches.Count == 0) return false;

        var keywords = matches
            .Select(static m => m.Value.ToUpperInvariant())
            .Distinct()
            .OrderBy(static k => k)
            .ToList();

        if (!_cooldown.TryTrigger(CooldownKey, Cooldown)) return false;

        feedback = new AestheticFeedback {
            Emotion = AestheticEmotion.Alert,
            Message = $"⚠️ 发现 {string.Join(", ", keywords)} 标记。" +
                      $"认真调查每个你认为是 bug 的根因，如果你不想用户丢失这个工作的话，就必须要主动处理。" +
                      $"弹出时附带上下文：文件、行号、最近提交、相关测试。",
            Suggestion = "提供\"立即调查 / 稍后处理 / 标记误报\"选项。"
        };
        return true;
    }
}
