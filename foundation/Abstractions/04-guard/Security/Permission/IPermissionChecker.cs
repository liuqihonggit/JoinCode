namespace JoinCode.Abstractions.Security.Permission;

public interface IPermissionChecker
{
    Task<ToolPermissionCheckResult> CheckPermissionAsync(string toolName, Dictionary<string, JsonElement>? arguments = null, CancellationToken cancellationToken = default);
}

public sealed class ToolPermissionCheckResult
{
    public bool IsApproved { get; private set; }

    public bool ConfirmationRequired { get; private set; }

    public string? Reason { get; private set; }

    private ToolPermissionCheckResult() { }

    public static ToolPermissionCheckResult Approved() => new() { IsApproved = true };

    public static ToolPermissionCheckResult Rejected(string reason) => new() { IsApproved = false, Reason = reason };

    public static ToolPermissionCheckResult PendingConfirmation(string reason) => new() { IsApproved = false, ConfirmationRequired = true, Reason = reason };
}
