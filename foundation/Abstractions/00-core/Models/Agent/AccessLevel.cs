namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 团队路径访问级别枚举 — 替代 TeamAllowedPath 中的 "read" 硬编码字符串
/// </summary>
public enum AccessLevel
{
    [EnumValue("read")] Read = 0,
    [EnumValue("write")] Write = 1,
    [EnumValue("admin")] Admin = 2
}
