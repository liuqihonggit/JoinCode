namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 子智能体摘要客户端 — L2 自摘要层调 LLM 压缩子智能体输出
/// <para>实现在上层（Composition/Brain），Agents 层只依赖此接口。</para>
/// <para>依赖倒置：Agents 不能依赖 Brain/Dream，通过此接口解耦。</para>
/// </summary>
public interface ISubAgentSummaryClient
{
    /// <summary>
    /// 将文本压缩为不超过 maxOutputTokens 的连贯摘要
    /// </summary>
    /// <param name="text">待摘要的完整文本</param>
    /// <param name="agentId">子智能体标识（用于日志/追踪）</param>
    /// <param name="maxOutputTokens">摘要最大 token 数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>摘要文本；null 表示失败（调用方应走 L3 落盘兜底）</returns>
    Task<string?> SummarizeAsync(string text, string agentId, int maxOutputTokens, CancellationToken cancellationToken = default);
}
