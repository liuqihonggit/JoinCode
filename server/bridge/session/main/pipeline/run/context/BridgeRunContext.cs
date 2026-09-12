namespace Core.Bridge;

public sealed class BridgeRunContext : PipelineContextBase
{
    public required BridgeMainArgs Args { get; init; }
    public CancellationToken CancellationToken { get; init; }

    public BridgeMainResult? EarlyResult { get; set; }
    public string? AccessToken { get; set; }
    public string? BaseUrl { get; set; }
    public string? ResumeSessionId { get; set; }
    public string? ReuseEnvironmentId { get; set; }
    public string? ResumePointerDir { get; set; }
    public bool IsResuming { get; set; }
    public BridgeSpawnMode? EffectiveSpawnMode { get; set; }
    public BridgeSpawnModeSource SpawnModeSource { get; set; } = BridgeSpawnModeSource.GateDefault;
    public BridgeConfig? Config { get; set; }
    public string? InitialSessionId { get; set; }
    public BridgeTokenRefreshScheduler? TokenRefreshScheduler { get; set; }
}
