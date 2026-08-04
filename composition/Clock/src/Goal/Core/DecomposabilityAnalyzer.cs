
namespace Core.Goal;

using JoinCode.Abstractions.Models.Goal;

[Register]
public sealed partial class DecomposabilityAnalyzer : IDecomposabilityAnalyzer
{
    private readonly IChatClient _kernel;
    [Inject] private readonly ILogger<DecomposabilityAnalyzer>? _logger;

    public DecomposabilityAnalyzer(IChatClient kernel, ILogger<DecomposabilityAnalyzer>? logger = null)
    {
        _kernel = kernel;
        _logger = logger;
    }

    public async Task<DecompositionResult> AnalyzeAsync(
        string objective,
        IReadOnlyList<string> constraints,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objective);

        var prompt = BuildAnalyzerPrompt(objective, constraints);

        var chatHistory = new MessageList();
        chatHistory.AddSystemMessage(prompt);
        chatHistory.AddUserMessage("Analyze whether this objective can be decomposed into parallel subtasks.");

        var executionSettings = new ChatOptions
        {
            Temperature = 0.0f,
            MaxTokens = 1000
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
            return ParseAnalysisResult(content);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Decomposability analyzer LLM call failed");
            return DecompositionResult.NotDecomposable("分解分析器不可用");
        }
    }

    internal static DecompositionResult ParseAnalysisResult(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return DecompositionResult.NotDecomposable("分解分析器返回空结果");
        }

        var result = LlmJsonHelper.DeserializeWithReport(content, GoalJsonContext.Default.DecompositionAnalysisJson, out var report);
        if (result is not null)
        {
            if (report.RepairHint is not null)
                _ = report.RepairHint;

            var subTasks = result.SubTasks.Select((s, i) => new SubTaskDefinition
            {
                Id = string.IsNullOrWhiteSpace(s.Id) ? $"sub_{i + 1}" : s.Id,
                Title = s.Title,
                Description = s.Description,
                DependsOn = s.DependsOn,
                OwnedFiles = s.OwnedFiles,
                Priority = SubTaskPriorityExtensions.FromValue(s.Priority) ?? SubTaskPriority.Medium,
                Variant = ExecutorVariantExtensions.FromValue(s.Variant) ?? ExecutorVariant.Code,
            }).ToList();

            var reason = result.Reason;
            if (report.FormatForLlm() is { Length: > 0 } detail)
                reason = $"{reason} [宽容修复: {detail}]";

            return result.IsDecomposable
                ? DecompositionResult.Decomposable(reason, subTasks)
                : DecompositionResult.NotDecomposable(reason);
        }

        var formatError = $"分解分析结果格式异常: {content.Trim()}";
        if (report.FormatForLlm() is { Length: > 0 } failureDetail)
            formatError = $"{formatError} | 解析明细: {failureDetail}";
        return DecompositionResult.NotDecomposable(formatError);
    }

    private static string BuildAnalyzerPrompt(string objective, IReadOnlyList<string> constraints)
    {
        var constraintsText = constraints.Count > 0
            ? string.Join("\n", constraints.Select(c => $"- {c}"))
            : "无特殊约束";

        return $$$"""
            You are a task decomposition analyst for a parallel agent system. Your job is to determine whether a given objective can be split into independent or partially-dependent subtasks that can execute in parallel.

            OBJECTIVE:
            {{{objective}}}

            CONSTRAINTS:
            {{{constraintsText}}}

            INSTRUCTIONS:
            - Analyze whether the objective can be decomposed into 2-8 parallel subtasks.
            - A task is decomposable if:
              1. It involves multiple independent files, modules, or features
              2. Subtasks can work on different parts without frequent conflicts
              3. Dependencies between subtasks are acyclic and well-defined
            - A task is NOT decomposable if:
              1. It is a single focused change in one file/module
              2. Subtasks would heavily share the same files (high conflict risk)
              3. The objective is too small to benefit from parallelization
            - For each subtask, specify:
              - id: short identifier (e.g., "sub_1", "sub_2")
              - title: concise name
              - description: what to implement/fix
              - dependsOn: list of subtask IDs this depends on (empty if independent)
              - ownedFiles: list of files this subtask will primarily modify
              - priority: "high", "medium", or "low"
              - variant: "code" for implementation, "explore" for analysis/research
            - Ensure ownedFiles overlap between subtasks is MINIMAL to avoid merge conflicts.
            - Ensure dependsOn forms a valid DAG (no cycles).

            RESPONSE FORMAT:
            Output a JSON block wrapped in ```json and ```:
            ```json
            {
              "isDecomposable": true/false,
              "reason": "brief explanation",
              "subTasks": [
                {
                  "id": "sub_1",
                  "title": "...",
                  "description": "...",
                  "dependsOn": [],
                  "ownedFiles": ["path/to/file1.cs"],
                  "priority": "high",
                  "variant": "code"
                }
              ]
            }
            ```
            If isDecomposable is false, subTasks should be an empty array.
            """;
    }
}
