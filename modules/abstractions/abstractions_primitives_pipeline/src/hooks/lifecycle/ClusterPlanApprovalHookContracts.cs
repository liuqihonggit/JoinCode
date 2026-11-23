using System.Text.Json;
using JoinCode.Abstractions.Models.Agent;

namespace JoinCode.Abstractions.Hooks;

/// <summary>
/// 集群计划审批钩子管理器接口 — 在集群计划执行前触发审批钩子,根据钩子结果决定是否继续执行
/// </summary>
public interface IClusterPlanApprovalHookManager {
    /// <summary>
    /// 异步触发集群计划审批钩子 — 根据上下文执行已注册的审批钩子并返回审批结果
    /// </summary>
    /// <param name="context">集群计划审批钩子上下文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>审批结果,指示是否应继续执行计划</returns>
    Task<ClusterPlanApprovalHookResult> OnClusterPlanApprovalAsync(ClusterPlanApprovalHookContext context, CancellationToken ct = default);
}

/// <summary>
/// 集群计划审批钩子上下文 — 携带会话标识、目标、计划与元数据
/// </summary>
public sealed partial class ClusterPlanApprovalHookContext {
    /// <summary>会话标识</summary>
    public required string SessionId { get; init; }
    /// <summary>计划目标描述</summary>
    public required string Objective { get; init; }
    /// <summary>集群计划实例</summary>
    public required ClusterPlan Plan { get; init; }
    /// <summary>附加元数据,传递给审批钩子</summary>
    public Dictionary<string, JsonElement> Metadata { get; init; } = new();
}

/// <summary>
/// 集群计划审批钩子结果 — 指示是否应继续执行计划,可携带消息与附加数据
/// </summary>
public sealed partial class ClusterPlanApprovalHookResult {
    /// <summary>是否应继续执行,默认为 true</summary>
    public bool ShouldProceed { get; init; } = true;
    /// <summary>结果消息(可选)</summary>
    public string? Message { get; init; }
    /// <summary>附加数据,供下游消费</summary>
    public Dictionary<string, JsonElement> AdditionalData { get; init; } = new();

    /// <summary>
    /// 创建"继续执行"结果
    /// </summary>
    /// <param name="message">结果消息(可选)</param>
    /// <returns>指示继续执行的审批结果</returns>
    public static ClusterPlanApprovalHookResult Proceed(string? message = null) => new() { ShouldProceed = true, Message = message };

    /// <summary>
    /// 创建"阻止执行"结果
    /// </summary>
    /// <param name="message">结果消息(可选)</param>
    /// <returns>指示阻止执行的审批结果</returns>
    public static ClusterPlanApprovalHookResult Block(string? message = null) => new() { ShouldProceed = false, Message = message };
}
