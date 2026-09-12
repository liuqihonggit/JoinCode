
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 结构化用户提问服务 — 用于 MCP 工具协议中的多选/批量提问
/// 消费方: UserInteractionToolHandlers (MCP ask_user_question 工具)
/// 关系: IUserInteractionService 提供简单交互（Ask/Send/Confirm），本接口提供结构化多选提问
/// </summary>
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
