namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Plan 审批消息路由器接口 — 对齐 TS plan_approval_request/response 消息分发
/// </summary>
public interface IPlanApprovalMessageRouter {
    /// <summary>
    /// 启动 Leader 侧路由：监听 plan_approval_request 并自动批准
    /// </summary>
    /// <param name="coordinatorAgentId">协调器智能体标识</param>
    void StartLeaderRouting(string coordinatorAgentId);

    /// <summary>
    /// 启动 Teammate 侧路由：监听 plan_approval_response
    /// </summary>
    /// <param name="teammateAgentId">Teammate 智能体标识</param>
    void StartTeammateRouting(string teammateAgentId);

    /// <summary>
    /// 停止 Leader 侧路由 + Teammate 侧路由（全部收尾）
    /// </summary>
    /// <returns>表示异步操作的任务</returns>
    Task StopRoutingAsync();
}
