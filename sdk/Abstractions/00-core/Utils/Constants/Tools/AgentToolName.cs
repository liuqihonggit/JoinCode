namespace JoinCode.Abstractions.Utils;

/// <summary>
/// Agent 相关工具名称枚举
/// </summary>
public enum AgentToolName
{
    [EnumValue("agent_spawn")] AgentSpawn,
    [EnumValue("agent_list")] AgentList,
    [EnumValue("agent_status")] AgentStatus,
    [EnumValue("SendMessage")] AgentSendMessage,
    [EnumValue("agent_get_messages")] AgentGetMessages,
    [EnumValue("agent_pause")] AgentPause,
    [EnumValue("agent_resume")] AgentResume,
    [EnumValue("agent_stop")] AgentStop,
    [EnumValue("Agent")] Agent,
    [EnumValue("plan_agent")] PlanAgent,
    [EnumValue("explore_agent")] ExploreAgent,
    [EnumValue("verification_agent")] VerificationAgent,
    [EnumValue("general_agent")] GeneralAgent,
    [EnumValue("guide_agent")] GuideAgent,
    [EnumValue("list_agents")] ListAgents,
    [EnumValue("agent_system_stats")] AgentSystemStats,
    [EnumValue("agent_list_stats")] AgentListStats,
    [EnumValue("agent_stats")] AgentStats,
    [EnumValue("agent_history")] AgentHistory,
    [EnumValue("agent_running")] AgentRunning,
    [EnumValue("agent_running_stats")] AgentRunningStats,
    [EnumValue("agent_execution_detail")] AgentExecutionDetail,
    [EnumValue("agent_clear_history")] AgentClearHistory,
}
