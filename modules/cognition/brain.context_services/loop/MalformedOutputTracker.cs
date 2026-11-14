namespace Core.Context;

/// <summary>
/// 异常输出追踪器 — 追踪 LLM 连续输出异常格式（如孤立 XML 工具调用标签）的次数。
/// 与 EmptyResponseTracker 同级，防止 LLM 异常输出导致 ReAct 循环静默中断。
/// 重置时机：用户输入新对话 / LLM 恢复正常输出
/// </summary>
[Register(typeof(IMalformedOutputTracker), ServiceLifetime.Singleton)]
public sealed class MalformedOutputTracker : ServiceEntity, IMalformedOutputTracker {
    private const int DefaultMaxConsecutive = 3;
    private int _consecutiveMalformedCount;

    /// <summary>
    /// 当前连续异常输出次数G次数
    /// </summary>
    public int ConsecutiveMalformedCount => _consecutiveMalformedCount;

    /// <summary>
    /// 允许的最大连续异常输出次数，超过即触发强制结束
    /// </summary>
    public int MaxConsecutiveMalformed => DefaultMaxConsecutive;

    /// <summary>
    /// 记录一次异常输出 — 计数+1，返回是否超过阈值
    /// </summary>
    /// <returns>true 表示超过阈值应强制结束；false 表示仍在容忍范围内</returns>
    public bool RecordMalformedOutput() {
        _consecutiveMalformedCount++;
        Diag.WriteLine($"[MalformedOutputTracker] 连续异常输出计数: {_consecutiveMalformedCount}/{DefaultMaxConsecutive}");
        return _consecutiveMalformedCount > DefaultMaxConsecutive;
    }

    /// <summary>
    /// 重置计数器 — LLM 恢复正常输出或用户输入新对话时调用
    /// </summary>
    public void Reset() {
        if (_consecutiveMalformedCount > 0)
            Diag.WriteLine($"[MalformedOutputTracker] 重置异常输出计数器: {_consecutiveMalformedCount} → 0");
        _consecutiveMalformedCount = 0;
    }

    /// <summary>
    /// 构建注入系统提示词的内容 — 提醒 LLM 使用正确的工具调用格式
    /// </summary>
    public string BuildInterventionPrompt() {
        return $"<system-reminder>系统检测到你的输出包含异常的 XML 工具调用标签" +
               $"（第{_consecutiveMalformedCount}次，最多{DefaultMaxConsecutive}次）。" +
               $"请使用正确的 function calling 协议输出工具调用，不要在 content 文本中输出 XML 标签。</system-reminder>";
    }
}
