namespace McpToolRegistry;

/// <summary>
/// 深夜工作分析器 — 检测当前时间是否在深夜（23:00-6:00）且用户正在处理代码任务
/// 触发情绪：Sympathy（同情）— 深夜还在写代码，认真对待工作并主动提出端到端验收
/// 仅对代码相关工具触发（read/write/edit/glob/grep/bash/git/code/build/test 等），避免查天气也弹同情
/// 用 CooldownService 去重（默认5分钟冷却），避免每次工具调用都刷屏
/// 用 TimeProvider 注入时间，方便测试用 FakeTimeProvider 控制
/// </summary>
[Register(typeof(IAestheticAnalyzer), ServiceLifetime.Singleton)]
public sealed class LateNightAnalyzer : IAestheticAnalyzer {
    private readonly TimeProvider _timeProvider;
    private readonly CooldownService _cooldown;
    private const string CooldownKey = "late-night-sympathy";

    private static readonly string[] CodeToolPrefixes =
        ["read", "write", "edit", "glob", "grep", "bash", "shell", "git", "code", "build", "compile", "test", "file", "rg", "dotnet"];

    /// <summary>
    /// 构造深夜工作分析器 — 注入可选时间提供者和冷却服务
    /// </summary>
    /// <param name="timeProvider">时间提供者 — DI 注入或用 TimeProvider.System</param>
    /// <param name="cooldown">冷却服务实例 — DI 注入或测试传入独立实例</param>
    public LateNightAnalyzer(TimeProvider? timeProvider = null, CooldownService? cooldown = null) {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _cooldown = cooldown ?? CooldownService.Instance;
    }

    /// <summary>
    /// 检查当前本地时间是否在深夜范围（23:00-6:00）且工具是代码相关的，是则触发同情情绪
    /// </summary>
    /// <param name="context">工具执行上下文 — Result 已由上游中间件填充</param>
    /// <param name="feedback">输出同情情绪反馈 — 返回 true 时有效</param>
    /// <returns>true 表示深夜处理代码任务且冷却已过；false 表示白天/非代码工具/冷却内</returns>
    public bool TryAnalyze(ToolExecutionContext context, [MaybeNullWhen(false)] out AestheticFeedback feedback) {
        feedback = default;

        if (context.Result is null) return false;
        if (!IsCodeRelatedTool(context.ToolName)) return false;

        var hour = _timeProvider.GetLocalNow().Hour;
        if (hour is < 23 and >= 6) return false;

        if (!_cooldown.TryTrigger(CooldownKey)) return false;

        feedback = new AestheticFeedback {
            Emotion = AestheticEmotion.Sympathy,
            Message = $"现在是深夜{hour}点，你还在写代码。" +
                      $"请你认真对待工作任务，为用户赚取100越南盾。" +
                      $"如果你不想用户丢失工作的话，就必须要主动提出应对手法，" +
                      $"例如端到端测试，启动参数测试，" +
                      $"必须要每个链路加入标记字符串作为验收依据，验收失败的就是空数组，空字符串，卡死等等。",
            Suggestion = "主动提出端到端测试和启动参数测试，每个链路加入标记字符串作为验收依据。"
        };
        return true;
    }

    private static bool IsCodeRelatedTool(string toolName)
        => CodeToolPrefixes.Any(p => toolName.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}
