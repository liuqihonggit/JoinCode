namespace JoinCode.Transport.Bridge;

/// <summary>
/// 会话活动类型 — 对齐 TS 端 SessionActivity
/// </summary>
public enum BridgeSessionActivity
{
    /// <summary>空闲</summary>
    [EnumValue("idle")] Idle,
    /// <summary>思考中</summary>
    [EnumValue("thinking")] Thinking,
    /// <summary>响应中</summary>
    [EnumValue("responding")] Responding,
    /// <summary>工具使用中</summary>
    [EnumValue("toolUse")] ToolUse,
    /// <summary>等待输入</summary>
    [EnumValue("waitingForInput")] WaitingForInput,
    /// <summary>需要用户操作（权限确认等）— 对齐 TS 端 requires_action</summary>
    [EnumValue("requires_action")] RequiresAction,
    /// <summary>运行中 — 对齐 TS 端 running</summary>
    [EnumValue("running")] Running,
}

/// <summary>
/// ReplBridge 传输层抽象 — 对齐 TS 端 ReplBridgeTransport
/// 将 v1/v2 的选择封装在构造站点，replBridge 只依赖此接口
///
/// - v1: HybridTransport（WS 读 + HTTP POST 写到 Session-Ingress）
/// - v2: SSETransport（读）+ CCRClient（写到 CCR v2 /worker/*）
/// </summary>
public interface IReplBridgeTransport : IAsyncDisposable
{
    /// <summary>写入单条消息</summary>
    Task WriteAsync(string message, CancellationToken ct = default);

    /// <summary>批量写入消息</summary>
    Task WriteBatchAsync(IReadOnlyList<string> messages, CancellationToken ct = default);

    /// <summary>关闭传输</summary>
    void Close();

    /// <summary>
    /// 异步关闭传输 — P1-4: 消除 Close() 内 sync-over-async 阻塞
    /// 调用方在 async 上下文中应优先使用此方法
    /// </summary>
    Task CloseAsync(CancellationToken ct = default);

    /// <summary>写路径是否就绪（v1: WS 已连接; v2: CCRClient 已初始化）</summary>
    bool IsConnectedStatus();

    /// <summary>获取状态标签（调试用）</summary>
    string GetStateLabel();

    /// <summary>设置数据接收回调</summary>
    void SetOnData(Action<string> callback);

    /// <summary>设置关闭回调</summary>
    void SetOnClose(Action<int?> callback);

    /// <summary>设置连接成功回调</summary>
    void SetOnConnect(Action callback);

    /// <summary>
    /// 设置批次丢弃回调 — 对齐 TS 端 onBatchDropped
    /// 当 maxConsecutiveFailures 导致批次被丢弃时触发
    /// </summary>
    void SetOnBatchDropped(Action<int, int> callback);

    /// <summary>发起连接（延迟到回调注册后）</summary>
    void Connect();

    /// <summary>
    /// SSE 事件流的高水位序列号 — 对齐 TS 端 getLastSequenceNum
    /// v1 始终返回 0（Session-Ingress WS 不使用 SSE 序列号）
    /// v2 返回实际值（用于传输切换时避免全量回放）
    /// </summary>
    int GetLastSequenceNum();

    /// <summary>
    /// 因 maxConsecutiveFailures 丢弃的批次计数
    /// v1: HybridTransport 的丢弃计数; v2: 始终为 0
    /// </summary>
    int DroppedBatchCount { get; }

    /// <summary>
    /// 上报 Worker 状态 — 对齐 TS 端 reportState
    /// v2: PUT /worker state; v1: no-op
    /// </summary>
    Task ReportStateAsync(BridgeSessionActivity state, CancellationToken ct = default);

    /// <summary>
    /// 上报 Worker 元数据 — 对齐 TS 端 reportMetadata
    /// v2: PUT /worker external_metadata; v1: no-op
    /// </summary>
    Task ReportMetadataAsync(Dictionary<string, JsonElement> metadata, CancellationToken ct = default);

    /// <summary>
    /// 上报事件投递状态 — 对齐 TS 端 reportDelivery
    /// v2: POST /worker/events/{id}/delivery; v1: no-op
    /// </summary>
    Task ReportDeliveryAsync(string eventId, string status, CancellationToken ct = default);

    /// <summary>
    /// 排空写队列 — 对齐 TS 端 flush
    /// v2: 等待 SerialBatchEventUploader 排空; v1: 立即返回
    /// </summary>
    Task FlushAsync(CancellationToken ct = default);
}
