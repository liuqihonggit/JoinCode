
namespace McpToolDispatch;

/// <summary>
/// 目标工具处理器 — 模型可通过 MCP 工具查询和更新目标状态
/// </summary>
[McpToolDispatch(ToolCategory.Goal)]
public sealed class GoalToolHandlers {
    private readonly IGoalEngine _goalEngine;

    /// <summary>
    /// 初始化目标工具处理器
    /// </summary>
    /// <param name="goalEngine">目标引擎</param>
    public GoalToolHandlers(IGoalEngine goalEngine) {
        _goalEngine = goalEngine ?? throw new ArgumentNullException(nameof(goalEngine));
    }

    /// <summary>
    /// 获取当前目标状态 — 模型可调用此工具查询正在执行的目标信息
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果</returns>
    [McpTool(SystemToolNameEnumConstants.GoalGet, "Get the current goal status including objective, progress, and evaluation results. Returns null if no goal is active.", "goal")]
    public Task<ToolResult> GetGoalAsync(
        CancellationToken cancellationToken = default) {
        var state = _goalEngine.CurrentState;

        if (state == null) {
            return Task.FromResult(ToolResultBuilder.Success()
                .WithText("No active goal")
                .Build());
        }

        var response = new StringBuilder();
        response.AppendLine($"Goal: {state.Objective}");
        response.AppendLine($"Status: {state.Status}");
        response.AppendLine($"Turns: {state.TurnsCompleted}");
        response.AppendLine($"Tokens: {state.TokensUsed}{(state.TokenBudget.HasValue ? $"/{state.TokenBudget.Value}" : "")}");
        response.AppendLine($"Elapsed: {(int)state.Elapsed.TotalSeconds}s");

        if (state.Constraints.Count > 0) {
            response.AppendLine($"Constraints: {string.Join("; ", state.Constraints)}");
        }

        if (state.LastEvaluation != null) {
            response.AppendLine($"Last evaluation: {(state.LastEvaluation.IsCompleted ? "COMPLETED" : "NOT COMPLETED")} — {state.LastEvaluation.Reason}");
        }

        return Task.FromResult(ToolResultBuilder.Success()
            .WithText(response.ToString())
            .Build());
    }

    /// <summary>
    /// 更新目标状态 — 模型可标记目标为已完成或无法完成
    /// 仅允许 achieved/unmet，通过引擎方法安全更新（线程安全）
    /// </summary>
    /// <param name="status">目标新状态，必须为 achieved 或 unmet</param>
    /// <param name="reason">状态变更原因</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果</returns>
    [McpTool(SystemToolNameEnumConstants.GoalUpdate, "Update the current goal status. The model can mark a goal as achieved or unmet. Only 'achieved' and 'unmet' statuses are allowed via this tool.", "goal")]
    public async Task<ToolResult> UpdateGoalAsync(
        [McpToolParameter("New status for the goal. Must be 'achieved' or 'unmet'.", Required = true, EnumValues = new[] { GoalStatusEnumConstants.Achieved, GoalStatusEnumConstants.Unmet })] string status,
        [McpToolParameter("Reason for the status change", Required = true)] string reason,
        CancellationToken cancellationToken = default) {
        if (_goalEngine.CurrentState == null) {
            return ToolResultBuilder.Error()
                .WithText("No active goal to update")
                .Build();
        }

        if (string.IsNullOrWhiteSpace(status)) {
            return ToolResultBuilder.Error()
                .WithText("status is required. Must be 'achieved' or 'unmet'")
                .Build();
        }

        if (string.IsNullOrWhiteSpace(reason)) {
            return ToolResultBuilder.Error()
                .WithText("reason is required")
                .Build();
        }

        var objective = _goalEngine.CurrentState.Objective;

        var goalStatus = GoalStatusExtensions.FromValue(status) ?? throw new ArgumentException($"Invalid goal status: {status}");

        switch (goalStatus) {
            case GoalStatus.Achieved:
            await _goalEngine.MarkCompletedAsync(reason, cancellationToken).ConfigureAwait(false);
            break;

            case GoalStatus.Unmet:
            await _goalEngine.MarkUnmetAsync(reason, cancellationToken).ConfigureAwait(false);
            break;

            default:
            return ToolResultBuilder.Error()
                .WithText($"Invalid status: '{status}'. Must be 'achieved' or 'unmet'")
                .Build();
        }

        var finalStatus = _goalEngine.CurrentState?.Status.ToString() ?? status;
        return ToolResultBuilder.Success()
            .WithText($"Goal updated: {objective} → {finalStatus} ({reason})")
            .Build();
    }

    /// <summary>
    /// 定义 Goal Graph — 协调者 Agent 调研后调用此工具定义执行图结构
    /// </summary>
    /// <param name="nodes">节点 JSON 数组，每个节点：{id, kind, name, systemPrompt?, instruction?, freshContext?}，kind 为 agent/function/join</param>
    /// <param name="edges">边 JSON 数组，每条边：{id?, fromId, toId, label?}，空 label 为无条件，非空为条件路由键</param>
    /// <param name="start_node_id">起始节点 ID</param>
    /// <param name="end_node_ids">结束节点 ID，逗号分隔</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果</returns>
    [McpTool(SystemToolNameEnumConstants.GoalGraphDefine, "Define a goal execution graph with nodes and edges. The coordinator agent uses this after investigating the task to create an optimal execution plan. Each node is an agent loop, edges define execution flow and conditional routing.", "goal")]
    public Task<ToolResult> DefineGraphAsync(
        [McpToolParameter("JSON array of nodes. Each node: {id, kind, name, systemPrompt?, instruction?, freshContext?}. kind: agent/function/join", Required = true)] string nodes,
        [McpToolParameter("JSON array of edges. Each edge: {id?, fromId, toId, label?}. Empty label = unconditional, non-empty = conditional route key", Required = true)] string edges,
        [McpToolParameter("Start node ID", Required = true)] string start_node_id,
        [McpToolParameter("End node IDs, comma-separated", Required = true)] string end_node_ids,
        CancellationToken cancellationToken = default) {
        try {
            _goalEngine.SetGraphDefinition(nodes, edges, start_node_id, end_node_ids);

            return Task.FromResult(ToolResultBuilder.Success()
                .WithText($"Graph defined successfully. Start: {start_node_id}, Ends: {end_node_ids}. The graph will execute when the goal loop continues.")
                .Build());
        } catch (Exception ex) {
            return Task.FromResult(ToolResultBuilder.Error()
                .WithText($"Failed to define graph: {ex.Message}")
                .Build());
        }
    }
}