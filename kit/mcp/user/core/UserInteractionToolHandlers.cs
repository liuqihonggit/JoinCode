


namespace McpToolDispatch;

/// <summary>
/// 用户交互工具处理器 — 向用户提出多选问题以收集信息、澄清歧义或做出决策
/// </summary>
[McpToolDispatch(ToolCategory.Interaction)]
public class UserInteractionToolHandlers {
    private readonly IInteractiveService _interactiveService;
    private readonly ILogger<UserInteractionToolHandlers>? _logger;

    /// <summary>
    /// 初始化用户交互工具处理器
    /// </summary>
    /// <param name="interactiveService">交互式服务实例</param>
    /// <param name="logger">日志记录器（可选）</param>
    public UserInteractionToolHandlers(IInteractiveService interactiveService, ILogger<UserInteractionToolHandlers>? logger = null) {
        _interactiveService = interactiveService ?? throw new ArgumentNullException(nameof(interactiveService));
        _logger = logger;
    }

    /// <summary>
    /// 向用户提出多选问题以收集信息、澄清歧义或做出决策
    /// </summary>
    /// <param name="questions">问题 JSON 数组（1-4 个问题），每个问题包含 question/header/options/multiSelect 字段</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含用户回答的工具执行结果</returns>
    [McpTool(UserInteractionToolNameEnumConstants.AskUserQuestion, "Ask the user multiple choice questions to gather information, clarify ambiguity, or make decisions", "interaction")]
    public async Task<ToolResult> AskUserQuestionAsync(
        [McpToolParameter("Questions to ask the user (JSON array, 1-4 questions). Each: {question, header, options:[{label,description,preview?}], multiSelect?}")] string questions,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(questions))
            return ToolResultBuilder.Error().WithText("questions cannot be empty").Build();

        List<QuestionItem> questionItems;
        try {
            questionItems = LlmJsonHelper.DeserializeValue(questions, QuestionItemListContext.Default.ListQuestionItem, out var repairHint, _logger)
                ?? new List<QuestionItem>();
            if (!string.IsNullOrEmpty(repairHint))
                _logger?.LogInformation("[AskUserQuestion] questions JSON 已修复: {RepairHint}", repairHint);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return ToolExceptionDiagnosticHelper.BuildErrorResult("ask_user_question", ex, _logger, "questions", questions ?? "(null)");
        }

        if (questionItems.Count == 0)
            return ToolResultBuilder.Error().WithText("At least 1 question required").Build();

        if (questionItems.Count > 4)
            return ToolResultBuilder.Error().WithText("Maximum 4 questions allowed").Build();

        var validationError = ValidateQuestions(questionItems);
        if (validationError is not null)
            return ToolResultBuilder.Error().WithText(validationError).Build();

        var result = await _interactiveService.AskUserQuestionsAsync(questionItems, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
            return ToolResultBuilder.Error().WithText(result.ErrorMessage ?? "Failed to get user answers").Build();

        if (result.Cancelled)
            return ToolResultBuilder.Error().WithText("User declined to answer questions").Build();

        var answersText = result.Answers is not null
            ? string.Join(", ", result.Answers.Select(kv => $"\"{kv.Key}\"=\"{kv.Value}\""))
            : result.Answer ?? string.Empty;

        return ToolResultBuilder.Success()
            .WithText($"User has answered your questions: {answersText}. You can now continue with the user's answers in mind.")
            .Build();
    }

    /// <summary>
    /// 向用户请求确认指定动作 — 是/否二选一
    /// </summary>
    /// <param name="action">要确认的动作描述</param>
    /// <param name="message">详细确认消息（可选，默认用 action）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含用户确认结果的工具执行结果</returns>
    [McpTool(UserInteractionToolNameEnumConstants.ConfirmAction, "Ask the user to confirm an action with yes/no", "interaction")]
    public async Task<ToolResult> ConfirmActionAsync(
        [McpToolParameter("The action to confirm")] string action,
        [McpToolParameter("Detailed confirmation message (optional)", Required = false)] string? message = null,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(action))
            return ToolResultBuilder.Error().WithText("action cannot be empty").Build();

        var questionText = string.IsNullOrWhiteSpace(message) ? action : message;
        var result = await _interactiveService.AskUserQuestionAsync(questionText, ["是", "否"], false, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
            return ToolResultBuilder.Error().WithText(result.ErrorMessage ?? "Failed to get user confirmation").Build();

        if (result.Cancelled)
            return ToolResultBuilder.Error().WithText("User cancelled the confirmation").Build();

        var confirmed = result.Answer == "是";
        return ToolResultBuilder.Success()
            .WithText(confirmed
                ? $"User confirmed the action: {action}"
                : $"User declined the action: {action}")
            .Build();
    }

    private static string? ValidateQuestions(List<QuestionItem> questions) {
        var questionTexts = new HashSet<string>();
        foreach (var q in questions) {
            if (string.IsNullOrWhiteSpace(q.Question))
                return "Question text cannot be empty";

            if (string.IsNullOrWhiteSpace(q.Header))
                return $"Header is required for question: {q.Question}";

            if (q.Header.Length > 12)
                return $"Header must be max 12 chars for question: {q.Question}";

            if (!questionTexts.Add(q.Question))
                return $"Duplicate question: {q.Question}";

            if (q.Options.Count < 2)
                return $"Question '{q.Question}' must have at least 2 options";

            if (q.Options.Count > 4)
                return $"Question '{q.Question}' must have at most 4 options";

            var labels = new HashSet<string>();
            foreach (var opt in q.Options) {
                if (string.IsNullOrWhiteSpace(opt.Label))
                    return $"Option label cannot be empty in question: {q.Question}";

                if (!labels.Add(opt.Label))
                    return $"Duplicate option label '{opt.Label}' in question: {q.Question}";
            }
        }

        return null;
    }
}