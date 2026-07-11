namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 工作流工具名称枚举
/// </summary>
public enum WorkflowToolName
{
    [EnumValue("workflow")] WorkflowCreate,
    [EnumValue("workflow_execute")] WorkflowExecute,
    [EnumValue("workflow_status")] WorkflowStatus,
    [EnumValue("mcp_ai_workflow_workflow_execute")] McpAiWorkflowWorkflowExecute,
    [EnumValue("mcp_ai_workflow_plan_create_and_execute")] McpAiWorkflowPlanCreateAndExecute,
    [EnumValue("mcp_ai_workflow_workflow_generate_code")] McpAiWorkflowWorkflowGenerateCode,
    [EnumValue("mcp_ai_workflow_workflow_analyze_code")] McpAiWorkflowWorkflowAnalyzeCode,
    [EnumValue("mcp_ai_workflow_workflow_chat")] McpAiWorkflowWorkflowChat,
    [EnumValue("mcp_ai_workflow_workflow_clear_history")] McpAiWorkflowWorkflowClearHistory,
    [EnumValue("mcp_ai_workflow_workflow_get_history")] McpAiWorkflowWorkflowGetHistory,
}
