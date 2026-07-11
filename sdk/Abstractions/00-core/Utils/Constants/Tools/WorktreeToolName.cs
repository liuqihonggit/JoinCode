namespace JoinCode.Abstractions.Utils;

/// <summary>
/// Worktree 工具名称枚举
/// </summary>
public enum WorktreeToolName
{
    [EnumValue("worktree_create")] WorktreeCreate,
    [EnumValue("worktree_remove")] WorktreeRemove,
    [EnumValue("worktree_list")] WorktreeList,
    [EnumValue("worktree_status")] WorktreeStatus,
    [EnumValue("worktree_cleanup")] WorktreeCleanup,
    [EnumValue("worktree_find_git")] WorktreeFindGit,
    [EnumValue("worktree_list_all")] WorktreeListAll,
    [EnumValue("EnterWorktree")] EnterWorktree,
    [EnumValue("ExitWorktree")] ExitWorktree,
}
