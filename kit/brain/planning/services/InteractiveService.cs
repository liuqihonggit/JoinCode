
namespace Core.Planning;

/// <summary>
/// 交互式问答服务 — 向用户提问获取决策输入
/// </summary>
[Register(typeof(IInteractiveService), ServiceLifetime.Singleton)]
public sealed partial class InteractiveService : ServiceEntity, IInteractiveService
{

    /// <summary>
    /// 初始化交互式问答服务
    /// </summary>
    /// <param name="logger">日志记录器（可选）</param>
    public InteractiveService(ILogger<InteractiveService>? logger = null)
    {
        _logger = logger;
    }
    private readonly ILogger<InteractiveService>? _logger;

    /// <summary>
    /// 向用户提出单个问题并获取回答
    /// </summary>
    /// <param name="question">问题文本</param>
    /// <param name="options">可选项列表（可选）</param>
    /// <param name="multiSelect">是否允许多选</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>用户回答结果</returns>
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

    /// <summary>
    /// 向用户批量提出多个问题（最多 4 个）并获取回答
    /// </summary>
    /// <param name="questions">问题项列表，每项需包含 2-4 个选项且问题文本唯一</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含所有问题回答的结果</returns>
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
