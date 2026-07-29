
namespace JoinCode.Abstractions.Interfaces;

public interface IInteractiveService
{
    /// <summary>
    /// 向用户提问（单选）
    /// </summary>
    Task<AskUserQuestionResult> AskUserQuestionAsync(
        string question,
        List<string>? options = null,
        bool multiSelect = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 向用户提问（多问题批量）
    /// </summary>
    Task<AskUserQuestionResult> AskUserQuestionsAsync(
        List<QuestionItem> questions,
        CancellationToken cancellationToken = default);
}
