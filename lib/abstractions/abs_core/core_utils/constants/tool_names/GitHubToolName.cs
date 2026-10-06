namespace JoinCode.Abstractions.Utils;

/// <summary>
/// GitHub CLI 工具名称枚举 — 对应 gh 子命令的 MCP 工具暴露
/// <para>安全级别：readonly=只读查询, safe-write=变更操作(合并/关闭/创建/下载等)</para>
/// </summary>
public enum GitHubToolName {
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

    [EnumValue("gh_pr_wait")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhPrWait,

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

    [EnumValue("gh_pr_create")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhPrCreate,

    [EnumValue("gh_pr_comment")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhPrComment,

    [EnumValue("gh_pr_edit")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhPrEdit,

    [EnumValue("gh_pr_review")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhPrReview,

    [EnumValue("gh_pr_lock")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhPrLock,

    [EnumValue("gh_pr_unlock")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhPrUnlock,

    [EnumValue("gh_pr_status")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhPrStatus,

    [EnumValue("gh_pr_ready")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhPrReady,

    [EnumValue("gh_pr_revert")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhPrRevert,

    [EnumValue("gh_pr_update_branch")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhPrUpdateBranch,

    // === Run 全套 ===
    [EnumValue("gh_run_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhRunList,

    [EnumValue("gh_run_view")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhRunView,

    [EnumValue("gh_run_wait")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhRunWait,

    [EnumValue("gh_run_rerun")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhRunRerun,

    [EnumValue("gh_run_cancel")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhRunCancel,

    [EnumValue("gh_run_download")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhRunDownload,

    [EnumValue("gh_run_delete")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhRunDelete,

    [EnumValue("gh_run_watch")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhRunWatch,

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

    [EnumValue("gh_release_delete_asset")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhReleaseDeleteAsset,

    [EnumValue("gh_release_edit")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhReleaseEdit,

    [EnumValue("gh_release_verify")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhReleaseVerify,

    [EnumValue("gh_release_verify_asset")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhReleaseVerifyAsset,

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

    [EnumValue("gh_issue_reopen")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhIssueReopen,

    [EnumValue("gh_issue_edit")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhIssueEdit,

    [EnumValue("gh_issue_delete")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhIssueDelete,

    [EnumValue("gh_issue_lock")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhIssueLock,

    [EnumValue("gh_issue_unlock")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhIssueUnlock,

    [EnumValue("gh_issue_status")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhIssueStatus,

    [EnumValue("gh_issue_develop")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhIssueDevelop,

    [EnumValue("gh_issue_pin")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhIssuePin,

    [EnumValue("gh_issue_unpin")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhIssueUnpin,

    [EnumValue("gh_issue_transfer")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhIssueTransfer,

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

    [EnumValue("gh_repo_edit")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhRepoEdit,

    [EnumValue("gh_repo_delete")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhRepoDelete,

    [EnumValue("gh_repo_archive")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhRepoArchive,

    [EnumValue("gh_repo_unarchive")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhRepoUnarchive,

    [EnumValue("gh_repo_rename")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhRepoRename,

    [EnumValue("gh_repo_sync")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhRepoSync,

    [EnumValue("gh_repo_set_default")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhRepoSetDefault,

    [EnumValue("gh_repo_autolink_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhRepoAutolinkList,

    [EnumValue("gh_repo_autolink_view")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhRepoAutolinkView,

    [EnumValue("gh_repo_autolink_create")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhRepoAutolinkCreate,

    [EnumValue("gh_repo_autolink_delete")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhRepoAutolinkDelete,

    [EnumValue("gh_repo_deploy_key_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhRepoDeployKeyList,

    [EnumValue("gh_repo_deploy_key_add")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhRepoDeployKeyAdd,

    [EnumValue("gh_repo_deploy_key_delete")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhRepoDeployKeyDelete,

    [EnumValue("gh_repo_gitignore_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhRepoGitignoreList,

    [EnumValue("gh_repo_gitignore_view")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhRepoGitignoreView,

    [EnumValue("gh_repo_license_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhRepoLicenseList,

    [EnumValue("gh_repo_license_view")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhRepoLicenseView,

    // === Label 管理 ===
    [EnumValue("gh_label_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhLabelList,

    [EnumValue("gh_label_create")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhLabelCreate,

    [EnumValue("gh_label_delete")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhLabelDelete,

    // === Search ===
    [EnumValue("gh_search_repos")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhSearchRepos,

    [EnumValue("gh_search_issues")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhSearchIssues,

    [EnumValue("gh_search_prs")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhSearchPrs,

    // === Workflow 管理 ===
    [EnumValue("gh_workflow_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhWorkflowList,

    [EnumValue("gh_workflow_view")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhWorkflowView,

    [EnumValue("gh_workflow_run")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhWorkflowRun,

    [EnumValue("gh_workflow_enable")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhWorkflowEnable,

    [EnumValue("gh_workflow_disable")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhWorkflowDisable,

    // === Auth/Config ===
    [EnumValue("gh_auth_status")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhAuthStatus,

    [EnumValue("gh_config_get")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhConfigGet,

    [EnumValue("gh_config_set")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhConfigSet,

    // === Gist ===
    [EnumValue("gh_gist_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhGistList,

    [EnumValue("gh_gist_view")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhGistView,

    [EnumValue("gh_gist_create")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhGistCreate,

    [EnumValue("gh_gist_delete")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhGistDelete,

    // === Org ===
    [EnumValue("gh_org_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhOrgList,

    // === SSH Key ===
    [EnumValue("gh_ssh_key_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhSshKeyList,

    [EnumValue("gh_ssh_key_add")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhSshKeyAdd,

    [EnumValue("gh_ssh_key_delete")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhSshKeyDelete,

    // === GPG Key ===
    [EnumValue("gh_gpg_key_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhGpgKeyList,

    [EnumValue("gh_gpg_key_add")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhGpgKeyAdd,

    [EnumValue("gh_gpg_key_delete")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhGpgKeyDelete,

    // === Secret ===
    [EnumValue("gh_secret_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhSecretList,

    [EnumValue("gh_secret_set")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhSecretSet,

    [EnumValue("gh_secret_delete")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhSecretDelete,

    // === Variable ===
    [EnumValue("gh_variable_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhVariableList,

    [EnumValue("gh_variable_get")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GhVariableGet,

    [EnumValue("gh_variable_set")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhVariableSet,

    [EnumValue("gh_variable_delete")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhVariableDelete,

    // === 分支保护 ===
    [EnumValue("gh_branch_sync_protection")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhBranchSyncProtection,

    // === 通用 API 调用 ===
    [EnumValue("gh_api")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GhApi,
}