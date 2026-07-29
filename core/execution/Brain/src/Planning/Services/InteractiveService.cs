
namespace Core.Planning;

[Register]
public sealed partial class InteractiveService : IInteractiveService
{
    [Inject] private readonly ILogger<InteractiveService>? _logger;

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
}
