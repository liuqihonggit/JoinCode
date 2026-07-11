namespace Core.Bridge.Gate;

using JoinCode.Abstractions.Pipeline;
using Core.Bridge.Init;

public sealed class BridgeInitGateContext : IPipelineContext
{
    public required BridgeInitOptions Options { get; init; }
    public required bool BridgeEnabled { get; init; }
    public required Func<string?> GetAccessToken { get; init; }
    public required Func<string?> GetOrgUUID { get; init; }
    public required Func<string> GetBaseUrl { get; init; }
    public required IFileSystem FileSystem { get; init; }
    public HttpClient? HttpClient { get; init; }
    public IReplBridgeTransportFactory? TransportFactory { get; init; }
    public ILogger? Logger { get; init; }
    public MiddlewarePipeline<V1BridgeInitContext>? V1Pipeline { get; init; }
    public MiddlewarePipeline<V2BridgeInitContext>? V2Pipeline { get; init; }
    public IClockService? Clock { get; init; }
    public CancellationToken CancellationToken { get; init; }

    public string? AccessToken { get; set; }
    public string? OrgUUID { get; set; }
    public string? Title { get; set; }
    public string? BaseUrl { get; set; }
    public IReplBridgeHandle? Handle { get; set; }

    public bool Failed { get; set; }
    public string? ErrorMessage { get; set; }
    public void Fail(string message) { Failed = true; ErrorMessage = message; }
}
