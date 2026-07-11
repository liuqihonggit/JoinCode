namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 域名黑名单预检结果
/// </summary>
public enum DomainCheckResult
{
    Allowed,
    Blocked,
    CheckFailed
}
