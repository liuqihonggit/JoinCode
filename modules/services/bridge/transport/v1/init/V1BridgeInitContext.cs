namespace Core.Bridge.Init;


/// <summary>
/// v1 env-based 桥初始化管道上下文 — 中间件间共享的可变状态
/// </summary>
public sealed class V1BridgeInitContext : ITokenValidationContext, INullCheckContext {
    /// <summary>桥核心参数</summary>
    public required BridgeCoreParams Parameters { get; init; }
    /// <summary>HTTP 客户端</summary>
    public required HttpClient HttpClient { get; init; }
    /// <summary>文件系统抽象</summary>
    public required IFileSystem FileSystem { get; init; }
    /// <summary>桥传输工厂</summary>
    public required IReplBridgeTransportFactory TransportFactory { get; init; }
    /// <summary>日志记录器（可选）</summary>
    public ILogger? Logger { get; init; }

    /// <summary>访问令牌</summary>
    public string? AccessToken { get; set; }
    /// <summary>先前的崩溃恢复指针</summary>
    public BridgePointer? PriorPointer { get; set; }
    /// <summary>先前会话的环境 ID</summary>
    public string? PriorSessionEnvId { get; set; }
    /// <summary>桥 API 客户端</summary>
    public BridgeApiClient? ApiClient { get; set; }
    /// <summary>环境 ID</summary>
    public string? EnvironmentId { get; set; }
    /// <summary>环境密钥</summary>
    public string? EnvironmentSecret { get; set; }
    /// <summary>会话入口 URL</summary>
    public string? SessionIngressUrl { get; set; }
    /// <summary>会话 ID</summary>
    public string? SessionId { get; set; }
    /// <summary>桥初始化状态</summary>
    public BridgeInitState? State { get; set; }
    /// <summary>工作轮询循环</summary>
    public BridgeWorkPollLoop? PollLoop { get; set; }
    /// <summary>当前传输实例</summary>
    public IReplBridgeTransport? CurrentTransport { get; set; }
    /// <summary>V2 代次</summary>
    public int V2Generation { get; set; }

    /// <summary>桥句柄</summary>
    public IReplBridgeHandle? Handle { get; set; }
    /// <summary>是否失败</summary>
    public bool Failed { get; set; }
    /// <summary>错误消息</summary>
    public string? ErrorMessage { get; set; }

    Func<string?> ITokenValidationContext.GetAccessToken => Parameters.GetAccessToken;

    IEnumerable<(string Name, object? Value)> INullCheckContext.RequiredParameters =>
    [
        (nameof(Parameters), Parameters),
        (nameof(HttpClient), HttpClient),
    ];

    /// <summary>
    /// 标记上下文为失败状态
    /// </summary>
    /// <param name="message">失败消息</param>
    public void Fail(string message) {
        Failed = true;
        ErrorMessage = message;
        Parameters.OnStateChange?.Invoke(BridgeState.Failed, message);
    }
}