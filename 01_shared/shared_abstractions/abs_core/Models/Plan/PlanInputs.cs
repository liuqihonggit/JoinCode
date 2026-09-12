namespace JoinCode.Abstractions.Models.Plan;

/// <summary>
/// 计划步骤输入
/// </summary>
public sealed record PlanStepInput
{
    /// <summary>
    /// 步骤描述
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// 工具名称（可选）
    /// </summary>
    public string? ToolName { get; init; }

    /// <summary>
    /// 工具参数（可选）
    /// </summary>
    public Dictionary<string, JsonElement>? Parameters { get; init; }
}

/// <summary>
/// 计划模式操作结果
/// </summary>
public sealed record PlanOperationResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// 计划状态
    /// </summary>
    public PlanState? PlanState { get; init; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 执行结果
    /// </summary>
    public string? ExecutionResult { get; init; }

    /// <summary>
    /// 对齐 TS getPlan(): 从磁盘读取的 plan 文件内容
    /// LLM 可能通过 FileWriteTool 修改了 plan 文件，ExitPlanMode 时读取最新内容
    /// </summary>
    public string? PlanFileContent { get; init; }

    /// <summary>
    /// 对齐 TS awaitingLeaderApproval: 是否正在等待 team-lead 审批
    /// Teammate 退出 PlanMode 时发送审批请求后设为 true
    /// </summary>
    public bool AwaitingLeaderApproval { get; init; }

    /// <summary>
    /// 审批请求 ID — 关联 PlanApprovalRequest/Response
    /// </summary>
    public string? ApprovalRequestId { get; init; }

    public PlanOperationResult(bool success, PlanState? planState = null, string? errorMessage = null, string? executionResult = null, string? planFileContent = null)
    {
        Success = success;
        PlanState = planState;
        ErrorMessage = errorMessage;
        ExecutionResult = executionResult;
        PlanFileContent = planFileContent;
    }
}
