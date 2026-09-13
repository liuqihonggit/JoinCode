namespace Core.Bridge.Init;


/// <summary>
/// v2 env-less 桥初始化管道上下文 — 中间件间共享的可变状态
/// </summary>
public sealed class V2BridgeInitContext : ITokenValidationContext, INullCheckContext
{
    public required V2BridgeParams Parameters { get; init; }
    public required HttpClient HttpClient { get; init; }
    public required IReplBridgeTransportFactory TransportFactory { get; init; }
    public ILogger? Logger { get; init; }
    public V2BridgeConfig Config { get; init; } = V2BridgeConfig.GetConfig();

    public string? AccessToken { get; set; }
    public string? SessionId { get; set; }
    public BridgeRemoteCredentials? Credentials { get; set; }
    public IReplBridgeTransport? Transport { get; set; }
    public BridgeInitState? State { get; set; }
    public BridgeTokenRefreshScheduler? Refresh { get; set; }

    public IReplBridgeHandle? Handle { get; set; }
    public bool Failed { get; set; }
    public string? ErrorMessage { get; set; }

    Func<string?> ITokenValidationContext.GetAccessToken => Parameters.GetAccessToken;

    IEnumerable<(string Name, object? Value)> INullCheckContext.RequiredParameters =>
    [
        (nameof(Parameters), Parameters),
        (nameof(HttpClient), HttpClient),
        (nameof(TransportFactory), TransportFactory),
    ];

    public void Fail(string message)
    {
        Failed = true;
        ErrorMessage = message;
        Parameters.OnStateChange?.Invoke(BridgeState.Failed, message);
    }
}
