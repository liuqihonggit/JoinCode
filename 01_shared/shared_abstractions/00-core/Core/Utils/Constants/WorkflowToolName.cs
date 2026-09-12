namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 工作流工具名称枚举
/// </summary>
public enum WorkflowToolName
{
    [EnumValue("workflow")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    WorkflowCreate,

    [EnumValue("workflow_execute")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    WorkflowExecute,

    [EnumValue("workflow_status")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    WorkflowStatus,

    [EnumValue("mcp_ai_workflow_workflow_execute")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    McpAiWorkflowWorkflowExecute,

    [EnumValue("mcp_ai_workflow_plan_create_and_execute")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    McpAiWorkflowPlanCreateAndExecute,

    [EnumValue("mcp_ai_workflow_workflow_generate_code")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    McpAiWorkflowWorkflowGenerateCode,

    [EnumValue("mcp_ai_workflow_workflow_analyze_code")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    McpAiWorkflowWorkflowAnalyzeCode,

    [EnumValue("mcp_ai_workflow_workflow_chat")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    McpAiWorkflowWorkflowChat,

    [EnumValue("mcp_ai_workflow_workflow_clear_history")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    McpAiWorkflowWorkflowClearHistory,

    [EnumValue("mcp_ai_workflow_workflow_get_history")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    McpAiWorkflowWorkflowGetHistory,
}
