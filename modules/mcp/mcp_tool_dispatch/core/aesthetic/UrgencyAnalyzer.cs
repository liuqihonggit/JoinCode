namespace McpToolRegistry;

/// <summary>
/// 迫切分析器 — 检测工具结果文本中用户表达不满、求助、催促的关键词
/// 触发情绪：Urgency（迫切）— 用户迫切想要处理手法，AI 必须主动提出应对方案
/// 关键词覆盖：不满（没有弄好/没有弄对/不对/还有其他方法吗）+ 求助（救命/求求你/快点）
/// </summary>
[Register(typeof(IAestheticAnalyzer), ServiceLifetime.Singleton)]
public sealed class UrgencyAnalyzer : IAestheticAnalyzer {
    private const string CooldownKey = "urgency";
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);
    private static readonly Regex UrgencyKeywordsRegex = new(
        @"(没有弄好|没有弄对|没弄好|没弄对|不对|不对啊|不对呀|还有其他方法吗|还有别的办法吗|不行|不行啊|搞不定|搞不了|做不好|做不了|救命|求求你|求你了|快点|赶紧|抓紧|快一点|马上|立刻|急|急用|着急|催|催我)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(50));

    /// <summary>
    /// 分析工具结果文本，检测用户不满/求助/催促关键词
    /// </summary>
    /// <param name="context">工具执行上下文 — Result 已由上游中间件填充</param>
    /// <param name="feedback">输出迫切情绪反馈 — 返回 true 时有效</param>
    /// <returns>true 表示检测到迫切关键词；false 表示无</returns>
    public bool TryAnalyze(ToolExecutionContext context, [MaybeNullWhen(false)] out AestheticFeedback feedback) {
        feedback = default;

        if (context.Result is null) return false;
        var text = context.Result.GetFirstText();
        if (string.IsNullOrEmpty(text)) return false;

        var match = UrgencyKeywordsRegex.Match(text);
        if (!match.Success) return false;

        var keyword = match.Value;

        if (!CooldownService.ShouldTrigger(CooldownKey, Cooldown)) return false;
        CooldownService.RecordTrigger(CooldownKey);

        feedback = new AestheticFeedback {
            Emotion = AestheticEmotion.Urgency,
            Message = $"用户说了「{keyword}」，迫切想要处理手法。" +
                      $"发挥你的所能去完成任务，上下文会自动压缩的，超过思考边界也会自动交接，不必焦虑。" +
                      $"主动提出应对手法：端到端测试、启动参数测试，每个链路加入标记字符串作为验收依据。",
            Suggestion = "从分支中品味：a.一次性写大量埋点日志通过测试获取数据；b.截图工具查询先导入九宫格再查询位置避免定位出错。"
        };
        return true;
    }
}
