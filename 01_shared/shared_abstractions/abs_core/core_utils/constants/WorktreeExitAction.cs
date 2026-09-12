namespace JoinCode.Abstractions.Utils;

/// <summary>
/// Worktree退出操作枚举
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 WorktreeExitActionConstants + WorktreeExitActionExtensions
/// </summary>
public enum WorktreeExitAction
{
    /// <summary>保留Worktree</summary>
    [EnumValue("keep")] Keep = 0,

    /// <summary>移除Worktree</summary>
    [EnumValue("remove")] Remove = 1
}
