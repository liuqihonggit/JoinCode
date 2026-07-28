namespace JoinCode.Abstractions.Interfaces;

using JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// 会话统计接口 — 提供会话级别的统计数据操作
/// </summary>
public interface ISessionStats
{
    /// <summary>
    /// 重置所有统计数据
    /// </summary>
    void Reset();

    /// <summary>
    /// 种子结转数据 — 从持久化存储恢复成本状态
    /// </summary>
    void SeedCarryover(long cacheHitTokens, long cacheMissTokens, decimal totalCostUsd = 0);

    /// <summary>
    /// 记录一轮对话的 token 用量
    /// </summary>
    void RecordTurn(TokenUsage usage, decimal costUsd = 0, CacheBreakResult? cacheBreak = null);
}
