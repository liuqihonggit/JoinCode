namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 域名黑名单预检结果
/// </summary>
public enum DomainCheckResult
{
    [EnumValue("allowed")]
    Allowed,
    [EnumValue("blocked")]
    Blocked,
    [EnumValue("check_failed")]
    CheckFailed
}
