namespace JoinCode.Abstractions.Security;

/// <summary>
/// 权限决策结果 — 中间件管道中权限检查的返回值,替代异常传播
/// </summary>
public enum PermissionDecision
{
    /// <summary>允许执行</summary>
    [EnumValue("allowed")] Allowed,

    /// <summary>拒绝执行</summary>
    [EnumValue("denied")] Denied,

    /// <summary>需要用户确认 — 交互模式下触发 IPermissionConfirmationHandler.Confirm</summary>
    [EnumValue("pending")] PendingConfirmation
}
