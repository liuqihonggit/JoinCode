namespace JoinCode.Abstractions.Models.Plan;

/// <summary>
/// 计划步骤输入
/// </summary>
public sealed record PlanStepInput {
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
public sealed record PlanOperationResult {
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

    /// <summary>构造计划操作结果。</summary>
    public PlanOperationResult(bool success, PlanState? planState = null, string? errorMessage = null, string? executionResult = null, string? planFileContent = null) {
        Success = success;
        PlanState = planState;
        ErrorMessage = errorMessage;
        ExecutionResult = executionResult;
        PlanFileContent = planFileContent;
    }
}

/// <summary>
/// 任务自动重排结果 — 连续失败任务自动后置机制的返回值
/// </summary>
public sealed record PlanAutoReorderResult {
    /// <summary>是否执行了重排</summary>
    public bool Reordered { get; init; }

    /// <summary>被后置的步骤描述列表</summary>
    public IReadOnlyList<string> PostponedSteps { get; init; } = [];

    /// <summary>提示消息 — 供 UI 显示"任务 X 因连续失败已后置，先推进任务 Y"</summary>
    public string? Message { get; init; }

    /// <summary>重排后的计划状态</summary>
    public PlanState? PlanState { get; init; }

    /// <summary>构造自动重排结果</summary>
    public PlanAutoReorderResult(bool reordered, IReadOnlyList<string>? postponedSteps = null, string? message = null, PlanState? planState = null) {
        Reordered = reordered;
        PostponedSteps = postponedSteps ?? [];
        Message = message;
        PlanState = planState;
    }
}