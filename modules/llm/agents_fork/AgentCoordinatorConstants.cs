namespace Core.Agents.Coordinator;

/// <summary>
/// Agent 协调器相关常量
/// </summary>
public static class AgentCoordinatorConstants {
    /// <summary>
    /// 日志消息模板
    /// </summary>
    public static class LogMessages {
        /// <summary>AgentCoordinator 日志前缀</summary>
        public const string AgentCoordinatorPrefix = "[AgentCoordinator]";
        /// <summary>AgentLifecycleManager 日志前缀</summary>
        public const string AgentLifecycleManagerPrefix = "[AgentLifecycleManager]";
        /// <summary>AgentWorktreeManager 日志前缀</summary>
        public const string AgentWorktreeManagerPrefix = "[AgentWorktreeManager]";
        /// <summary>AgentExecutionEngine 日志前缀</summary>
        public const string AgentExecutionEnginePrefix = "[AgentExecutionEngine]";
        /// <summary>SubAgent 日志前缀</summary>
        public const string SubAgentPrefix = "[SubAgent]";

        /// <summary>生成子 Agent 日志模板</summary>
        public const string SpawnSubAgent = "{Prefix} 生成子Agent {AgentId}: {Task}";
        /// <summary>开始执行 Agent 日志模板</summary>
        public const string StartExecuteAgent = "{Prefix} 开始执行Agent {AgentId}";
        /// <summary>Agent 执行完成日志模板</summary>
        public const string AgentExecuteComplete = "{Prefix} Agent {AgentId} 执行完成，状态: {State}";
        /// <summary>Agent 执行失败日志模板</summary>
        public const string AgentExecuteFailed = "{Prefix} Agent {AgentId} 执行失败";
        /// <summary>已取消所有 Agent 日志模板</summary>
        public const string CancelAllAgents = "{Prefix} 已取消所有Agent";
        /// <summary>Agent 状态不允许重试日志模板</summary>
        public const string AgentRetryNotAllowed = "{Prefix} Agent {AgentId} 状态 {State} 不允许重试";
        /// <summary>创建 Worktree 日志模板</summary>
        public const string CreateWorktree = "{Prefix} 为 Agent {AgentId} 创建 worktree: {WorktreePath}";
        /// <summary>创建 Worktree 失败日志模板</summary>
        public const string CreateWorktreeFailed = "{Prefix} 为 Agent {AgentId} 创建 worktree 失败: {Error}";
        /// <summary>已清理 Worktree 日志模板</summary>
        public const string CleanupWorktree = "{Prefix} 已清理 Agent {AgentId} 的 worktree";
        /// <summary>清理 Worktree 被阻止日志模板</summary>
        public const string CleanupWorktreeBlocked = "{Prefix} 清理 Agent {AgentId} 的 worktree 被阻止: {Reason}";
        /// <summary>创建 Worktree 出错日志模板</summary>
        public const string CreateWorktreeError = "{Prefix} 创建 worktree 时出错: {AgentId}";
        /// <summary>清理 Worktree 出错日志模板</summary>
        public const string CleanupWorktreeError = "{Prefix} 清理 worktree 时出错: {AgentId}";
        /// <summary>串行执行失败日志模板</summary>
        public const string SequentialExecuteFailed = "{Prefix} Agent {AgentId} 执行失败，停止序列执行";
        /// <summary>子 Agent 开始执行日志模板</summary>
        public const string SubAgentStartExecute = "[{Prefix} {AgentId}] 开始执行任务 (第{Count}次)";
    }

    /// <summary>
    /// Agent ID 生成格式
    /// </summary>
    public static class AgentIdFormats {
        /// <summary>基于 GUID 的 Agent ID 格式</summary>
        public const string GuidFormat = "agent-{0:N}";
        /// <summary>基于计数器的 Agent ID 格式</summary>
        public const string CounterFormat = "agent-{0:D4}-{1}";
    }

    /// <summary>
    /// 系统提示消息
    /// </summary>
    public static class SystemPrompts {
        /// <summary>子 Agent 系统消息模板</summary>
        public const string SubAgentSystemMessage = "你是一个专业的助手，正在执行以下任务: {0}";
    }
}