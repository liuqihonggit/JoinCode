namespace Core.Hooks.ToolPermission;

public interface IPermissionHookExecutor
{
    Task RegisterHookAsync(IPermissionHook hook, CancellationToken cancellationToken = default);

    Task UnregisterHookAsync(string hookName, CancellationToken cancellationToken = default);

    IAsyncEnumerable<PermissionHookResult> ExecuteHooksAsync(
        string toolName,
        string toolUseId,
        Dictionary<string, JsonElement> input,
        string? permissionMode,
        List<PermissionUpdate>? suggestions,
        CancellationToken cancellationToken = default);
}
