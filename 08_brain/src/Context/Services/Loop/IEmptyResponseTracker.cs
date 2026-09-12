namespace Core.Context;

/// <summary>
/// 空响应追踪器接口 — 追踪工具调用后LLM空响应的连续次数
/// 跨对话轮次持久化，用户新消息或LLM从无声变有声时重置
/// CLI 和 GUI 共享此内核组件，与 InformationEntropyGuardian 同级
/// </summary>
public interface IEmptyResponseTracker
{
    /// <summary>
    /// 当前连续空响应次数（工具调用后LLM返回空白回复的连续计数）
    /// </summary>
    int ConsecutiveEmptyCount { get; }

    /// <summary>
    /// 最大允许连续空响应次数（超过此值强制结束对话）
    /// </summary>
    int MaxConsecutiveEmpty { get; }

    /// <summary>
    /// 记录一次空响应 — 计数+1，返回是否超过阈值
    /// </summary>
    /// <returns>true 表示超过阈值应强制结束；false 表示仍在容忍范围内</returns>
    bool RecordEmptyResponse();

    /// <summary>
    /// 重置计数器 — 在以下时机调用：
    /// 1. 用户输入新对话（PreChatMiddleware）
    /// 2. LLM从无声变有声（QueryLoopMiddleware检测到非空文本响应）
    /// </summary>
    void Reset();

    /// <summary>
    /// 构建注入系统提示词的内容 — 当空响应未超阈值时，生成催促LLM继续的提示
    /// </summary>
    string BuildInterventionPrompt();
}
