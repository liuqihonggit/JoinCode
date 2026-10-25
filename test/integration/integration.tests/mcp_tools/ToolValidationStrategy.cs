namespace Integration.Tests.McpTools;

/// <summary>
/// 工具验证类型 — 决定工具如何被验证
/// </summary>
public enum ToolValidationKind {
    /// <summary>读类：真实执行（无副作用）</summary>
    RealExecute,
    /// <summary>写类：MockServer 模拟 API</summary>
    MockExecute,
    /// <summary>危险类：仅验证参数校验 + 权限拒绝</summary>
    ValidateOnly,
    /// <summary>跳过：不验证</summary>
    Skip,
}

/// <summary>
/// 工具验证策略接口 — 插件式，每工具一个策略
/// </summary>
public interface IToolValidationStrategy {
    /// <summary>获取工具的验证类型</summary>
    ToolValidationKind GetKind(ToolInfo tool);
}

/// <summary>
/// 默认工具验证策略 — 按 Category + 工具名前缀路由
/// </summary>
public sealed class DefaultToolValidationStrategy : IToolValidationStrategy {
    private static readonly FrozenSet<string> DangerousTools = FrozenSet.Create(
        StringComparer.Ordinal,
        [
            "delete", "desktop_click", "desktop_type", "desktop_move",
            "desktop_scroll", "desktop_screenshot", "desktop_key",
        ]
    );

    private static readonly FrozenSet<string> WriteTools = FrozenSet.Create(
        StringComparer.Ordinal,
        [
            "write", "edit", "patch", "mkdir",
        ]
    );

    private static readonly FrozenSet<string> GitWriteTools = FrozenSet.Create(
        StringComparer.Ordinal,
        [
            "git_add", "git_commit", "git_push", "git_pull", "git_reset",
            "git_clean", "git_branch", "git_clone", "git_merge",
            "worktree_create", "worktree_remove", "worktree_cleanup", "worktree_merge",
        ]
    );

    private static readonly FrozenSet<string> GhWriteTools = FrozenSet.Create(
        StringComparer.Ordinal,
        [
            "gh_pr_create", "gh_pr_close", "gh_pr_merge", "gh_pr_comment",
            "gh_pr_review", "gh_pr_update", "gh_pr_approve",
            "gh_issue_create", "gh_issue_close", "gh_issue_comment", "gh_issue_update",
            "gh_run_cancel", "gh_run_rerun", "gh_repo_fork",
        ]
    );

    /// <summary>
    /// 有真实网络副作用的工具 — 冒烟测试跳过，避免连接外部服务器导致超时
    /// （mcp_connect 连真实 MCP 服务器，CI 环境无可用服务器）
    /// </summary>
    private static readonly FrozenSet<string> NetworkSideEffectTools = FrozenSet.Create(
        StringComparer.Ordinal,
        [
            "mcp_connect",
        ]
    );

    /// <summary>获取工具的验证类型</summary>
    public ToolValidationKind GetKind(ToolInfo tool) {
        if (NetworkSideEffectTools.Contains(tool.Name))
            return ToolValidationKind.Skip;

        if (DangerousTools.Contains(tool.Name) ||
            string.Equals(tool.Category, "desktop", StringComparison.OrdinalIgnoreCase))
            return ToolValidationKind.ValidateOnly;

        if (GhWriteTools.Contains(tool.Name))
            return ToolValidationKind.MockExecute;

        if (tool.Name.StartsWith("gh_", StringComparison.Ordinal))
            return ToolValidationKind.RealExecute;

        if (GitWriteTools.Contains(tool.Name))
            return ToolValidationKind.MockExecute;

        if (tool.Name.StartsWith("git_", StringComparison.Ordinal))
            return ToolValidationKind.RealExecute;

        if (WriteTools.Contains(tool.Name))
            return ToolValidationKind.MockExecute;

        return ToolValidationKind.RealExecute;
    }
}
