namespace JoinCode.Abstractions.Utils;

/// <summary>
/// Git 工具名称枚举
/// </summary>
public enum GitToolName
{
    [EnumValue("git_status")] GitStatus,
    [EnumValue("git_add")] GitAdd,
    [EnumValue("git_commit")] GitCommit,
    [EnumValue("git_push")] GitPush,
    [EnumValue("git_pull")] GitPull,
    [EnumValue("git_log")] GitLog,
    [EnumValue("git_diff")] GitDiff,
    [EnumValue("git_branch")] GitBranch,
    [EnumValue("git_clone")] GitClone,
    [EnumValue("git_reset")] GitReset,
    [EnumValue("git_clean")] GitClean,
}
