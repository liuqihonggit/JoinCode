namespace JoinCode.Abstractions.Models.Plan;

/// <summary>
/// 计划状态
/// </summary>
public sealed record PlanState
{
    /// <summary>
    /// 计划ID
    /// </summary>
    public required string PlanId { get; init; }

    /// <summary>
    /// 计划描述
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 计划状态
    /// </summary>
    public PlanStatus Status { get; set; } = PlanStatus.Draft;

    /// <summary>
    /// 计划步骤列表
    /// </summary>
    public List<PlanStep> Steps { get; init; } = new();

    /// <summary>
    /// 当前步骤索引
    /// </summary>
    public int CurrentStepIndex { get; set; } = 0;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 完成时间
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// 是否处于计划模式
    /// </summary>
    public bool IsInPlanMode { get; set; } = true;

    /// <summary>
    /// 对齐 TS planFilePath: plan 文件的磁盘路径
    /// 进入 plan 模式时生成，LLM 可通过 FileWriteTool 直接写入此文件
    /// </summary>
    public string? PlanFilePath { get; set; }

    /// <summary>
    /// 对齐 TS planWasEdited: 标记计划是否被用户编辑过
    /// </summary>
    public bool WasEditedByUser { get; set; } = false;

    /// <summary>
    /// 获取已批准的步骤数
    /// </summary>
    public int ApprovedStepsCount => Steps.Count(s => s.IsApproved);

    /// <summary>
    /// 获取已完成的步骤数
    /// </summary>
    public int CompletedStepsCount => Steps.Count(s => s.IsCompleted);

    /// <summary>
    /// 获取总步骤数
    /// </summary>
    public int TotalSteps => Steps.Count;

    /// <summary>
    /// 获取当前步骤
    /// </summary>
    public PlanStep? GetCurrentStep()
    {
        if (CurrentStepIndex >= 0 && CurrentStepIndex < Steps.Count)
        {
            return Steps[CurrentStepIndex];
        }
        return null;
    }

    /// <summary>
    /// 获取进度百分比
    /// </summary>
    public double GetProgressPercentage()
    {
        if (Steps.Count == 0) return 0;
        return (double)CompletedStepsCount / Steps.Count * 100;
    }
}

/// <summary>
/// 计划步骤
/// </summary>
public sealed record PlanStep
{
    /// <summary>
    /// 步骤索引
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// 步骤描述
    /// </summary>
    public required string Description { get; set; }

    /// <summary>
    /// 工具名称（可选）
    /// </summary>
    public string? ToolName { get; set; }

    /// <summary>
    /// 工具参数（可选）
    /// </summary>
    public Dictionary<string, JsonElement>? Parameters { get; set; }

    /// <summary>
    /// 步骤状态
    /// </summary>
    public PlanStepStatus Status { get; set; } = PlanStepStatus.Pending;

    /// <summary>
    /// 是否已批准
    /// </summary>
    public bool IsApproved => Status == PlanStepStatus.Approved || Status == PlanStepStatus.Completed;

    /// <summary>
    /// 是否已完成
    /// </summary>
    public bool IsCompleted => Status == PlanStepStatus.Completed;

    /// <summary>
    /// 执行结果
    /// </summary>
    public string? ExecutionResult { get; set; }

    /// <summary>
    /// 拒绝原因
    /// </summary>
    public string? RejectionReason { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 完成时间
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// 执行时间（毫秒）
    /// </summary>
    public long? ExecutionTimeMs { get; set; }
}
