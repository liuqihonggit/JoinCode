
namespace Core.Bridge.Init;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// v1 env-based 桥初始化管道上下文 — 中间件间共享的可变状态
/// </summary>
public sealed class V1BridgeInitContext : ITokenValidationContext, INullCheckContext
{
    public required BridgeCoreParams Parameters { get; init; }
    public required HttpClient HttpClient { get; init; }
    public required IFileSystem FileSystem { get; init; }
    public required IReplBridgeTransportFactory TransportFactory { get; init; }
    public ILogger? Logger { get; init; }

    public string? AccessToken { get; set; }
    public BridgePointer? PriorPointer { get; set; }
    public string? PriorSessionEnvId { get; set; }
    public BridgeApiClient? ApiClient { get; set; }
    public string? EnvironmentId { get; set; }
    public string? EnvironmentSecret { get; set; }
    public string? SessionIngressUrl { get; set; }
    public string? SessionId { get; set; }
    public BridgeInitState? State { get; set; }
    public BridgeWorkPollLoop? PollLoop { get; set; }
    public IReplBridgeTransport? CurrentTransport { get; set; }
    public int V2Generation { get; set; }

    public IReplBridgeHandle? Handle { get; set; }
    public bool Failed { get; set; }
    public string? ErrorMessage { get; set; }

    Func<string?> ITokenValidationContext.GetAccessToken => Parameters.GetAccessToken;

    IEnumerable<(string Name, object? Value)> INullCheckContext.RequiredParameters =>
    [
        (nameof(Parameters), Parameters),
        (nameof(HttpClient), HttpClient),
    ];

    public void Fail(string message)
    {
        Failed = true;
        ErrorMessage = message;
        Parameters.OnStateChange?.Invoke(BridgeState.Failed, message);
    }
}

/// <summary>
/// v2 env-less 桥初始化管道上下文 — 中间件间共享的可变状态
/// </summary>
public sealed class V2BridgeInitContext : ITokenValidationContext, INullCheckContext
{
    public required BridgeEnvLessParams Parameters { get; init; }
    public required HttpClient HttpClient { get; init; }
    public required IReplBridgeTransportFactory TransportFactory { get; init; }
    public ILogger? Logger { get; init; }
    public BridgeEnvLessConfig Config { get; init; } = BridgeEnvLessConfig.GetConfig();

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
