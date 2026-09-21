namespace JoinCode.Abstractions.Security.Permission;

public interface IPermissionChecker {
    /// <summary>异步检查工具调用权限。</summary>
    Task<ToolPermissionCheckResult> CheckPermissionAsync(string toolName, Dictionary<string, JsonElement>? arguments = null, CancellationToken cancellationToken = default);
}

public sealed class ToolPermissionCheckResult {
    /// <summary>获取是否已批准。</summary>
    public bool IsApproved { get; private set; }

    /// <summary>获取是否需要确认。</summary>
    public bool ConfirmationRequired { get; private set; }

    /// <summary>获取原因说明。</summary>
    public string? Reason { get; private set; }

    private ToolPermissionCheckResult() { }

    /// <summary>创建已批准结果。</summary>
    public static ToolPermissionCheckResult Approved() => new() { IsApproved = true };

    /// <summary>创建已拒绝结果。</summary>
    public static ToolPermissionCheckResult Rejected(string reason) => new() { IsApproved = false, Reason = reason };

    /// <summary>创建待确认结果。</summary>
    public static ToolPermissionCheckResult PendingConfirmation(string reason) => new() { IsApproved = false, ConfirmationRequired = true, Reason = reason };
}