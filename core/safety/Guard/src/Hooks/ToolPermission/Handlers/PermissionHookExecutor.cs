namespace Core.Hooks.ToolPermission;

[Register]
public sealed partial class PermissionHookExecutor : IPermissionHookExecutor
{
    [Inject] private readonly IHookOrchestrator _hookOrchestrator;
    [Inject] private readonly ILogger<PermissionHookExecutor>? _logger;
    [Inject] private readonly ITelemetryService? _telemetryService;

    public Task RegisterHookAsync(IPermissionHook hook, CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("权限钩子注册: {HookName}", hook.Name);
        return Task.CompletedTask;
    }

    public Task UnregisterHookAsync(string hookName, CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("权限钩子注销: {HookName}", hookName);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<PermissionHookResult> ExecuteHooksAsync(
        string toolName,
        string toolUseId,
        Dictionary<string, JsonElement> input,
        string? permissionMode,
        List<PermissionUpdate>? suggestions,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var eventSuggestions = suggestions?.Select(s => new JoinCode.Abstractions.Hooks.PermissionUpdate
        {
            ToolName = s.ToolName,
            Action = s.Action,
            Destination = s.Destination,
            Parameters = s.Parameters
        }).ToList();

        var hookInput = HookInputFactory.ForPermissionRequest(
            toolName,
            toolUseId,
            input,
            permissionMode,
            eventSuggestions);

        await foreach (var result in _hookOrchestrator.ExecuteHooksAsync(hookInput, cancellationToken))
        {
            if (result.PermissionRequestResult != null)
            {
                var convertedResult = ConvertToToolPermissionResult(result.PermissionRequestResult);
                _telemetryService?.RecordCount("permission.hook.count", new() { ["tool"] = toolName, ["behavior"] = convertedResult.Behavior.ToValue() }, description: "Permission hook count");

                yield return new PermissionHookResult
                {
                    HookName = "PermissionRequest",
                    PermissionRequestResult = convertedResult
                };

                if (result.PreventContinuation || result.Outcome == HookOutcome.Blocking)
                {
                    yield break;
                }
            }
        }
    }

    private static PermissionRequestResult ConvertToToolPermissionResult(JoinCode.Abstractions.Hooks.PermissionRequestResult result)
    {
        return result.Behavior switch
        {
            PermissionBehavior.Allow => PermissionRequestResult.Allow(
                result is JoinCode.Abstractions.Hooks.PermissionAllowResult allow ? allow.UpdatedInput : null,
                result is JoinCode.Abstractions.Hooks.PermissionAllowResult allow2
                    ? allow2.UpdatedPermissions?.Select(u => new PermissionUpdate
                    {
                        ToolName = u.ToolName,
                        Action = u.Action,
                        Destination = u.Destination,
                        Parameters = u.Parameters
                    }).ToList()
                    : null),
            _ => PermissionRequestResult.Deny(
                result is JoinCode.Abstractions.Hooks.PermissionDenyResult deny ? deny.Message ?? "Permission denied" : "Permission denied",
                false)
        };
    }

    public Task<int> GetRegisteredHookCountAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0);
    }
}

public sealed record PermissionRequestResult
{
    public required PermissionBehavior Behavior { get; init; }
    public Dictionary<string, JsonElement>? UpdatedInput { get; init; }
    public List<PermissionUpdate>? UpdatedPermissions { get; init; }
    public string? Message { get; init; }
    public bool Interrupt { get; init; }

    public static PermissionRequestResult Allow(
        Dictionary<string, JsonElement>? updatedInput = null,
        List<PermissionUpdate>? updatedPermissions = null)
    {
        return new PermissionRequestResult
        {
            Behavior = PermissionBehavior.Allow,
            UpdatedInput = updatedInput,
            UpdatedPermissions = updatedPermissions
        };
    }

    public static PermissionRequestResult Deny(string message, bool interrupt = false)
    {
        return new PermissionRequestResult
        {
            Behavior = PermissionBehavior.Deny,
            Message = message,
            Interrupt = interrupt
        };
    }
}

public sealed record PermissionHookResult
{
    public required string HookName { get; init; }
    public PermissionRequestResult? PermissionRequestResult { get; init; }
}

public interface IPermissionHook
{
    string Name { get; }
    Task<PermissionHookResult?> ExecuteAsync(PermissionHookContext context, CancellationToken cancellationToken = default);
}

public sealed record PermissionHookContext
{
    public required string ToolName { get; init; }
    public required string ToolUseId { get; init; }
    public required Dictionary<string, JsonElement> Input { get; init; }
    public string? PermissionMode { get; init; }
    public List<PermissionUpdate>? Suggestions { get; init; }
}
