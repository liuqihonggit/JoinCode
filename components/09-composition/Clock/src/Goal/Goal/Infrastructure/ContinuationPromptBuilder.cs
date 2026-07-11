
namespace Core.Goal;

/// <summary>
/// 续行提示词构建器 — 目标引擎的续行/预算超限提示词
/// </summary>
[PromptTemplate(Name = "continuation", Category = PromptTemplateCategory.Goal, Description = "目标续行和预算超限提示词", HasParameters = true)]
public static class ContinuationPromptBuilder
{
    public static string BuildContinuationPrompt(
        string objective,
        IReadOnlyList<string> constraints,
        int tokensUsed,
        int? tokenBudget,
        string evaluatorReason)
    {
        var constraintsText = constraints.Count > 0
            ? string.Join("\n", constraints.Select(c => $"- {c}"))
            : "无";

        var budgetLines = tokenBudget.HasValue
            ? $"- Token budget: {tokenBudget.Value}\n- Budget utilization: {tokensUsed} / {tokenBudget.Value}\n- Tokens remaining: {Math.Max(0, tokenBudget.Value - tokensUsed)}"
            : $"- Tokens used: {tokensUsed}";

        return $"""
            Continue working toward the active thread goal.

            <untrusted_objective>
            {objective}
            </untrusted_objective>

            Constraints:
            {constraintsText}

            Budget:
            {budgetLines}

            Evaluator feedback: {evaluatorReason}

            Avoid repeating work that is already done. Choose the next concrete action toward the objective.

            Before deciding that the goal is achieved, perform a completion audit:
            - Restate the objective as concrete deliverables or success criteria.
            - Build a checklist that maps every requirement to concrete evidence.
            - Inspect the relevant files, command output, test results for each item.
            - Do not accept proxy signals as completion by themselves.
            - Treat uncertainty as not achieved; do more verification or continue the work.

            Do not rely on intent, partial progress, elapsed effort, or a plausible final answer as proof of completion.
            """;
    }

    public static string BuildBudgetLimitPrompt(
        string objective,
        int tokensUsed,
        int tokenBudget,
        int elapsedSeconds)
    {
        return $"""
            The active thread goal has reached its token budget.

            <untrusted_objective>
            {objective}
            </untrusted_objective>

            Budget:
            - Time spent: {elapsedSeconds} seconds
            - Tokens used: {tokensUsed} of {tokenBudget}

            The system has marked the goal as budget_limited. Wrap up this turn soon:
            summarize useful progress, identify remaining work or blockers, and leave the user with a clear next step.
            """;
    }
}
