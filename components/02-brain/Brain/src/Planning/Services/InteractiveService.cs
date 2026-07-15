
namespace Core.Planning;

[Register]
public sealed partial class InteractiveService : IInteractiveService
{
    private bool _isInPlanMode;
    private string? _currentPlanId;
    private string? _currentGoal;
    private DateTime? _enteredAt;
    private List<PlanStep>? _steps;
    [Inject] private readonly ILogger<InteractiveService>? _logger;
    [Inject] private readonly ITelemetryService? _telemetryService;
    [Inject] private readonly IClockService _clock;

    public Task<AskUserQuestionResult> AskUserQuestionAsync(string question, List<string>? options = null, bool multiSelect = false, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("[Ask] {Question}", question);

        if (options?.Count > 0)
        {
            for (int i = 0; i < options.Count; i++)
            {
                _logger?.LogDebug("Option {Index}: {Option}", i + 1, options[i]);
            }
        }

        return Task.FromResult(AskUserQuestionResult.SuccessResult("user answer"));
    }

    public Task<AskUserQuestionResult> AskUserQuestionsAsync(List<QuestionItem> questions, CancellationToken cancellationToken = default)
    {
        if (questions.Count == 0)
            return Task.FromResult(AskUserQuestionResult.FailureResult("No questions provided"));

        if (questions.Count > 4)
            return Task.FromResult(AskUserQuestionResult.FailureResult("Maximum 4 questions allowed"));

        foreach (var q in questions)
        {
            if (q.Options.Count < 2 || q.Options.Count > 4)
                return Task.FromResult(AskUserQuestionResult.FailureResult($"Question '{q.Question}' must have 2-4 options"));

            var labels = q.Options.Select(o => o.Label).ToList();
            if (labels.Distinct().Count() != labels.Count)
                return Task.FromResult(AskUserQuestionResult.FailureResult($"Question '{q.Question}' has duplicate option labels"));
        }

        var questionTexts = questions.Select(q => q.Question).ToList();
        if (questionTexts.Distinct().Count() != questionTexts.Count)
            return Task.FromResult(AskUserQuestionResult.FailureResult("Question texts must be unique"));

        _logger?.LogInformation("[AskQuestions] {Count} questions", questions.Count);

        var answers = new Dictionary<string, string>();
        foreach (var q in questions)
        {
            answers[q.Question] = q.Options[0].Label;
        }

        return Task.FromResult(AskUserQuestionResult.QuestionsResult(answers));
    }

    public Task<PlanModeResult> EnterPlanModeAsync(string? initialPlan = null, string? goal = null, CancellationToken cancellationToken = default)
    {
        _isInPlanMode = true;
        _currentPlanId = Guid.NewGuid().ToString("N")[..8];
        _currentGoal = goal;
        _enteredAt = _clock.GetUtcNow();
        _steps = new List<PlanStep>();

        if (!string.IsNullOrEmpty(initialPlan))
        {
            var lines = initialPlan.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var index = 0;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    _steps.Add(new PlanStep
                    {
                        Index = index++,
                        Description = trimmed,
                        Status = PlanStepStatus.Pending
                    });
                }
            }
        }

        _logger?.LogInformation("[计划模式] 已进入计划模式，目标: {Goal}", goal ?? "未指定");

        RecordInteractiveMetrics("enter_plan", true);
        return Task.FromResult(PlanModeResult.SuccessResult(_currentPlanId ?? throw new InvalidOperationException("Plan ID is not available."), "已进入计划模式"));
    }

    public Task<PlanModeResult> ExitPlanModeAsync(bool confirm = true, string? summary = null, CancellationToken cancellationToken = default)
    {
        if (!_isInPlanMode)
        {
            return Task.FromResult(PlanModeResult.FailureResult(ContractsErrorMessages.NotInPlanMode));
        }

        if (!confirm)
        {
            return Task.FromResult(PlanModeResult.FailureResult(ContractsErrorMessages.UserCancelledExit));
        }

        _logger?.LogInformation("[计划模式] 已退出计划模式，总结: {Summary}", summary ?? "无");

        _isInPlanMode = false;
        _currentPlanId = null;
        _currentGoal = null;
        _enteredAt = null;
        _steps = null;

        RecordInteractiveMetrics("exit_plan", true);
        return Task.FromResult(PlanModeResult.SuccessResult(string.Empty, "已退出计划模式"));
    }

    public Task<PlanModeStatus> GetPlanModeStatusAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PlanModeStatus
        {
            IsInPlanMode = _isInPlanMode,
            CurrentPlanId = _currentPlanId,
            CurrentGoal = _currentGoal,
            EnteredAt = _enteredAt,
            Steps = _steps?.Select(s => new PlanStepInfo
            {
                Index = s.Index,
                Description = s.Description,
                Status = s.Status.ToString()
            }).ToList() ?? new List<PlanStepInfo>()
        });
    }

    private void RecordInteractiveMetrics(string operation, bool isSuccess)
        => _telemetryService?.RecordCount("interactive.plan.count", new() { ["operation"] = operation, ["success"] = isSuccess.ToString() }, "count", "Interactive plan operation count");
}
