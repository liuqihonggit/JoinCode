
namespace Core.Goal;

using JoinCode.Abstractions.Models.Goal;

[Register]
public sealed partial class DecomposabilityAnalyzer : ServiceEntity, IDecomposabilityAnalyzer
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

        var envOverride = Environment.GetEnvironmentVariable("JCC_CLUSTER_DECOMPOSITION_OVERRIDE");
        if (!string.IsNullOrWhiteSpace(envOverride))
        {
            _logger?.LogInformation("Decomposability analyzer using environment override");
            return ParseAnalysisResult(envOverride);
        }

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

            var complexity = ComplexityLevelExtensions.FromValue(result.Complexity) ?? ComplexityLevel.Medium;
            var mode = ExecutionModeExtensions.FromValue(result.Mode) ?? ExecutionMode.PlanA;
            var rationale = result.Rationale;

            var reason = result.Reason;
            if (report.FormatForLlm() is { Length: > 0 } detail)
                reason = $"{reason} [宽容修复: {detail}]";

            return result.IsDecomposable
                ? DecompositionResult.Decomposable(reason, subTasks, complexity, mode, rationale)
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

            THINKING CHAIN (follow this order strictly):
            Step 1 — Analyze decomposability: Can the objective be split into 2-8 parallel subtasks?
            Step 2 — Assess complexity: Estimate the complexity level (low/medium/high).
            Step 3 — Choose execution mode: Select plan A or plan B based on dependency structure.
            Step 4 — List subtasks: Enumerate subtasks with ids, dependencies, and owned files.

            DECOMPOSABILITY RULES:
            - A task is decomposable if:
              1. It involves multiple independent files, modules, or features
              2. Subtasks can work on different parts without frequent conflicts
              3. Dependencies between subtasks are acyclic and well-defined
            - A task is NOT decomposable if:
              1. It is a single focused change in one file/module
              2. Subtasks would heavily share the same files (high conflict risk)
              3. The objective is too small to benefit from parallelization
<<<<<<< HEAD

            COMPLEXITY LEVELS:
            - "low": 1-5 subtasks, simple independent changes
            - "medium": 6-20 subtasks, moderate dependencies and coordination
            - "high": more than 20 subtasks, complex orchestration (rarely decomposable within limits)

            EXECUTION MODES:
            - "A" (serial-first): Use when subtasks have strong sequential dependencies. The first subtask should be a serial/setup node, and the last subtask should be a review/integration node. Middle subtasks may still run in parallel if independent.
            - "B" (parallel-first): Use when subtasks are mostly independent with minimal dependencies. The first subtask should be a parallel dispatch node. Suitable for low-complexity objectives with no shared state.
            - Choose "A" when dependsOn chains are deep or a final review step is needed.
            - Choose "B" when most subtasks have empty dependsOn and no integration step is required.

            SUBTASK SPECIFICATION:
            - id: short identifier (e.g., "sub_1", "sub_2")
            - title: concise name
            - description: what to implement/fix
            - dependsOn: list of subtask IDs this depends on (empty if independent)
            - ownedFiles: list of files this subtask will primarily modify
            - priority: "high", "medium", or "low"
            - variant: "code" for implementation, "explore" for analysis/research
            - Ensure ownedFiles overlap between subtasks is MINIMAL to avoid merge conflicts.
            - Ensure dependsOn forms a valid DAG (no cycles).
            - Ensure the declared complexity is consistent with the actual number of subtasks.
            - For mode "A": first subtask should have no dependencies (serial start), last subtask should depend on all others (review end).
            - For mode "B": first subtask should have no dependencies (parallel start), remaining subtasks should have empty or minimal dependsOn.
=======
            - Assess the complexity level of the objective:
              - "low": 1-5 subtasks, simple independent changes
              - "medium": 6-20 subtasks, moderate dependencies and coordination
              - "high": more than 20 subtasks, complex orchestration (rarely decomposable within limits)
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
            - Ensure the declared complexity is consistent with the actual number of subtasks.
>>>>>>> f3ccf043b (feat: P1-1 复杂度档次机制 | 决策: ComplexityLevel枚举(low/medium/high)+[EnumValue],ValidateComplexityConsistency按子任务数校验档次一致性,边界互斥告警含complexity_mismatch标识)

            RESPONSE FORMAT:
            Output a JSON block wrapped in ```json and ```:
            ```json
            {
              "isDecomposable": true/false,
              "reason": "brief explanation",
              "complexity": "low" | "medium" | "high",
<<<<<<< HEAD
              "mode": "A" | "B",
              "rationale": "why this mode and complexity were chosen",
=======
>>>>>>> f3ccf043b (feat: P1-1 复杂度档次机制 | 决策: ComplexityLevel枚举(low/medium/high)+[EnumValue],ValidateComplexityConsistency按子任务数校验档次一致性,边界互斥告警含complexity_mismatch标识)
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
