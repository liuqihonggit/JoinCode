namespace JoinCode.Abstractions.Models.ErrorRecovery;

/// <summary>
/// 工具错误分类枚举 — 统一错误关键词匹配的分类结果
/// 与 Exceptions.ErrorCategory（异常分类）不同，此枚举用于工具执行错误的语义分类
/// </summary>
public enum ToolErrorCategory
{
    [EnumValue("permission")] Permission,
    [EnumValue("not_found")] NotFound,
    [EnumValue("timeout")] Timeout,
    [EnumValue("command_not_found")] CommandNotFound,
    [EnumValue("access_denied")] AccessDenied,
    [EnumValue("unknown")] Unknown,
}
