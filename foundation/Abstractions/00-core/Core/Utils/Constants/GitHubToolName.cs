namespace JoinCode.Abstractions.Utils;

/// <summary>
/// GitHub CLI 工具名称枚举 — 对应 gh 子命令的 MCP 工具暴露
/// <para>安全级别：readonly=只读查询, safe-write=变更操作(合并/关闭/创建/下载等)</para>
/// </summary>
public enum GitHubToolName
{
    // === PR 全套 ===
    [EnumValue("gh_pr_view")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhPrView,

    [EnumValue("gh_pr_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhPrList,

    [EnumValue("gh_pr_diff")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhPrDiff,

    [EnumValue("gh_pr_checks")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhPrChecks,

    [EnumValue("gh_pr_merge")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhPrMerge,

    [EnumValue("gh_pr_checkout")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhPrCheckout,

    [EnumValue("gh_pr_close")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhPrClose,

    [EnumValue("gh_pr_reopen")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhPrReopen,

    // === Run 全套 ===
    [EnumValue("gh_run_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhRunList,

    [EnumValue("gh_run_view")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhRunView,

    [EnumValue("gh_run_rerun")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhRunRerun,

    [EnumValue("gh_run_cancel")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhRunCancel,

    // === Release 全套 ===
    [EnumValue("gh_release_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhReleaseList,

    [EnumValue("gh_release_view")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhReleaseView,

    [EnumValue("gh_release_create")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhReleaseCreate,

    [EnumValue("gh_release_download")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhReleaseDownload,

    [EnumValue("gh_release_upload")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhReleaseUpload,

    [EnumValue("gh_release_delete")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhReleaseDelete,

    // === Issue 全套 ===
    [EnumValue("gh_issue_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhIssueList,

    [EnumValue("gh_issue_view")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhIssueView,

    [EnumValue("gh_issue_create")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhIssueCreate,

    [EnumValue("gh_issue_close")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhIssueClose,

    [EnumValue("gh_issue_comment")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhIssueComment,

    // === Repo 全套 ===
    [EnumValue("gh_repo_view")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhRepoView,

    [EnumValue("gh_repo_clone")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhRepoClone,

    [EnumValue("gh_repo_create")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhRepoCreate,

    [EnumValue("gh_repo_fork")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhRepoFork,

    [EnumValue("gh_repo_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhRepoList,

    // === 通用 API 调用 ===
    [EnumValue("gh_api")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhApi,
}
