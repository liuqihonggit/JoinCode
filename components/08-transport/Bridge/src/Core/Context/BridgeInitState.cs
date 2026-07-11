
namespace Core.Bridge;

/// <summary>
/// 桥初始化共享状态 — 对齐 TS 端 initEnvLessBridgeCore 中的闭包变量
/// 提取为类以便在多个方法间共享可变状态
/// </summary>
public sealed class BridgeInitState
{
    public BridgeFlushGate<string> FlushGate { get; init; } = null!;
    public BoundedUUIDSet RecentPostedUUIDs { get; init; } = null!;
    public BoundedUUIDSet RecentInboundUUIDs { get; init; } = null!;
    public CancellationTokenSource InitCts { get; init; } = null!;

    /// <summary>初始历史刷新是否已完成</summary>
    public bool InitialFlushDone;

    /// <summary>初始消息 UUID 集合 — 对齐 TS 端 initialMessageUUIDs，用于 writeMessages 双层去重</summary>
    public BoundedUUIDSet? InitialMessageUUIDs;

    /// <summary>是否已拆卸</summary>
    public bool TornDown;

    /// <summary>拆卸是否已启动 — 对齐 TS 端 teardownStarted，防止重入</summary>
    public bool TeardownStarted;

    /// <summary>认证恢复是否正在进行 — 防止并发恢复</summary>
    public bool AuthRecoveryInFlight;

    /// <summary>上次传输的 SSE 序列号高水位 — 对齐 TS 端 lastTransportSequenceNum</summary>
    public int LastTransportSequenceNum;

    /// <summary>环境重建次数 — 对齐 TS 端 environmentRecreations</summary>
    public int EnvironmentRecreations;

    /// <summary>最大环境重建次数 — 对齐 TS 端 MAX_ENVIRONMENT_RECREATIONS</summary>
    public int MaxEnvironmentRecreations = 3;

    /// <summary>
    /// 标题派生闩锁 — 对齐 TS 端 userMessageCallbackDone
    /// 初始值 = !onUserMessage（无回调则跳过扫描）
    /// 当 onUserMessage 回调返回 true 时关闭（策略说"派生完成"）
    /// Strategy 2 后重置为 !onUserMessage（新会话需要重新派生标题）
    /// </summary>
    public bool UserMessageCallbackDone;
}

// IReplBridgeTransportFactory 已迁移到 JoinCode.Transport.Bridge 命名空间 (Transport.Contracts)

/// <summary>
/// v1 重连可变状态 — 替代 ref 参数，因为 async 方法不支持 ref
/// </summary>
internal sealed class V1ReconnectState
{
    public string EnvironmentId;
    public string EnvironmentSecret;
    public string SessionId;
    public int EnvironmentRecreations;

    public V1ReconnectState(string environmentId, string environmentSecret, string sessionId)
    {
        EnvironmentId = environmentId;
        EnvironmentSecret = environmentSecret;
        SessionId = sessionId;
    }
}
