
namespace McpToolHandlers;

/// <summary>
/// 目标工具处理器 — 模型可通过 MCP 工具查询和更新目标状态
/// </summary>
[McpToolHandler(ToolCategory.Goal)]
public sealed class GoalToolHandlers
{
    private readonly IGoalEngine _goalEngine;

    public GoalToolHandlers(IGoalEngine goalEngine)
    {
        _goalEngine = goalEngine ?? throw new ArgumentNullException(nameof(goalEngine));
    }

    /// <summary>
    /// 获取当前目标状态 — 模型可调用此工具查询正在执行的目标信息
    /// </summary>
    [McpTool(SystemToolNameConstants.GoalGet, "Get the current goal status including objective, progress, and evaluation results. Returns null if no goal is active.", "goal")]
    public Task<ToolResult> GetGoalAsync(
        CancellationToken cancellationToken = default)
    {
        var state = _goalEngine.CurrentState;

        if (state == null)
        {
            return Task.FromResult(McpResultBuilder.Success()
                .WithText("No active goal")
                .Build());
        }

        var response = new StringBuilder();
        response.AppendLine($"Goal: {state.Objective}");
        response.AppendLine($"Status: {state.Status}");
        response.AppendLine($"Turns: {state.TurnsCompleted}");
        response.AppendLine($"Tokens: {state.TokensUsed}{(state.TokenBudget.HasValue ? $"/{state.TokenBudget.Value}" : "")}");
        response.AppendLine($"Elapsed: {(int)state.Elapsed.TotalSeconds}s");

        if (state.Constraints.Count > 0)
        {
            response.AppendLine($"Constraints: {string.Join("; ", state.Constraints)}");
        }

        if (state.LastEvaluation != null)
        {
            response.AppendLine($"Last evaluation: {(state.LastEvaluation.IsCompleted ? "COMPLETED" : "NOT COMPLETED")} — {state.LastEvaluation.Reason}");
        }

        return Task.FromResult(McpResultBuilder.Success()
            .WithText(response.ToString())
            .Build());
    }

    /// <summary>
    /// 更新目标状态 — 模型可标记目标为已完成或无法完成
/// 仅允许 achieved/unmet，通过引擎方法安全更新（线程安全）
    /// </summary>
    [McpTool(SystemToolNameConstants.GoalUpdate, "Update the current goal status. The model can mark a goal as achieved or unmet. Only 'achieved' and 'unmet' statuses are allowed via this tool.", "goal")]
    public async Task<ToolResult> UpdateGoalAsync(
        [McpToolParameter("New status for the goal. Must be 'achieved' or 'unmet'.", Required = true, EnumValues = new[] { "achieved", "unmet" })] string status,
        [McpToolParameter("Reason for the status change", Required = true)] string reason,
        CancellationToken cancellationToken = default)
    {
        if (_goalEngine.CurrentState == null)
        {
            return McpResultBuilder.Error()
                .WithText("No active goal to update")
                .Build();
        }

        if (string.IsNullOrWhiteSpace(status))
        {
            return McpResultBuilder.Error()
                .WithText("status is required. Must be 'achieved' or 'unmet'")
                .Build();
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return McpResultBuilder.Error()
                .WithText("reason is required")
                .Build();
        }

        var objective = _goalEngine.CurrentState.Objective;

        var goalStatus = GoalStatusExtensions.FromValue(status) ?? throw new ArgumentException($"Invalid goal status: {status}");

        switch (goalStatus)
        {
            case GoalStatus.Achieved:
                await _goalEngine.MarkCompletedAsync(reason, cancellationToken).ConfigureAwait(false);
                break;

            case GoalStatus.Unmet:
                await _goalEngine.MarkUnmetAsync(reason, cancellationToken).ConfigureAwait(false);
                break;

            default:
                return McpResultBuilder.Error()
                    .WithText($"Invalid status: '{status}'. Must be 'achieved' or 'unmet'")
                    .Build();
        }

        var finalStatus = _goalEngine.CurrentState?.Status.ToString() ?? status;
        return McpResultBuilder.Success()
            .WithText($"Goal updated: {objective} → {finalStatus} ({reason})")
            .Build();
    }
}
