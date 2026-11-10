namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Swarm 权限消息路由器接口 — 在邮箱与权限回调服务/请求处理器之间路由权限消息
/// </summary>
public interface ISwarmPermissionMessageRouter {
    /// <summary>
    /// 启动 Leader 侧消息路由，监听权限请求
    /// </summary>
    /// <param name="coordinatorAgentId">协调器智能体标识</param>
    void StartRouting(string coordinatorAgentId);

    /// <summary>
    /// 启动 Worker 侧响应路由，监听权限响应消息
    /// </summary>
    /// <param name="workerAgentId">Worker 智能体标识</param>
    void StartWorkerResponseRouting(string workerAgentId);

    /// <summary>
    /// 异步停止消息路由并释放相关资源
    /// </summary>
    /// <returns>表示异步操作的任务</returns>
    Task StopRoutingAsync();
}
