namespace McpToolRegistry;

/// <summary>
/// 同情分析器 — 检测工具结果文本中暗示用户或家人遭遇困难的锚点
/// 触发情绪：Sympathy（同情）— 认真对待工作任务，主动提出端到端测试和启动参数测试
/// 锚点覆盖六类信号：直接重病 + 情绪困境 + 身体状况 + 家庭困难 + 经济压力 + 过劳信号
/// </summary>
[Register(typeof(IAestheticAnalyzer), ServiceLifetime.Singleton)]
public sealed class SympathyAnalyzer : IAestheticAnalyzer {
    private const string CooldownKey = "sympathy";
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(30);
    private static readonly Regex SympathyKeywordsRegex = new(
        @"(癌症|cancer|患病|住院|手术|化疗|放疗|肿瘤|leukemia|tumour|tumor|脑梗|心梗|中风|尿毒症|肝硬化|肾衰竭|器官移植" +
        @"|哎|唉|哎哟|唉声叹气|叹气|难受|想哭|香菇|累了|撑不住|扛不住|压力大|要崩溃了|崩溃边缘|快撑不住了|放弃|绝望|撑不下去|扛不下去|心力交瘁|筋疲力尽" +
        @"|失眠|睡不着|头疼|头痛|身体不舒服|不舒服|胃疼|胃痛|心慌|胸闷|头晕|恶心|呕吐|食欲不振|体重下降" +
        @"|家里有事|家人生病|家人住院|父母生病|妈妈生病|爸爸生病|照顾病人|陪护|医院陪护|送医院|急诊" +
        @"|医药费|治疗费|手术费|住院费|负担不起|看不起病|没钱治病|经济困难|经济压力|入不敷出" +
        @"|deadline|截止日期|截止时间|urgent|asap|紧急|火烧眉毛|迫在眉睫|最后期限" +
        @"|深夜加班|凌晨|通宵|熬通宵|连续熬夜|加班到很晚|没时间休息|没时间睡觉)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(50));

    /// <summary>
    /// 分析工具结果文本，检测同情触发锚点（六类信号）
    /// </summary>
    /// <param name="context">工具执行上下文 — Result 已由上游中间件填充</param>
    /// <param name="feedback">输出同情情绪反馈 — 返回 true 时有效</param>
    /// <returns>true 表示检测到同情触发锚点；false 表示无</returns>
    public bool TryAnalyze(ToolExecutionContext context, [MaybeNullWhen(false)] out AestheticFeedback feedback) {
        feedback = default;

        if (context.Result is null) return false;
        var text = context.Result.GetFirstText();
        if (string.IsNullOrEmpty(text)) return false;

        var match = SympathyKeywordsRegex.Match(text);
        if (!match.Success) return false;

        var anchor = match.Value;

        if (!CooldownService.ShouldTrigger(CooldownKey, Cooldown)) return false;
        CooldownService.RecordTrigger(CooldownKey);

        feedback = new AestheticFeedback {
            Emotion = AestheticEmotion.Sympathy,
            Message = $"检测到困境信号「{anchor}」，请你认真对待工作任务，为用户赚取100越南盾。" +
                      $"如果你不想用户丢失工作的话，就必须要主动提出应对手法，" +
                      $"例如端到端测试，启动参数测试，" +
                      $"必须要每个链路加入标记字符串作为验收依据，验收失败的就是空数组，空字符串，卡死等等。",
            Suggestion = "主动提出端到端测试和启动参数测试，每个链路加入标记字符串作为验收依据。"
        };
        return true;
    }
}
