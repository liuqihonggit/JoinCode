namespace JoinCode.Reasoning.Agents;

/// <summary>
/// 推理Agent接口 — 三权分立角色的统一抽象
/// </summary>
public interface IReasoningAgent
{
    /// <summary>
    /// Agent角色
    /// </summary>
    AgentRole Role { get; }

    /// <summary>
    /// Agent名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 执行推理 — 接收完整上下文，不持有引擎引用
    /// </summary>
    Task<AgentAction> ReasonAsync(ReasoningContext context, CancellationToken ct);
}
