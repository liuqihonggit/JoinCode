namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Agent 消息路由循环接口 — 将 ReceiveAsync + CancellationToken 契约收敛到一处
/// </summary>
public interface IAgentMessageRouteLoop : IAsyncDisposable {
    /// <summary>启动路由循环（单次语义 — 二次调用 no-op）</summary>
    void Start();
}
