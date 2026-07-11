
namespace Core.Agents;

/// <summary>
/// 计划 Agent - 制定任务执行计划
/// </summary>
public sealed class PlanAgent : BuiltInAgentBase
{
    public override string Name => "PlanAgent";
    public override string Description => "制定清晰、可执行的任务计划，将复杂任务分解为可管理的步骤";
    public override BuiltInAgentType AgentType => BuiltInAgentType.Plan;
    public override string SystemPrompt => AgentPrompts.PlanAgentSystemPrompt;

    public PlanAgent(
        IChatClient kernel,
        IClockService clock,
        ILogger<PlanAgent>? logger = null)
        : base(kernel, clock, logger)
    {
    }

    /// <summary>
    /// 制定计划
    /// </summary>
    public async Task<PlanResult> CreatePlanAsync(
        PlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var prompt = BuildPlanPrompt(request);
        var response = await ProcessAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);

        return new PlanResult
        {
            Success = true,
            PlanId = Guid.NewGuid().ToString("N")[..8],
            Content = response.Content,
            ExecutionTimeMs = response.ExecutionTimeMs,
            TokenUsage = response.TokenUsage
        };
    }

    /// <summary>
    /// 优化现有计划
    /// </summary>
    public async Task<PlanResult> RefinePlanAsync(
        string existingPlan,
        string feedback,
        CancellationToken cancellationToken = default)
    {
        var prompt = $"""
请根据以下反馈优化现有计划：

## 现有计划
{existingPlan}

## 反馈意见
{feedback}

请提供优化后的计划，并说明改进之处。
""";

        var response = await ProcessAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);

        return new PlanResult
        {
            Success = true,
            PlanId = Guid.NewGuid().ToString("N")[..8],
            Content = response.Content,
            ExecutionTimeMs = response.ExecutionTimeMs,
            TokenUsage = response.TokenUsage
        };
    }

    private static string BuildPlanPrompt(PlanRequest request)
    {
        var prompt = new System.Text.StringBuilder();
        prompt.AppendLine("请为以下目标制定详细的执行计划：");
        prompt.AppendLine();
        prompt.AppendLine($"## 目标\n{request.Goal}");

        if (!string.IsNullOrWhiteSpace(request.Context))
        {
            prompt.AppendLine();
            prompt.AppendLine($"## 上下文\n{request.Context}");
        }

        if (request.Constraints != null && request.Constraints.Count > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine("## 约束条件");
            foreach (var constraint in request.Constraints)
            {
                prompt.AppendLine($"- {constraint}");
            }
        }

        if (request.Preferences != null && request.Preferences.Count > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine("## 偏好设置");
            foreach (var preference in request.Preferences)
            {
                prompt.AppendLine($"- {preference}");
            }
        }

        prompt.AppendLine();
        prompt.AppendLine("请按照系统提示词中指定的格式输出计划。");

        return prompt.ToString();
    }

    protected override float GetTemperature() => 0.5f;
}

/// <summary>
/// 计划请求
/// </summary>
public sealed record PlanRequest
{
    public required string Goal { get; init; }
    public string? Context { get; init; }
    public List<string>? Constraints { get; init; }
    public List<string>? Preferences { get; init; }
}

/// <summary>
/// 计划结果
/// </summary>
public sealed record PlanResult
{
    public required bool Success { get; init; }
    public string? PlanId { get; init; }
    public string? Content { get; init; }
    public long ExecutionTimeMs { get; init; }
    public TokenUsage TokenUsage { get; init; } = new();
    public string? ErrorMessage { get; init; }
}
