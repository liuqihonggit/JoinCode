
namespace Core.Bridge;

/// <summary>
/// v1 env-based 桥核心参数 — 对齐 TS 端 replBridge.ts BridgeCoreParams
/// v1 路径需要注入式 createSession/archiveSession 等回调
/// </summary>
public sealed class BridgeCoreParams
{
    /// <summary>工作目录 — 对齐 TS 端 dir</summary>
    public required string Dir { get; init; }

    /// <summary>机器名 — 对齐 TS 端 machineName</summary>
    public required string MachineName { get; init; }

    /// <summary>Git 分支 — 对齐 TS 端 branch</summary>
    public required string Branch { get; init; }

    /// <summary>Git 仓库 URL — 对齐 TS 端 gitRepoUrl</summary>
    public string? GitRepoUrl { get; init; }

    /// <summary>会话标题</summary>
    public required string Title { get; init; }

    /// <summary>API 基础 URL</summary>
    public required string BaseUrl { get; init; }

    /// <summary>Session Ingress URL — 对齐 TS 端 sessionIngressUrl</summary>
    public required string SessionIngressUrl { get; init; }

    /// <summary>Worker 类型 — 对齐 TS 端 workerType</summary>
    public required string WorkerType { get; init; }

    /// <summary>获取访问令牌</summary>
    public required Func<string?> GetAccessToken { get; init; }

    /// <summary>创建会话 — 对齐 TS 端 createSession(environmentId, title, gitRepoUrl, branch)</summary>
    public required Func<string, string, string?, string, CancellationToken, Task<string?>> CreateSession { get; init; }

    /// <summary>归档会话 — 对齐 TS 端 archiveSession(sessionId)</summary>
    public required Func<string, CancellationToken, Task> ArchiveSession { get; init; }

    /// <summary>获取当前标题 — 对齐 TS 端 getCurrentTitle</summary>
    public Func<string>? GetCurrentTitle { get; init; }

    /// <summary>将内部消息转为 SDK 格式 — 对齐 TS 端 toSDKMessages</summary>
    public Func<string, string[]>? ToSDKMessages { get; init; }

    /// <summary>401 认证失败回调</summary>
    public Func<string, Task<bool>>? OnAuth401 { get; init; }

    /// <summary>轮询间隔配置获取 — 对齐 TS 端 getPollIntervalConfig</summary>
    public Func<BridgePollIntervalConfig>? GetPollIntervalConfig { get; init; }

    /// <summary>初始历史消息上限</summary>
    public int InitialHistoryCap { get; init; } = 200;

    /// <summary>初始消息</summary>
    public string[]? InitialMessages { get; init; }

    /// <summary>已刷新的 UUID 集合 — 对齐 TS 端 previouslyFlushedUUIDs</summary>
    public HashSet<string>? PreviouslyFlushedUUIDs { get; init; }

    /// <summary>入站消息回调</summary>
    public Action<string>? OnInboundMessage { get; init; }

    /// <summary>用户消息回调</summary>
    public Func<string, string, bool>? OnUserMessage { get; init; }

    /// <summary>权限响应回调</summary>
    public Action<JsonElement>? OnPermissionResponse { get; init; }

    /// <summary>中断回调</summary>
    public Action? OnInterrupt { get; init; }

    /// <summary>设置模型回调</summary>
    public Action<string?>? OnSetModel { get; init; }

    /// <summary>设置最大思考令牌数回调</summary>
    public Action<int?>? OnSetMaxThinkingTokens { get; init; }

    /// <summary>设置权限模式回调</summary>
    public Func<string, OperationResult>? OnSetPermissionMode { get; init; }

    /// <summary>状态变更回调</summary>
    public Action<BridgeState, string?>? OnStateChange { get; init; }

    /// <summary>获取受信设备令牌</summary>
    public Func<Task<string?>>? GetTrustedDeviceToken { get; init; }

    /// <summary>是否持久模式 — 对齐 TS 端 perpetual</summary>
    public bool Perpetual { get; init; }

    /// <summary>初始 SSE 序列号 — 对齐 TS 端 initialSSESequenceNum</summary>
    public int InitialSSESequenceNum { get; init; }

    /// <summary>是否仅出站模式</summary>
    public bool OutboundOnly { get; init; }

    /// <summary>标签</summary>
    public string[]? Tags { get; init; }

    /// <summary>并发会话服务 — 对齐 TS 端 concurrentSessions.updateSessionBridgeId</summary>
    public ConcurrentSessionService? ConcurrentSessionService { get; init; }
}

/// <summary>
/// v2 env-less 桥核心参数 — 对齐 TS 端 remoteBridgeCore.ts EnvLessBridgeParams
/// </summary>
public sealed class BridgeEnvLessParams
{
    /// <summary>API 基础 URL</summary>
    public required string BaseUrl { get; init; }

    /// <summary>组织 UUID</summary>
    public required string OrgUUID { get; init; }

    /// <summary>会话标题</summary>
    public required string Title { get; init; }

    /// <summary>获取访问令牌</summary>
    public required Func<string?> GetAccessToken { get; init; }

    /// <summary>401 认证失败回调 — 对齐 TS 端 onAuth401(staleAccessToken): Promise&lt;bool&gt;</summary>
    public Func<string, Task<bool>>? OnAuth401 { get; init; }

    /// <summary>将内部消息转为 SDK 格式 — 对齐 TS 端 toSDKMessages</summary>
    public Func<string, string[]>? ToSDKMessages { get; init; }

    /// <summary>初始历史消息上限 — 对齐 TS 端 initialHistoryCap (number)</summary>
    public int InitialHistoryCap { get; init; }

    /// <summary>初始消息</summary>
    public string[]? InitialMessages { get; init; }

    /// <summary>入站消息回调</summary>
    public Action<string>? OnInboundMessage { get; init; }

    /// <summary>用户消息回调 — 对齐 TS 端 onUserMessage(text, sessionId): bool</summary>
    public Func<string, string, bool>? OnUserMessage { get; init; }

    /// <summary>权限响应回调</summary>
    public Action<JsonElement>? OnPermissionResponse { get; init; }

    /// <summary>中断回调</summary>
    public Action? OnInterrupt { get; init; }

    /// <summary>设置模型回调</summary>
    public Action<string?>? OnSetModel { get; init; }

    /// <summary>设置最大思考令牌数回调</summary>
    public Action<int?>? OnSetMaxThinkingTokens { get; init; }

    /// <summary>设置权限模式回调</summary>
    public Func<string, OperationResult>? OnSetPermissionMode { get; init; }

    /// <summary>状态变更回调 — 对齐 TS 端 onStateChange(state, detail?)</summary>
    public Action<BridgeState, string?>? OnStateChange { get; init; }

    /// <summary>获取受信设备令牌 — 对齐 TS 端 deps.getTrustedDeviceToken</summary>
    public Func<Task<string?>>? GetTrustedDeviceToken { get; init; }

    /// <summary>是否仅出站模式</summary>
    public bool OutboundOnly { get; init; }

    /// <summary>标签</summary>
    public string[]? Tags { get; init; }
}
