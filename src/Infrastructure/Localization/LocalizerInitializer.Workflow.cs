namespace Infrastructure.Localization;

public static partial class LocalizerInitializer
{
    private static void RegisterWorkflowEntries(Dictionary<string, string> defaultEntries, Dictionary<string, string> zhEntries)
    {
        // === WorkflowToolHandlers ===
        defaultEntries[StringKey.WorkflowPromptModeReceivedMessage] = "[Prompt Mode] Received message: {0}";
        defaultEntries[StringKey.WorkflowPromptModeHistoryCleared] = "[Prompt Mode] Chat history cleared";
        defaultEntries[StringKey.WorkflowChatServiceUnavailable] = "Chat service unavailable";
        defaultEntries[StringKey.WorkflowChatHistoryCleared] = "Chat history cleared";
        defaultEntries[StringKey.WorkflowPromptModeNoHistory] = "[Prompt Mode] No chat history";
        defaultEntries[StringKey.WorkflowNoChatHistory] = "No chat history";
        defaultEntries[StringKey.WorkflowAiServiceUnavailable] = "AI service unavailable with current configuration. Please configure a valid OpenAI API Key, or set the mode to PromptOnly.";
        defaultEntries[StringKey.WorkflowTaskCannotBeEmpty] = "task parameter cannot be empty";
        defaultEntries[StringKey.WorkflowPromptCannotBeEmpty] = "prompt parameter cannot be empty";
        defaultEntries[StringKey.WorkflowRequirementCannotBeEmpty] = "requirement parameter cannot be empty";
        defaultEntries[StringKey.WorkflowCodeCannotBeEmpty] = "code parameter cannot be empty";
        defaultEntries[StringKey.WorkflowMessageCannotBeEmpty] = "message parameter cannot be empty";

        zhEntries[StringKey.WorkflowPromptModeReceivedMessage] = "[提示词模式] 收到消息: {0}";
        zhEntries[StringKey.WorkflowPromptModeHistoryCleared] = "[提示词模式] 聊天历史已清空";
        zhEntries[StringKey.WorkflowChatServiceUnavailable] = "聊天服务不可用";
        zhEntries[StringKey.WorkflowChatHistoryCleared] = "聊天历史已清空";
        zhEntries[StringKey.WorkflowPromptModeNoHistory] = "[提示词模式] 暂无聊天历史";
        zhEntries[StringKey.WorkflowNoChatHistory] = "暂无聊天历史";
        zhEntries[StringKey.WorkflowAiServiceUnavailable] = "当前配置无法使用AI服务。请配置有效的 OpenAI API Key，或将模式设置为 PromptOnly。";
        zhEntries[StringKey.WorkflowTaskCannotBeEmpty] = "task 参数不能为空";
        zhEntries[StringKey.WorkflowPromptCannotBeEmpty] = "prompt 参数不能为空";
        zhEntries[StringKey.WorkflowRequirementCannotBeEmpty] = "requirement 参数不能为空";
        zhEntries[StringKey.WorkflowCodeCannotBeEmpty] = "code 参数不能为空";
        zhEntries[StringKey.WorkflowMessageCannotBeEmpty] = "message 参数不能为空";

        // === WorktreeToolHandlers ===
        defaultEntries[StringKey.WorktreeAgentIdCannotBeEmpty] = "agent_id cannot be empty";
        defaultEntries[StringKey.WorktreeCreateFailed] = "Failed to create Worktree: {0}";
        defaultEntries[StringKey.WorktreeCreateSuccess] = "Worktree created successfully";
        defaultEntries[StringKey.WorktreeLabelAgentId] = "Agent ID: {0}";
        defaultEntries[StringKey.WorktreeLabelPath] = "Worktree path: {0}";
        defaultEntries[StringKey.WorktreeLabelBranch] = "Branch: {0}";
        defaultEntries[StringKey.WorktreeLabelBaseCommit] = "Base commit: {0}";
        defaultEntries[StringKey.WorktreeAgentIsolationNote] = "The agent can now work in an isolated environment without affecting the main branch.";
        defaultEntries[StringKey.WorktreeUncommittedChangesWarning] = "Worktree has uncommitted changes. Use force=true to force remove, or commit/stash changes first.";
        defaultEntries[StringKey.WorktreeRemoveFailed] = "Failed to remove Worktree: {0}";
        defaultEntries[StringKey.WorktreeRemoved] = "Worktree removed";
        defaultEntries[StringKey.WorktreeForceRemoveNote] = "Warning: Force remove mode";
        defaultEntries[StringKey.WorktreeLabelBlockReason] = "Block reason: {0}";
        defaultEntries[StringKey.WorktreeSessionList] = "Worktree session list";
        defaultEntries[StringKey.WorktreeActiveSessionCount] = "{0} active session(s)";
        defaultEntries[StringKey.WorktreeNoActiveSessions] = "No active Worktree sessions";
        defaultEntries[StringKey.WorktreeLabelCreated] = "Created: {0}";
        defaultEntries[StringKey.WorktreeSessionNotFound] = "Worktree session not found for agent '{0}'";
        defaultEntries[StringKey.WorktreeStatusLabel] = "Worktree status [{0}]";
        defaultEntries[StringKey.WorktreeLabelGitRoot] = "Git root: {0}";
        defaultEntries[StringKey.WorktreeUncommittedChanges] = "Uncommitted changes: {0}";
        defaultEntries[StringKey.WorktreeUnpushedCommits] = "Unpushed commits: {0}";
        defaultEntries[StringKey.WorktreeLabelCreateTime] = "Created at: {0}";
        defaultEntries[StringKey.WorktreeConfirmCleanup] = "Please enter 'yes' to confirm cleanup of stale Worktrees";
        defaultEntries[StringKey.WorktreeCleanupComplete] = "Worktree cleanup complete";
        defaultEntries[StringKey.WorktreeLabelStaleHours] = "Stale timeout: {0} hours";
        defaultEntries[StringKey.WorktreeLabelCleanedCount] = "Cleaned count: {0}";
        defaultEntries[StringKey.WorktreeGitRootNotFound] = "Git repository root not found (searching from {0})";
        defaultEntries[StringKey.WorktreeGitRootTitle] = "Git repository root";
        defaultEntries[StringKey.WorktreeLabelStartPath] = "Start path: {0}";
        defaultEntries[StringKey.WorktreeLabelGitRootPath] = "Git root: {0}";
        defaultEntries[StringKey.WorktreeExistingCount] = "Existing Worktrees ({0}):";
        defaultEntries[StringKey.WorktreeListTitle] = "Git Worktree list";
        defaultEntries[StringKey.WorktreeTotalCount] = "{0} Worktree(s) total";
        defaultEntries[StringKey.WorktreeNoWorktrees] = "No Worktrees";
        defaultEntries[StringKey.WorktreeHasChanges] = "Yes";
        defaultEntries[StringKey.WorktreeNoChanges] = "No";
        defaultEntries[StringKey.WorktreeHasUnpushed] = "Yes";
        defaultEntries[StringKey.WorktreeNoUnpushed] = "No";

        zhEntries[StringKey.WorktreeAgentIdCannotBeEmpty] = "agent_id 不能为空";
        zhEntries[StringKey.WorktreeCreateFailed] = "创建Worktree失败: {0}";
        zhEntries[StringKey.WorktreeCreateSuccess] = "Worktree创建成功";
        zhEntries[StringKey.WorktreeLabelAgentId] = "代理ID: {0}";
        zhEntries[StringKey.WorktreeLabelPath] = "Worktree路径: {0}";
        zhEntries[StringKey.WorktreeLabelBranch] = "分支: {0}";
        zhEntries[StringKey.WorktreeLabelBaseCommit] = "基于提交: {0}";
        zhEntries[StringKey.WorktreeAgentIsolationNote] = "代理现在可以在隔离环境中工作，不会影响主分支。";
        zhEntries[StringKey.WorktreeUncommittedChangesWarning] = "Worktree有未提交更改。请使用 force=true 强制移除，或先提交/丢弃更改。";
        zhEntries[StringKey.WorktreeRemoveFailed] = "移除Worktree失败: {0}";
        zhEntries[StringKey.WorktreeRemoved] = "Worktree已移除";
        zhEntries[StringKey.WorktreeForceRemoveNote] = "注意: 强制移除模式";
        zhEntries[StringKey.WorktreeLabelBlockReason] = "阻止原因: {0}";
        zhEntries[StringKey.WorktreeSessionList] = "Worktree会话列表";
        zhEntries[StringKey.WorktreeActiveSessionCount] = "共 {0} 个活动会话";
        zhEntries[StringKey.WorktreeNoActiveSessions] = "暂无活动Worktree会话";
        zhEntries[StringKey.WorktreeLabelCreated] = "创建: {0}";
        zhEntries[StringKey.WorktreeSessionNotFound] = "未找到代理 '{0}' 的Worktree会话";
        zhEntries[StringKey.WorktreeStatusLabel] = "Worktree状态 [{0}]";
        zhEntries[StringKey.WorktreeLabelGitRoot] = "Git根目录: {0}";
        zhEntries[StringKey.WorktreeUncommittedChanges] = "未提交更改: {0}";
        zhEntries[StringKey.WorktreeUnpushedCommits] = "未推送提交: {0}";
        zhEntries[StringKey.WorktreeLabelCreateTime] = "创建时间: {0}";
        zhEntries[StringKey.WorktreeConfirmCleanup] = "请输入 'yes' 确认清理过期Worktree";
        zhEntries[StringKey.WorktreeCleanupComplete] = "Worktree清理完成";
        zhEntries[StringKey.WorktreeLabelStaleHours] = "清理过期时间: {0} 小时";
        zhEntries[StringKey.WorktreeLabelCleanedCount] = "清理数量: {0}";
        zhEntries[StringKey.WorktreeGitRootNotFound] = "未找到Git仓库根目录（从 {0} 开始查找）";
        zhEntries[StringKey.WorktreeGitRootTitle] = "Git仓库根目录";
        zhEntries[StringKey.WorktreeLabelStartPath] = "起始路径: {0}";
        zhEntries[StringKey.WorktreeLabelGitRootPath] = "Git根目录: {0}";
        zhEntries[StringKey.WorktreeExistingCount] = "现有Worktree ({0} 个):";
        zhEntries[StringKey.WorktreeListTitle] = "Git Worktree列表";
        zhEntries[StringKey.WorktreeTotalCount] = "共 {0} 个Worktree";
        zhEntries[StringKey.WorktreeNoWorktrees] = "暂无Worktree";
        zhEntries[StringKey.WorktreeHasChanges] = "有";
        zhEntries[StringKey.WorktreeNoChanges] = "无";
        zhEntries[StringKey.WorktreeHasUnpushed] = "有";
        zhEntries[StringKey.WorktreeNoUnpushed] = "无";
    }
}
