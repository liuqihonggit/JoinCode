namespace JoinCode.Abstractions.Models.Git;

/// <summary>
/// Git 分支操作类型枚举 — 替代 GitBranchAsync 中的 "create"/"switch"/"delete" 硬编码字符串
/// </summary>
public enum GitBranchOperation
{
    [EnumValue("create")] Create = 0,
    [EnumValue("switch")] Switch = 1,
    [EnumValue("delete")] Delete = 2
}
