namespace JoinCode.Abstractions.Utils;

/// <summary>
/// Worktree 工具名称枚举
/// </summary>
public enum WorktreeToolName
{
    [EnumValue("worktree_create")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    WorktreeCreate,

    [EnumValue("worktree_remove")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    WorktreeRemove,

    [EnumValue("worktree_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    WorktreeList,

    [EnumValue("worktree_status")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    WorktreeStatus,

    [EnumValue("worktree_cleanup")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    WorktreeCleanup,

    [EnumValue("worktree_find_git")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    WorktreeFindGit,

    [EnumValue("worktree_list_all")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    WorktreeListAll,

    [EnumValue("worktree_merge")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    WorktreeMerge,

    [EnumValue("EnterWorktree")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    EnterWorktree,

    [EnumValue("ExitWorktree")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    ExitWorktree,
}
