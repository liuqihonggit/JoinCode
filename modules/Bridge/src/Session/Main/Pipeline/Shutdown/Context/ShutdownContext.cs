namespace Core.Bridge;

using JoinCode.Abstractions.Pipeline;

public sealed class ShutdownContext : IPipelineContext
{
    public bool IsResuming { get; init; }
    public bool FatalExit { get; init; }
    public string? EnvironmentId { get; init; }
    public BridgeSpawnMode SpawnMode { get; init; }
    public string? ResumePointerDir { get; init; }

    internal Dictionary<string, BridgeSubprocessHandle> ActiveSessions { get; set; } = new();
    internal Dictionary<string, string> SessionCompatIds { get; set; } = new();
    internal BridgeSubprocessSpawner? Spawner { get; set; }
    internal BridgeApiClient? ApiClient { get; set; }
    internal BridgePointerService? PointerService { get; set; }
    internal string? WorkingDirectory { get; set; }
    internal Func<string, CancellationToken, Task>? ArchiveSession { get; set; }
    internal Action? UnregisterKeyboardListener { get; set; }
    internal CancellationTokenSource? LoopCts { get; set; }
    internal Task? LoopTask { get; set; }
    internal Timer? PointerRefreshTimer { get; set; }

    bool IPipelineContext.Failed { get; set; }
    string? IPipelineContext.ErrorMessage { get; set; }
    void IPipelineContext.Fail(string message)
    {
        ((IPipelineContext)this).Failed = true;
        ((IPipelineContext)this).ErrorMessage = message;
    }
}
