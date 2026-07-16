namespace Core.Bridge;

using JoinCode.Abstractions.Pipeline;

public sealed class HandleWorkContext : IPipelineContext
{
    public required BridgeConfig Config { get; init; }
    public required BridgeWorkItem Work { get; init; }
    public CancellationToken CancellationToken { get; init; }

    public BridgeWorkSecret? Secret { get; set; }
    public string? SessionIngressToken { get; set; }
    public string? SecretApiBaseUrl { get; set; }
    public bool UseCcrV2 { get; set; }
    public int? WorkerEpoch { get; set; }
    public string? SdkUrl { get; set; }
    public string? CreatedWorktreePath { get; set; }
    public BridgeSubprocessHandle? Handle { get; set; }

    public bool ShortCircuited { get; set; }

    public string? EnvironmentId { get; set; }

    public string? SpawnDir { get; set; }
    public Func<string?>? GetAccessToken { get; set; }
    public string? PermissionMode { get; set; }
    public Action<BridgePermissionRequest, string?>? OnPermissionRequest { get; set; }
    public Action<BridgeNdjsonActivity>? OnActivity { get; set; }
    public Action<string>? OnFirstUserMessage { get; set; }

    internal BridgeSubprocessSpawner? Spawner { get; set; }
    internal BridgeMainPollConfig? PollConfig { get; set; }

    internal Dictionary<string, BridgeSubprocessHandle> ActiveSessions { get; set; } = new();
    internal Dictionary<string, DateTime> SessionStartTimes { get; set; } = new();
    internal Dictionary<string, string> SessionWorkIds { get; set; } = new();
    internal Dictionary<string, string> SessionIngressTokens { get; set; } = new();
    internal Dictionary<string, string> SessionWorktrees { get; set; } = new();
    internal HashSet<string> CompletedWorkIds { get; set; } = new();
    internal HashSet<string> V2Sessions { get; set; } = new();
    internal Dictionary<string, string> SessionCompatIds { get; set; } = new();

    internal Func<string, CancellationToken, Task>? StopWorkAsync { get; set; }
    internal Action<Task>? TrackCleanup { get; set; }    internal Action? CapacityWake { get; set; }
    internal Action<string, Dictionary<string, string>?>? TelemetryCount { get; set; }

    bool IPipelineContext.Failed { get; set; }
    string? IPipelineContext.ErrorMessage { get; set; }
    void IPipelineContext.Fail(string message)
    {
        ((IPipelineContext)this).Failed = true;
        ((IPipelineContext)this).ErrorMessage = message;
    }

    /// <summary>
    /// 标记 Work 失败：记录完成、停止工作、唤醒容量、短路管道
    /// </summary>
    internal void FailWork(CancellationToken ct = default)
    {
        CompletedWorkIds.Add(Work.WorkId);
        if (TrackCleanup is not null && StopWorkAsync is not null)
        {
            TrackCleanup(StopWorkAsync(Work.WorkId, ct));
        }
        CapacityWake?.Invoke();
        ShortCircuited = true;
    }
}
