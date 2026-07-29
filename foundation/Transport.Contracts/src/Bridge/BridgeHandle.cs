namespace JoinCode.Transport.Bridge;

/// <summary>
/// 桥连接状态 — 对齐 TS 端 BridgeState: 'ready' | 'connected' | 'reconnecting' | 'failed'
/// </summary>
public enum BridgeState
{
    /// <summary>传输已创建但尚未连接 — 对齐 TS 端 'ready'</summary>
    [EnumValue("ready")] Ready,
    /// <summary>已连接 — 对齐 TS 端 'connected'</summary>
    [EnumValue("connected")] Connected,
    /// <summary>重连中 — 对齐 TS 端 'reconnecting'</summary>
    [EnumValue("reconnecting")] Reconnecting,
    /// <summary>失败 — 对齐 TS 端 'failed'</summary>
    [EnumValue("failed")] Failed,
    /// <summary>关闭中（CS 端扩展，用于优雅关闭流程）</summary>
    [EnumValue("closing")] Closing,
    /// <summary>已断开（CS 端扩展，用于关闭完成状态）</summary>
    [EnumValue("disconnected")] Disconnected,
}

/// <summary>
/// REPL 桥句柄接口 — 对齐 TS 端 ReplBridgeHandle
/// 供 React 树外的调用者（工具、斜杠命令）访问桥方法
/// </summary>
public interface IReplBridgeHandle
{
    /// <summary>会话 ID（cse_* 格式）— 对齐 TS 端 bridgeSessionId</summary>
    string SessionId { get; }

    /// <summary>环境 ID — 对齐 TS 端 environmentId</summary>
    string EnvironmentId { get; }

    /// <summary>Session-Ingress URL — 对齐 TS 端 sessionIngressUrl</summary>
    string SessionIngressUrl { get; }

    /// <summary>当前连接状态</summary>
    BridgeState State { get; }

    /// <summary>写入消息 — 对齐 TS 端 writeMessages(messages)</summary>
    void WriteMessages(string[] messages);

    /// <summary>写入 SDK 消息 — 对齐 TS 端 writeSdkMessages(messages)</summary>
    void WriteSdkMessages(string[] messages);

    /// <summary>发送控制请求 — 对齐 TS 端 sendControlRequest(request)</summary>
    void SendControlRequest(string requestJson);

    /// <summary>发送控制响应 — 对齐 TS 端 sendControlResponse(response)</summary>
    void SendControlResponse(string responseJson);

    /// <summary>发送取消控制请求 — 对齐 TS 端 sendControlCancelRequest(requestId)</summary>
    void SendControlCancelRequest(string requestId);

    /// <summary>发送结果消息 — 对齐 TS 端 sendResult()</summary>
    void SendResult();

    /// <summary>优雅关闭 — 对齐 TS 端 teardown()</summary>
    Task TeardownAsync(CancellationToken ct = default);

    /// <summary>刷新待发消息</summary>
    Task FlushAsync(CancellationToken ct = default);

    /// <summary>
    /// 获取当前 SSE 序列号高水位 — 对齐 TS 端 BridgeCoreHandle.getSSESequenceNum()
    /// 合并已关闭传输的检查点和当前活跃传输的实时值
    /// Daemon 调用者在关闭时持久化此值，下次启动作为 initialSSESequenceNum 传入
    /// </summary>
    int GetSSESequenceNum();
}
