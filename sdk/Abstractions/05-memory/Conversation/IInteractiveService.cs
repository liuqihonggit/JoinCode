
namespace JoinCode.Abstractions.Interfaces;

public interface IInteractiveService
{
    Task<AskUserQuestionResult> AskUserQuestionAsync(
        string question,
        List<string>? options = null,
        bool multiSelect = false,
        CancellationToken cancellationToken = default);

    Task<AskUserQuestionResult> AskUserQuestionsAsync(
        List<QuestionItem> questions,
        CancellationToken cancellationToken = default);

    Task<PlanModeResult> EnterPlanModeAsync(
        string? initialPlan = null,
        string? goal = null,
        CancellationToken cancellationToken = default);

    Task<PlanModeResult> ExitPlanModeAsync(
        bool confirm = true,
        string? summary = null,
        CancellationToken cancellationToken = default);

    Task<PlanModeStatus> GetPlanModeStatusAsync(CancellationToken cancellationToken = default);
}
