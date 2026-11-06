namespace Core.Security.DangerClassification;

/// <summary>
/// Git 子命令统一目录 — 集中所有 Git 子命令白名单/黑名单的定义,作为 Git 子命令分类的唯一数据源
/// <para>
/// 三个语义不同的集合:
/// <list type="bullet">
/// <item><see cref="ReadOnlySubcommands"/> — 纯只读子命令(25个),不修改仓库状态,供 CommandDangerClassifier.IsGitReadOnlySubcommand 委托</item>
/// <item><see cref="SafeSubcommands"/> — 安全子命令(只读+部分写入,48个),宽放模式日常工作命令,供 ReadOnlyCommandDetector.SafeGitSubcommands 委托</item>
/// <item><see cref="DangerousSubcommands"/> — 危险子命令(9个),真正破坏性操作需确认,供 ReadOnlyCommandDetector.DangerousGitSubcommands 委托</item>
/// </list>
/// 消费方禁止重复硬编码,必须委托本目录。
/// </para>
/// </summary>
public static class GitCommandCatalog {
    /// <summary>
    /// 纯只读 Git 子命令集合 — 不修改仓库状态(25个)
    /// <para>
    /// 供 CommandDangerClassifier.IsGitReadOnlySubcommand 委托消费。
    /// branch/stash/config/fetch 有条件只读(参数决定),由消费方额外判断,本集合仅列出子命令名。
    /// </para>
    /// </summary>
    public static readonly FrozenSet<string> ReadOnlySubcommands = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "status", "log", "diff", "show", "blame", "reflog", "describe", "shortlog",
        "ls-files", "ls-tree", "cat-file", "rev-parse", "rev-list", "name-rev",
        "cherry", "cherry-pick",
        "branch",
        "remote", "stash",
        "config",
        "fetch",
        "grep", "count-objects", "fsck", "gc",
        "help", "version", "var");

    /// <summary>
    /// 安全 Git 子命令集合 — 宽放模式:日常工作命令无条件放行(52个,含 ReadOnlySubcommands 全部只读命令)
    /// <para>
    /// 供 ReadOnlyCommandDetector.SafeGitSubcommands 委托消费。
    /// 包含只读子命令 + 部分写入子命令(add/commit/mv 等),语义比 ReadOnlySubcommands 宽松。
    /// 已补全 ReadOnlySubcommands 中的纯只读命令(count-objects/fsck/gc/var),确保安全 ⊃ 只读。
    /// </para>
    /// </summary>
    public static readonly FrozenSet<string> SafeSubcommands = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "status", "log", "show", "diff", "branch", "tag", "remote", "config",
        "help", "version", "stash", "blame", "annotate", "describe",
        "shortlog", "reflog", "ls-files", "ls-tree", "ls-remote",
        "name-rev", "rev-parse", "rev-list", "merge-base",
        "cherry", "cherry-pick",
        "grep", "whatchanged", "show-branch", "verify-pack",
        "cat-file", "for-each-ref", "worktree",
        "count-objects", "fsck", "gc", "var",
        "add", "commit", "mv", "restore", "switch", "checkout",
        "fetch", "pull", "merge", "rebase",
        "init", "clone", "submodule", "am", "apply", "notes");

    /// <summary>
    /// 危险 Git 子命令集合 — 仅真正破坏性操作需确认(9个)
    /// <para>
    /// 供 ReadOnlyCommandDetector.DangerousGitSubcommands 委托消费。
    /// push 推送到远程不可撤回,reset/clean 可能丢失提交,filter-branch 重写历史等。
    /// </para>
    /// </summary>
    public static readonly FrozenSet<string> DangerousSubcommands = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "push", "reset", "rm", "clean",
        "format-patch", "send-email", "filter-branch", "replace", "update-ref");
}
