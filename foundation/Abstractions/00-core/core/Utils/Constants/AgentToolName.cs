namespace JoinCode.Abstractions.Utils;

/// <summary>
/// Agent 相关工具名称枚举
/// </summary>
public enum AgentToolName
{
    [EnumValue("agent_spawn")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    AgentSpawn,

    [EnumValue("agent_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    AgentList,

    [EnumValue("agent_status")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    AgentStatus,

    [EnumValue("SendMessage")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    AgentSendMessage,

    [EnumValue("agent_get_messages")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    AgentGetMessages,

    [EnumValue("agent_pause")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    AgentPause,

    [EnumValue("agent_resume")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    AgentResume,

    [EnumValue("agent_stop")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    AgentStop,

    [EnumValue("Agent")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    Agent,

    [EnumValue("plan_agent")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    PlanAgent,

    [EnumValue("explore_agent")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    ExploreAgent,

    [EnumValue("verification_agent")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    VerificationAgent,

    [EnumValue("general_agent")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GeneralAgent,

    [EnumValue("guide_agent")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GuideAgent,

    [EnumValue("list_agents")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    ListAgents,

    [EnumValue("agent_system_stats")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    AgentSystemStats,

    [EnumValue("agent_list_stats")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    AgentListStats,

    [EnumValue("agent_stats")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    AgentStats,

    [EnumValue("agent_history")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    AgentHistory,

    [EnumValue("agent_running")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    AgentRunning,

    [EnumValue("agent_running_stats")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    AgentRunningStats,

    [EnumValue("agent_execution_detail")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    AgentExecutionDetail,

    [EnumValue("agent_clear_history")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    AgentClearHistory,
}
