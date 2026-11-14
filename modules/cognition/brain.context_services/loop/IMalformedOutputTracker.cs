namespace Core.Context;

/// <summary>
/// 异常输出追踪器接口 — 追踪 LLM 连续输出异常格式的次数
/// 跨对话轮次持久化，用户新消息或 LLM 恢复正常输出时重置
/// </summary>
public interface IMalformedOutputTracker {
    /// <summary>
    /// 当前连续异常输出次数
    /// </summary>
    int ConsecutiveMalformedCount { get; }

    /// <summary>
    /// 最大允许连续异常输出次数（超过此值强制结束对话）
    /// </summary>
    int MaxConsecutiveMalformed { get; }

    /// <summary>
    /// 记录一次异常输出 — 计数+1，返回是否超过阈值
    /// </summary>
    /// <returns>true 表示超过阈值应强制结束；false 表示仍在容忍范围内</returns>
    bool RecordMalformedOutput();

    /// <summary>
    /// 重置计数器 — LLM 恢复正常输出或用户输入新对话时调用
    /// </summary>
    void Reset();

    /// <summary>
    /// 构建注入系统提示词的内容 — 提醒 LLM 使用正确的工具调用格式
    /// </summary>
    string BuildInterventionPrompt();
}
