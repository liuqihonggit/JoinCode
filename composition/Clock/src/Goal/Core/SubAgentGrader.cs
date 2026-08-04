
namespace Core.Goal;

[Register]
public sealed partial class SubAgentGrader : ISubAgentGrader
{
    private readonly IChatClient _kernel;
    [Inject] private readonly ILogger<SubAgentGrader>? _logger;

    private const double PassThreshold = 0.6;

    public SubAgentGrader(IChatClient kernel, ILogger<SubAgentGrader>? logger = null)
    {
        _kernel = kernel;
        _logger = logger;
    }

    public async Task<GradingResult> GradeAsync(GradingContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var ruleResult = EvaluateRules(context);

        if (ruleResult.Score <= 0.0)
        {
            return ruleResult;
        }

        try
        {
            var llmResult = await EvaluateWithLlmAsync(context, ct).ConfigureAwait(false);
            if (llmResult is not null)
            {
                return llmResult;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "LLM grading failed, using rule-based fallback");
        }

        return ruleResult;
    }

    internal static GradingResult EvaluateRules(GradingContext context)
    {
        var criteria = new List<GradingCriterion>();
        var totalScore = 0.0;
        var maxScore = 0.0;

        if (!context.IsSuccess)
        {
            criteria.Add(new GradingCriterion { Name = "execution", Score = 0.0, Feedback = $"执行失败: {context.Error ?? "unknown"}" });
            return GradingResult.FromRules(0.0, "执行失败", criteria);
        }

        maxScore += 1.0;
        criteria.Add(new GradingCriterion { Name = "execution", Score = 1.0, Feedback = "执行成功" });
        totalScore += 1.0;

        if (context.CheckpointResult is not null)
        {
            maxScore += 1.0;
            if (context.CheckpointResult.Passed)
            {
                totalScore += 1.0;
                criteria.Add(new GradingCriterion { Name = "checkpoint", Score = 1.0, Feedback = "质量关卡通过" });
            }
            else
            {
                var violations = string.Join(", ", context.CheckpointResult.Violations.Select(v => v.Message));
                criteria.Add(new GradingCriterion { Name = "checkpoint", Score = 0.0, Feedback = $"质量关卡未通过: {violations}" });
            }
        }

        maxScore += 1.0;
        if (!string.IsNullOrWhiteSpace(context.AgentOutput))
        {
            totalScore += 1.0;
            criteria.Add(new GradingCriterion { Name = "output", Score = 1.0, Feedback = "有输出" });
        }
        else
        {
            criteria.Add(new GradingCriterion { Name = "output", Score = 0.0, Feedback = "无输出" });
        }

        var finalScore = maxScore > 0 ? totalScore / maxScore : 0.0;
        return GradingResult.FromRules(finalScore, $"规则评分: {finalScore:P0}", criteria);
    }

    private async Task<GradingResult?> EvaluateWithLlmAsync(GradingContext context, CancellationToken ct)
    {
        var prompt = BuildGradingPrompt(context);

        var chatHistory = new MessageList();
        chatHistory.AddSystemMessage(prompt);
        chatHistory.AddUserMessage("Grade the sub-agent's work quality.");

        var executionSettings = new ChatOptions
        {
            Temperature = 0.0f,
            MaxTokens = 500
        };

        var chatService = _kernel.GetChatCompletionService();
        var results = await chatService.GetApiMessageContentsAsync(chatHistory, executionSettings, _kernel, ct).ConfigureAwait(false);

        var content = results.Count > 0 ? results[0].Content : null;
        return ParseGradingResult(content);
    }

    internal static GradingResult? ParseGradingResult(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var result = LlmJsonHelper.DeserializeWithReport(content, GoalJsonContext.Default.GradingAnalysisJson, out var report);
        if (result is null)
        {
            return null;
        }

        var criteria = result.Criteria.Select(c => new GradingCriterion
        {
            Name = c.Name,
            Score = c.Score,
            Feedback = c.Feedback
        }).ToList();

        var avgScore = criteria.Count > 0 ? criteria.Average(c => c.Score) : 0.0;
        var reason = result.Reason;
        if (report.FormatForLlm() is { Length: > 0 } detail)
        {
            reason = $"{reason} [宽容修复: {detail}]";
        }

        return GradingResult.FromLlm(avgScore, reason, criteria);
    }

    private static string BuildGradingPrompt(GradingContext context)
    {
        var checkpointStatus = context.CheckpointResult?.Passed == true ? "passed" : "failed or not run";
        var diffInfo = context.DiffSummary ?? "not available";

        return $$$"""
            You are a code quality grader for a parallel agent system. Grade the sub-agent's work on a 0-1 scale per criterion.

            TASK: {{{context.TaskDescription}}}
            EXECUTION STATUS: {{{(context.IsSuccess ? "success" : "failed")}}}
            CHECKPOINT STATUS: {{{checkpointStatus}}}
            AGENT OUTPUT: {{{(context.AgentOutput.Length > 2000 ? context.AgentOutput[..2000] + "..." : context.AgentOutput)}}}
            DIFF SUMMARY: {{{diffInfo}}}

            GRADE THESE CRITERIA (0-1 each):
            1. "correctness" — Does the output correctly address the task?
            2. "completeness" — Is the task fully completed?
            3. "quality" — Code quality, no obvious bugs, follows conventions?

            RESPONSE FORMAT:
            Output a JSON block wrapped in ```json and ```:
            ```json
            {
              "reason": "brief overall assessment",
              "criteria": [
                {"name": "correctness", "score": 0.8, "feedback": "..."},
                {"name": "completeness", "score": 0.7, "feedback": "..."},
                {"name": "quality", "score": 0.9, "feedback": "..."}
              ]
            }
            ```
            """;
    }
}
