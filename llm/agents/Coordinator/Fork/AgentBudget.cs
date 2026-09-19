namespace Core.Agents.Coordinator;

/// <summary>
/// Agent 预算值对象 — 不可变配置，创建时设定，运行期间不修改
/// 包含 Token 预算上限等预算约束
/// </summary>
public sealed record AgentBudget {
    /// <summary>Token 预算上限（null 表示无限制）</summary>
    public int? TokenBudget { get; init; }

    /// <summary>
    /// 构造 Agent 预算值对象
    /// </summary>
    /// <param name="tokenBudget">Token 预算上限</param>
    public AgentBudget(int? tokenBudget = null) {
        TokenBudget = tokenBudget;
    }
}