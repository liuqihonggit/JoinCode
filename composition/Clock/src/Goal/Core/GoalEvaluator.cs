
namespace Core.Goal;

[Register]
public sealed partial class GoalEvaluator : IGoalEvaluator
{
    private readonly IChatClient _kernel;
    [Inject] private readonly ILogger<GoalEvaluator>? _logger;

    public GoalEvaluator(IChatClient kernel, ILogger<GoalEvaluator>? logger = null)
    {
        _kernel = kernel;
        _logger = logger;
    }

    public async Task<GoalEvaluationResult> EvaluateAsync(
        string objective,
        IReadOnlyList<string> constraints,
        string recentConversation,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objective);

        var prompt = BuildEvaluatorPrompt(objective, constraints, recentConversation);

        var chatHistory = new MessageList();
        chatHistory.AddSystemMessage(prompt);
        chatHistory.AddUserMessage("Evaluate whether the objective has been achieved based on the conversation above.");

        var executionSettings = new ChatOptions
        {
            Temperature = 0.0f,
            MaxTokens = 200
        };

        try
        {
            var chatService = _kernel.GetChatCompletionService();
            var results = await chatService.GetApiMessageContentsAsync(
                chatHistory,
                executionSettings,
                _kernel,
                cancellationToken).ConfigureAwait(false);

            var content = results.Count > 0 ? results[0].Content : null;
            return ParseEvaluationResult(content);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, L.T(StringKey.GoalEvaluatorCallFailed));
            return GoalEvaluationResult.NotCompleted(L.T(StringKey.GoalEvaluatorCallFailed));
        }
    }

    internal static GoalEvaluationResult ParseEvaluationResult(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return GoalEvaluationResult.NotCompleted(L.T(StringKey.GoalEvaluatorEmptyResult));
        }

        var result = LlmJsonHelper.Deserialize(content, GoalJsonContext.Default.GoalEvaluationJson, out var repairHint);
        if (result is not null)
        {
            if (repairHint is not null)
                System.Diagnostics.Trace.WriteLine($"Goal evaluation JSON repaired: {repairHint}");

            return result.Completed
                ? GoalEvaluationResult.Completed(result.Reason)
                : GoalEvaluationResult.NotCompleted(result.Reason);
        }

        return GoalEvaluationResult.NotCompleted(L.T(StringKey.GoalEvaluatorFormatError, content.Trim()));
    }

    private static string BuildEvaluatorPrompt(string objective, IReadOnlyList<string> constraints, string recentConversation)
    {
        var constraintsText = constraints.Count > 0
            ? string.Join("\n", constraints.Select(c => $"- {c}"))
            : L.T(StringKey.GoalEvaluatorNoConstraints);

        return $$$"""
            You are a goal completion evaluator. Your job is to determine whether the stated objective has been achieved based on the conversation evidence.

            OBJECTIVE:
            {{{objective}}}

            CONSTRAINTS:
            {{{constraintsText}}}

            RECENT CONVERSATION:
            {{{recentConversation}}}

            INSTRUCTIONS:
            - Return a JSON object with exactly two fields: "completed" (boolean) and "reason" (string).
            - "completed" is true ONLY if the objective has been fully achieved and no required work remains.
            - "reason" should briefly explain what evidence supports your judgment.
            - Do NOT accept proxy signals (passing tests alone, partial implementation, elapsed effort).
            - Treat uncertainty as NOT completed.
            - You can only judge based on what appears in the conversation. You cannot execute tools.

            RESPONSE FORMAT:
            Output a JSON block wrapped in ```json and ```:
            ```json
            {"completed": true/false, "reason": "..."}
            ```
            """;
    }
}
