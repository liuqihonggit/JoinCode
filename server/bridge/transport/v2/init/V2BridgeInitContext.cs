namespace Core.Bridge.Init;


/// <summary>
/// v2 env-less 桥初始化管道上下文 — 中间件间共享的可变状态
/// </summary>
public sealed class V2BridgeInitContext : ITokenValidationContext, INullCheckContext {
    /// <summary>桥接初始化参数 — 由调用方提供的配置项集合</summary>
    public required V2BridgeParams Parameters { get; init; }
    /// <summary>HTTP 客户端 — 用于桥接 API 调用</summary>
    public required HttpClient HttpClient { get; init; }
    /// <summary>桥接传输工厂 — 创建 v2 传输实例</summary>
    public required IReplBridgeTransportFactory TransportFactory { get; init; }
    /// <summary>日志记录器实例(可选)</summary>
    public ILogger? Logger { get; init; }
    /// <summary>v2 桥接配置 — 含超时、重试等参数</summary>
    public V2BridgeConfig Config { get; init; } = V2BridgeConfig.GetConfig();

    /// <summary>访问令牌 — 认证后填充</summary>
    public string? AccessToken { get; set; }
    /// <summary>会话标识 — 创建会话后填充</summary>
    public string? SessionId { get; set; }
    /// <summary>桥接远程凭据 — 含 API 基址、Worker JWT 等</summary>
    public BridgeRemoteCredentials? Credentials { get; set; }
    /// <summary>桥接传输实例 — 建立传输后填充</summary>
    public IReplBridgeTransport? Transport { get; set; }
    /// <summary>桥接初始化状态 — 跟踪连接、刷新等运行时状态</summary>
    public BridgeInitState? State { get; set; }
    /// <summary>令牌刷新调度器 — 自动续期访问令牌</summary>
    public BridgeTokenRefreshScheduler? Refresh { get; set; }

    /// <summary>桥接句柄 — 供外部控制传输生命周期</summary>
    public IReplBridgeHandle? Handle { get; set; }
    /// <summary>是否已失败 — 中间件检测后置位</summary>
    public bool Failed { get; set; }
    /// <summary>失败错误消息 — 与 Failed 配合使用</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>获取访问令牌的回调 — 实现令牌校验上下文接口</summary>
    Func<string?> ITokenValidationContext.GetAccessToken => Parameters.GetAccessToken;

    /// <summary>必填参数列表 — 实现空值检查上下文接口,用于启动前校验</summary>
    IEnumerable<(string Name, object? Value)> INullCheckContext.RequiredParameters =>
    [
        (nameof(Parameters), Parameters),
        (nameof(HttpClient), HttpClient),
        (nameof(TransportFactory), TransportFactory),
    ];

    /// <summary>
    /// 标记上下文为失败状态,并通知状态变更
    /// </summary>
    /// <param name="message">失败错误消息</param>
    public void Fail(string message) {
        Failed = true;
        ErrorMessage = message;
        Parameters.OnStateChange?.Invoke(BridgeState.Failed, message);
    }
}