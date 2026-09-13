namespace Core.Hooks.Lifecycle;

/// <summary>停止 Hook 管理器接口 — 在会话停止前执行 Stop Hook</summary>
public interface IStopHookManager
{
    /// <summary>触发 Stop Hook,返回是否允许停止</summary>
    Task<StopHookResult> OnStopAsync(StopHookContext context, CancellationToken ct = default);
}

/// <summary>Stop Hook 上下文 — 包含会话 ID、停止原因和元数据</summary>
public sealed partial class StopHookContext
{
    /// <summary>会话 ID</summary>
    public required string SessionId { get; init; }
    /// <summary>停止原因</summary>
    public required string Reason { get; init; }
    /// <summary>附加元数据</summary>
    public Dictionary<string, JsonElement> Metadata { get; init; } = new();
}

/// <summary>Stop Hook 执行结果 — 包含是否允许停止、消息和附加数据</summary>
public sealed partial class StopHookResult
{
    /// <summary>是否应该停止</summary>
    public bool ShouldStop { get; init; } = true;
    /// <summary>结果消息</summary>
    public string? Message { get; init; }
    /// <summary>附加数据</summary>
    public Dictionary<string, JsonElement> AdditionalData { get; init; } = new();

    /// <summary>创建允许继续的结果（ShouldStop=false）</summary>
    public static StopHookResult Continue(string? message = null) => new() { ShouldStop = false, Message = message };
    /// <summary>创建停止的结果（ShouldStop=true）</summary>
    public static StopHookResult Stop(string? message = null) => new() { ShouldStop = true, Message = message };
}

/// <summary>
/// Stop Hook 管理器实现 — 通过 IHookOrchestrator 执行 Stop Hook,支持阻止停止
/// </summary>
[Register(typeof(IStopHookManager), ServiceLifetime.Singleton)]
public sealed partial class StopHookManager : ServiceEntity, IStopHookManager
{
    private readonly IHookOrchestrator _orchestrator;
    private readonly ILogger<StopHookManager>? _logger;
    private readonly ITelemetryService? _telemetryService;

    /// <summary>
    /// 构造函数 — 注入 Hook 编排器、可选的日志器和遥测服务
    /// </summary>
    public StopHookManager(IHookOrchestrator orchestrator, ILogger<StopHookManager>? logger = null, ITelemetryService? telemetryService = null)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger;
        _telemetryService = telemetryService;
    }

    /// <inheritdoc />
    public async Task<StopHookResult> OnStopAsync(StopHookContext context, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, JsonElement>
        {
            ["sessionId"] = JsonElementHelper.FromString(context.SessionId),
            ["reason"] = JsonElementHelper.FromString(context.Reason),
            ["metadata"] = JsonSerializer.SerializeToElement(context.Metadata, HooksJsonContext.Default.DictionaryStringJsonElement)
        };

        var additionalData = new Dictionary<string, JsonElement>();

        await foreach (var result in _orchestrator.ExecuteHooksAsync(
            HookEvent.Stop,
            payload,
            sessionId: context.SessionId,
            cancellationToken: ct).ConfigureAwait(false))
        {
            if (result.Outcome == HookOutcome.Blocking)
            {
                _logger?.LogInformation("Stop hook prevented stop for session {SessionId}: {Message}",
                    context.SessionId, result.Message);

                RecordStopHookMetrics(context.Reason, true);
                return StopHookResult.Continue(result.Message);
            }

            if (result.PreventContinuation)
            {
                return StopHookResult.Continue(result.Message);
            }

            if (result.UpdatedInput != null)
            {
                foreach (var kvp in result.UpdatedInput)
                {
                    additionalData[kvp.Key] = kvp.Value;
                }
            }
        }

        RecordStopHookMetrics(context.Reason, false);

        return new StopHookResult { AdditionalData = additionalData };
    }

    private void RecordStopHookMetrics(string reason, bool prevented)
        => _telemetryService?.RecordCount("hook.stop.count", new() { ["reason"] = reason, ["prevented"] = prevented.ToString() }, description: "Stop hook execution count");
}
