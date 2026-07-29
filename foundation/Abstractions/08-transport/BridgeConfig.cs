namespace JoinCode.Abstractions.Transport;

/// <summary>
/// 子进程生成模式 — 对齐 TS 端 SpawnMode
/// </summary>
public enum BridgeSpawnMode
{
    /// <summary>单会话模式</summary>
    [EnumValue("single-session")]
    SingleSession,
    /// <summary>Worktree 隔离模式</summary>
    [EnumValue("worktree")]
    Worktree,
    /// <summary>同目录模式</summary>
    [EnumValue("same-dir")]
    SameDir,
}

/// <summary>
/// SpawnMode 来源追踪 — 对齐 TS 端 SpawnModeSource
/// </summary>
public enum BridgeSpawnModeSource
{
    /// <summary>恢复会话 — 对齐 TS 端 'resume'</summary>
    [EnumValue("resume")]
    Resume,
    /// <summary>命令行参数 — 对齐 TS 端 'flag'</summary>
    [EnumValue("flag")]
    Flag,
    /// <summary>已保存偏好 — 对齐 TS 端 'saved'</summary>
    [EnumValue("saved")]
    Saved,
    /// <summary>默认兜底 — 对齐 TS 端 'gate_default'</summary>
    [EnumValue("gate_default")]
    GateDefault,
}

public class BridgeConfig
{
    // ===== 服务端配置（CS 独有）=====

    public bool Enabled { get; set; } = false;
    public TransportProtocol Protocol { get; set; } = TransportProtocol.WebSocket;
    public string WebSocketEndpoint { get; set; } = WorkflowConstants.Paths.DefaultWebSocketEndpoint;
    public string SseEndpoint { get; set; } = WorkflowConstants.Paths.DefaultSseEndpoint;
    public bool AutoReconnect { get; set; } = true;
    public int MaxReconnectAttempts { get; set; } = WorkflowConstants.Retry.MaxReconnectAttempts;

    /// <summary>JWT 签名密钥，为空时自动生成</summary>
    public string JwtSecretKey { get; set; } = string.Empty;

    /// <summary>JWT Token 过期时间（秒）</summary>
    public int JwtExpirationSeconds { get; set; } = 3600;

    /// <summary>AES-256 加密密钥（Base64 编码），为空时自动生成</summary>
    public string EncryptionKeyBase64 { get; set; } = string.Empty;

    /// <summary>远程 API 密钥</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>远程 API 超时时间（秒）</summary>
    public int ApiTimeoutSeconds { get; set; } = 30;

    /// <summary>会话超时时间（分钟）</summary>
    public int SessionTimeoutMinutes { get; set; } = 30;

    /// <summary>容量伸缩最小实例数</summary>
    public int CapacityMinInstances { get; set; } = 1;

    /// <summary>容量伸缩最大实例数</summary>
    public int CapacityMaxInstances { get; set; } = 5;

    /// <summary>扩容阈值百分比（0-100）</summary>
    public double CapacityScaleUpThreshold { get; set; } = 80.0;

    /// <summary>缩容阈值百分比（0-100）</summary>
    public double CapacityScaleDownThreshold { get; set; } = 20.0;

    // ===== 客户端配置（对齐 TS 端 BridgeConfig）=====

    /// <summary>工作目录 — 对齐 TS 端 dir</summary>
    public string Dir { get; set; } = string.Empty;

    /// <summary>机器名 — 对齐 TS 端 machineName</summary>
    public string MachineName { get; set; } = Environment.MachineName;

    /// <summary>Git 分支 — 对齐 TS 端 branch</summary>
    public string Branch { get; set; } = string.Empty;

    /// <summary>Git 仓库 URL — 对齐 TS 端 gitRepoUrl</summary>
    public string? GitRepoUrl { get; set; }

    /// <summary>最大并发会话数 — 对齐 TS 端 maxSessions</summary>
    public int MaxSessions { get; set; } = 5;

    /// <summary>子进程生成模式 — 对齐 TS 端 spawnMode</summary>
    public BridgeSpawnMode SpawnMode { get; set; } = BridgeSpawnMode.SingleSession;

    /// <summary>SpawnMode 来源追踪 — 对齐 TS 端 spawnModeSource</summary>
    public BridgeSpawnModeSource SpawnModeSource { get; set; } = BridgeSpawnModeSource.GateDefault;

    /// <summary>详细日志 — 对齐 TS 端 verbose</summary>
    public bool Verbose { get; set; }

    /// <summary>沙箱模式 — 对齐 TS 端 sandbox</summary>
    public bool Sandbox { get; set; }

    /// <summary>Bridge 实例 UUID — 对齐 TS 端 bridgeId</summary>
    public string BridgeId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Worker 类型元数据 — 对齐 TS 端 workerType</summary>
    public string WorkerType { get; set; } = "bridge";

    /// <summary>环境注册 UUID — 对齐 TS 端 environmentId</summary>
    public string EnvironmentId { get; set; } = string.Empty;

    /// <summary>重连环境 ID — 对齐 TS 端 reuseEnvironmentId</summary>
    public string? ReuseEnvironmentId { get; set; }

    /// <summary>API 基础 URL — 对齐 TS 端 apiBaseUrl</summary>
    public string ApiBaseUrl { get; set; } = string.Empty;

    /// <summary>Session Ingress URL — 对齐 TS 端 sessionIngressUrl</summary>
    public string SessionIngressUrl { get; set; } = string.Empty;

    /// <summary>调试文件路径 — 对齐 TS 端 debugFile</summary>
    public string? DebugFile { get; set; }

    /// <summary>会话超时（毫秒）— 对齐 TS 端 sessionTimeoutMs</summary>
    public int SessionTimeoutMs { get; set; } = 0;
}
