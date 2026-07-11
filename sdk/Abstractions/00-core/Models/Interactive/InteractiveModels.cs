namespace JoinCode.Abstractions.Models.Interactive;

public sealed record QuestionOption
{
    public required string Label { get; init; }
    public required string Description { get; init; }
    public string? Preview { get; init; }
}

public sealed record QuestionItem
{
    public required string Question { get; init; }
    public required string Header { get; init; }
    public required List<QuestionOption> Options { get; init; }
    public bool MultiSelect { get; init; }
}

public sealed record AskUserQuestionResult
{
    public required bool Success { get; init; }
    public Dictionary<string, string>? Answers { get; init; }
    public string? Answer { get; init; }
    public List<string>? SelectedOptions { get; init; }
    public string? ErrorMessage { get; init; }
    public bool Cancelled { get; init; }

    public static AskUserQuestionResult SuccessResult(string answer) => new()
    {
        Success = true,
        Answer = answer
    };

    public static AskUserQuestionResult MultiSelectResult(List<string> selectedOptions) => new()
    {
        Success = true,
        SelectedOptions = selectedOptions
    };

    public static AskUserQuestionResult QuestionsResult(Dictionary<string, string> answers) => new()
    {
        Success = true,
        Answers = answers
    };

    public static AskUserQuestionResult FailureResult(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage
    };

    public static AskUserQuestionResult CancelledResult() => new()
    {
        Success = false,
        Cancelled = true,
        ErrorMessage = "User cancelled"
    };
}

/// <summary>
/// 计划模式结果
/// </summary>
public sealed record PlanModeResult
{
    public required bool Success { get; init; }
    public string? PlanId { get; init; }
    public string? Message { get; init; }
    public string? ErrorMessage { get; init; }

    public static PlanModeResult SuccessResult(string planId, string message) => new()
    {
        Success = true,
        PlanId = planId,
        Message = message
    };

    public static PlanModeResult FailureResult(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage
    };
}

/// <summary>
/// 计划模式状态
/// </summary>
public sealed record PlanModeStatus
{
    public required bool IsInPlanMode { get; init; }
    public string? CurrentPlanId { get; init; }
    public string? CurrentGoal { get; init; }
    public DateTime? EnteredAt { get; init; }
    public List<PlanStepInfo>? Steps { get; init; }
}

/// <summary>
/// 计划步骤信息（简化版）
/// </summary>
public sealed record PlanStepInfo
{
    public int Index { get; init; }
    public required string Description { get; init; }
    public string? Status { get; init; }
}
