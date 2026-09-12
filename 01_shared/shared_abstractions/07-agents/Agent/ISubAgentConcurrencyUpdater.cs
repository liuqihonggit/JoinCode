namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 子代理并发热重载接口 — settings.json 变更时由 SubAgentConcurrencyMiddleware 调用，
/// 实时更新各组件的 SemaphoreSlim 上限（ADR 0048 热重载）。
/// 实现方：AgentCoordinator / ForkSubAgentManager / GoalGraphEngine。
/// </summary>
public interface ISubAgentConcurrencyUpdater
{
    /// <summary>
    /// 更新并发上限 — 用 Interlocked.Exchange 原子替换内部 SemaphoreSlim，旧的 Dispose。
    /// 竞态说明：正在 WaitAsync 的线程可能捕获 ObjectDisposedException，调用方应 catch 并重试。
    /// </summary>
    void UpdateConcurrencyOptions(SubAgentConcurrencyOptions options);
}
