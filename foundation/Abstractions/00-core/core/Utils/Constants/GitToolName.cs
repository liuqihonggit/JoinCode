namespace JoinCode.Abstractions.Utils;

/// <summary>
/// Git 工具名称枚举
/// </summary>
public enum GitToolName
{
    [EnumValue("git_status")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GitStatus,

    [EnumValue("git_add")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GitAdd,

    [EnumValue("git_commit")]
    [SecurityClass("sensitive", AutoDenied = true, PlanDenied = true, AskAllowed = true)]
    GitCommit,

    [EnumValue("git_push")]
    [SecurityClass("sensitive", AutoDenied = true, PlanDenied = true, AskAllowed = true, AgentDestructive = true)]
    GitPush,

    [EnumValue("git_pull")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GitPull,

    [EnumValue("git_log")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GitLog,

    [EnumValue("git_diff")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GitDiff,

    [EnumValue("git_branch")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GitBranch,

    [EnumValue("git_clone")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GitClone,

    [EnumValue("git_reset")]
    [SecurityClass("sensitive", AutoDenied = true, PlanDenied = true, AskAllowed = true, AgentDestructive = true)]
    GitReset,

    [EnumValue("git_clean")]
    [SecurityClass("sensitive", AutoDenied = true, PlanDenied = true, AskAllowed = true, AgentDestructive = true)]
    GitClean,
}
