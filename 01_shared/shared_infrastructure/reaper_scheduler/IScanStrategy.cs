namespace Infrastructure.ReaperScheduler;

/// <summary>
/// 扫描策略接口 — 按会话隔离扫描, EntityReaper/ShellProcessWatchdog 各为一个策略
/// ReaperScheduler 空闲时按会话轮流调用各策略的 Scan
/// </summary>
public interface IScanStrategy
{
    /// <summary>策略名称 — 诊断用</summary>
    string Name { get; }

    /// <summary>
    /// 扫描指定会话作用域 — 只扫描该会话的 Entity, 不扫全局
    /// </summary>
    void Scan(SessionScope scope);
}
