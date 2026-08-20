namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 延迟邮件服务 — 邮件标记"将在 N 轮后自动打开，或任务结束之后注入"
/// Worker 可继续当前任务稍后再看，减少中断
/// </summary>
public interface IDeferredMailService
{
    /// <summary>
    /// 投递延迟邮件（存入待投递队列，N 轮后自动到期）
    /// </summary>
    Task DeferAsync(DeferredMail mail, CancellationToken cancellationToken = default);

    /// <summary>
    /// 轮次递减：每轮调用，剩余轮次减1，返回已到期（剩余轮次小于等于0）的邮件并从队列移除
    /// </summary>
    /// <param name="agentId">收件人 Agent ID</param>
    /// <returns>到期的邮件列表</returns>
    IReadOnlyList<DeferredMail> TickTurns(string agentId);

    /// <summary>
    /// 任务结束注入：返回该 Agent 所有未到期但需立即投递的邮件，并从队列移除
    /// </summary>
    /// <param name="agentId">收件人 Agent ID</param>
    /// <returns>所有待投递邮件</returns>
    IReadOnlyList<DeferredMail> FlushOnTaskEnd(string agentId);

    /// <summary>
    /// 获取待投递邮件（未到期，不移除）
    /// </summary>
    /// <param name="agentId">收件人 Agent ID</param>
    /// <returns>待投递邮件列表</returns>
    IReadOnlyList<DeferredMail> GetPending(string agentId);
}
