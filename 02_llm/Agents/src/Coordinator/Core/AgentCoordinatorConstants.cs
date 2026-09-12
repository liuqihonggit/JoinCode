namespace Core.Agents.Coordinator;

/// <summary>
/// Agent 协调器相关常量
/// </summary>
public static class AgentCoordinatorConstants
{
    /// <summary>
    /// 日志消息模板
    /// </summary>
    public static class LogMessages
    {
        public const string AgentCoordinatorPrefix = "[AgentCoordinator]";
        public const string AgentLifecycleManagerPrefix = "[AgentLifecycleManager]";
        public const string AgentWorktreeManagerPrefix = "[AgentWorktreeManager]";
        public const string AgentExecutionEnginePrefix = "[AgentExecutionEngine]";
        public const string SubAgentPrefix = "[SubAgent]";

        public const string SpawnSubAgent = "{Prefix} 生成子Agent {AgentId}: {Task}";
        public const string StartExecuteAgent = "{Prefix} 开始执行Agent {AgentId}";
        public const string AgentExecuteComplete = "{Prefix} Agent {AgentId} 执行完成，状态: {State}";
        public const string AgentExecuteFailed = "{Prefix} Agent {AgentId} 执行失败";
        public const string CancelAllAgents = "{Prefix} 已取消所有Agent";
        public const string AgentRetryNotAllowed = "{Prefix} Agent {AgentId} 状态 {State} 不允许重试";
        public const string CreateWorktree = "{Prefix} 为 Agent {AgentId} 创建 worktree: {WorktreePath}";
        public const string CreateWorktreeFailed = "{Prefix} 为 Agent {AgentId} 创建 worktree 失败: {Error}";
        public const string CleanupWorktree = "{Prefix} 已清理 Agent {AgentId} 的 worktree";
        public const string CleanupWorktreeBlocked = "{Prefix} 清理 Agent {AgentId} 的 worktree 被阻止: {Reason}";
        public const string CreateWorktreeError = "{Prefix} 创建 worktree 时出错: {AgentId}";
        public const string CleanupWorktreeError = "{Prefix} 清理 worktree 时出错: {AgentId}";
        public const string SequentialExecuteFailed = "{Prefix} Agent {AgentId} 执行失败，停止序列执行";
        public const string SubAgentStartExecute = "[{Prefix} {AgentId}] 开始执行任务 (第{Count}次)";
    }

    /// <summary>
    /// Agent ID 生成格式
    /// </summary>
    public static class AgentIdFormats
    {
        public const string GuidFormat = "agent-{0:N}";
        public const string CounterFormat = "agent-{0:D4}-{1}";
    }

    /// <summary>
    /// 系统提示消息
    /// </summary>
    public static class SystemPrompts
    {
        public const string SubAgentSystemMessage = "你是一个专业的助手，正在执行以下任务: {0}";
    }
}
