
namespace Core.Hooks.Lifecycle;

using JoinCode.Abstractions.Models.Agent;
using JoinCode.Abstractions.Models.Goal;

public interface IClusterPlanApprovalHookManager
{
    Task<ClusterPlanApprovalHookResult> OnClusterPlanApprovalAsync(ClusterPlanApprovalHookContext context, CancellationToken ct = default);
}

public sealed partial class ClusterPlanApprovalHookContext
{
    public required string SessionId { get; init; }
    public required string Objective { get; init; }
    public required ClusterPlan Plan { get; init; }
    public Dictionary<string, JsonElement> Metadata { get; init; } = new();
}

public sealed partial class ClusterPlanApprovalHookResult
{
    public bool ShouldProceed { get; init; } = true;
    public string? Message { get; init; }
    public Dictionary<string, JsonElement> AdditionalData { get; init; } = new();

    public static ClusterPlanApprovalHookResult Proceed(string? message = null) => new() { ShouldProceed = true, Message = message };
    public static ClusterPlanApprovalHookResult Block(string? message = null) => new() { ShouldProceed = false, Message = message };
}

[Register(typeof(IClusterPlanApprovalHookManager), ServiceLifetime.Singleton)]
public sealed partial class ClusterPlanApprovalHookManager : ServiceEntity, IClusterPlanApprovalHookManager
{
    private readonly IHookOrchestrator _orchestrator;
    private readonly ILogger<ClusterPlanApprovalHookManager>? _logger;

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    public ClusterPlanApprovalHookManager(IHookOrchestrator orchestrator, ILogger<ClusterPlanApprovalHookManager>? logger = null)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    public async Task<ClusterPlanApprovalHookResult> OnClusterPlanApprovalAsync(
        ClusterPlanApprovalHookContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var payload = new Dictionary<string, JsonElement>
        {
            ["session_id"] = JsonElementHelper.FromString(context.SessionId),
            ["objective"] = JsonElementHelper.FromString(context.Objective),
            ["is_decomposable"] = JsonElementHelper.FromBoolean(context.Plan.Decomposition.IsDecomposable),
            ["sub_task_count"] = JsonElementHelper.FromInt64(context.Plan.Decomposition.SubTasks.Count),
        };

        if (context.Plan.ValidationResult is not null)
        {
            payload["is_valid"] = JsonElementHelper.FromBoolean(context.Plan.ValidationResult.IsValid);
        }

        foreach (var (key, value) in context.Metadata)
        {
            payload[key] = value;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DefaultTimeout);

        try
        {
            await foreach (var hookResult in _orchestrator.ExecuteHooksAsync(
                HookEvent.ClusterPlanApproval,
                payload,
                sessionId: context.SessionId,
                cancellationToken: cts.Token).ConfigureAwait(false))
            {
                if (hookResult.Outcome == HookOutcome.Blocking)
                {
                    _logger?.LogInformation("Cluster plan approval blocked by hook: {Message}", hookResult.Message);
                    return ClusterPlanApprovalHookResult.Block(hookResult.Message);
                }

                if (hookResult.PreventContinuation)
                {
                    return ClusterPlanApprovalHookResult.Block(hookResult.Message);
                }
            }

            return ClusterPlanApprovalHookResult.Proceed();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _logger?.LogWarning("Cluster plan approval hook timed out after {Timeout}s, auto-proceeding", DefaultTimeout.TotalSeconds);
            return ClusterPlanApprovalHookResult.Proceed($"审批超时 {DefaultTimeout.TotalSeconds}s，自动放行");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Cluster plan approval hook failed, blocking for safety");
            return ClusterPlanApprovalHookResult.Block($"审批异常，安全阻塞: {ex.Message}");
        }
    }
}
