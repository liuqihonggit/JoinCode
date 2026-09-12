namespace JoinCode.Abstractions.Security;

/// <summary>
/// 权限结果类，包含权限检查的结果信息
/// </summary>
public sealed class PermissionResult
{
    /// <summary>
    /// 是否已授权
    /// </summary>
    public bool IsGranted { get; }

    /// <summary>
    /// 拒绝原因（如果未授权）
    /// </summary>
    public string? DenyReason { get; }

    /// <summary>
    /// 授权过期时间（null表示永不过期）
    /// </summary>
    public DateTimeOffset? ExpirationTime { get; }

    /// <summary>
    /// 是否需要用户确认
    /// </summary>
    public bool RequiresConfirmation { get; }

    /// <summary>
    /// 确认提示信息
    /// </summary>
    public string? ConfirmationPrompt { get; }

    /// <summary>
    /// 授权是否已过期
    /// </summary>
    public bool IsExpired => ExpirationTime.HasValue && DateTimeOffset.UtcNow > ExpirationTime.Value;

    private PermissionResult(bool isGranted, string? denyReason, DateTimeOffset? expirationTime, bool requiresConfirmation, string? confirmationPrompt)
    {
        IsGranted = isGranted;
        DenyReason = denyReason;
        ExpirationTime = expirationTime;
        RequiresConfirmation = requiresConfirmation;
        ConfirmationPrompt = confirmationPrompt;
    }

    /// <summary>
    /// 创建已授权的结果
    /// </summary>
    /// <param name="expirationTime">过期时间（null表示永不过期）</param>
    /// <returns>授权结果</returns>
    public static PermissionResult Granted(DateTimeOffset? expirationTime = null)
    {
        return new PermissionResult(true, null, expirationTime, false, null);
    }

    /// <summary>
    /// 创建被拒绝的结果
    /// </summary>
    /// <param name="reason">拒绝原因</param>
    /// <returns>拒绝结果</returns>
    public static PermissionResult Denied(string reason)
    {
        return new PermissionResult(false, reason, null, false, null);
    }

    /// <summary>
    /// 创建需要确认的结果
    /// </summary>
    /// <param name="prompt">确认提示信息</param>
    /// <returns>需要确认的结果</returns>
    public static PermissionResult PendingConfirmation(string prompt)
    {
        return new PermissionResult(false, null, null, true, prompt);
    }

    /// <summary>
    /// 创建带有临时授权的结果
    /// </summary>
    /// <param name="duration">授权持续时间</param>
    /// <returns>临时授权结果</returns>
    public static PermissionResult TemporaryGrant(TimeSpan duration)
    {
        return new PermissionResult(true, null, DateTimeOffset.UtcNow.Add(duration), false, null);
    }
}
