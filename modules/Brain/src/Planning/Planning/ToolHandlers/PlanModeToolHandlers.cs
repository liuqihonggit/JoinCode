
namespace Core.Planning.ToolHandlers;

/// <summary>
/// 计划模式工具处理器 - 提供计划模式管理功能
/// </summary>
[McpToolHandler(ToolCategory.Plan)]
public class PlanModeToolHandlers
{
    private readonly IPlanModeManager _planModeManager;
    private readonly IChannelStateService? _channelStateService;

    public PlanModeToolHandlers(IPlanModeManager planModeManager, IChannelStateService? channelStateService = null)
    {
        _planModeManager = planModeManager ?? throw new ArgumentNullException(nameof(planModeManager));
        _channelStateService = channelStateService;
    }

    [McpTool(PlanToolNameConstants.EnterPlanMode, "Enter plan mode for complex tasks requiring exploration and design", "plan")]
    public async Task<ToolResult> EnterPlanModeAsync(
        [McpToolParameter("Plan description (optional)", Required = false)] string? description = null,
        CancellationToken cancellationToken = default)
    {
        // 对齐 TS EnterPlanModeTool.isEnabled: channels 激活时禁用 PlanMode
        // 原因: 退出审批对话框需要终端交互，channels 用户不在终端前会导致对话框挂起
        if (_channelStateService?.IsChannelsEnabled == true)
        {
            return McpResultBuilder.Error().WithText("Plan mode is disabled when channels are active. The plan-approval dialog requires terminal interaction, which is not available when the user is on an external channel (Telegram/Discord/etc.).").Build();
        }

        var result = await _planModeManager.EnterPlanModeAsync(
            description,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!result.Success)
            return McpResultBuilder.Error().WithText(result.ErrorMessage ?? "Failed to enter plan mode").Build();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Entered plan mode. You should now focus on exploring the codebase and designing an implementation approach.");
        sb.AppendLine();

        // 对齐 TS plan_mode attachment: 注入 plan 文件路径，让 LLM 知道应该用 FileWriteTool 写 plan
        if (result.PlanState?.PlanFilePath is not null)
        {
            sb.AppendLine($"## Plan File");
            sb.AppendLine($"You should write your plan to: {result.PlanState.PlanFilePath}");
            sb.AppendLine($"Use the FileWriteTool to create and update this plan file as you explore.");
            sb.AppendLine();
        }

        sb.AppendLine("In plan mode, you should:");
        sb.AppendLine("1. Thoroughly explore the codebase to understand existing patterns");
        sb.AppendLine("2. Identify similar features and architectural approaches");
        sb.AppendLine("3. Consider multiple approaches and their trade-offs");
        sb.AppendLine("4. Use AskUserQuestion if you need to clarify the approach");
        sb.AppendLine("5. Design a concrete implementation strategy");
        sb.AppendLine("6. When ready, use ExitPlanMode to present your plan for approval");
        sb.AppendLine();
        sb.AppendLine("Remember: DO NOT write or edit any files yet. This is a read-only exploration and planning phase.");

        return McpResultBuilder.Success().WithText(sb.ToString()).Build();
    }

    [McpTool(PlanToolNameConstants.ExitPlanMode, "Exit plan mode and present plan for approval", "plan")]
    public async Task<ToolResult> ExitPlanModeAsync(
        [McpToolParameter("Whether to execute remaining approved steps", Required = false)] bool? execute_remaining_steps = false,
        [McpToolParameter("Prompt-based permissions needed to implement the plan. Each entry specifies a tool and a semantic description of the action, e.g. {\"tool\":\"Bash\",\"prompt\":\"run tests\"}", Required = false)] Dictionary<string, JsonElement>[]? allowed_prompts = null,
        CancellationToken cancellationToken = default)
    {
        // 对齐 TS ExitPlanModeV2Tool.isEnabled: channels 激活时禁用 PlanMode 退出
        // 原因: 与 EnterPlanMode 配对，防止模型进入 PlanMode 后无法退出
        if (_channelStateService?.IsChannelsEnabled == true)
        {
            return McpResultBuilder.Error().WithText("Plan mode exit is disabled when channels are active. The plan-approval dialog requires terminal interaction.").Build();
        }

        // 对齐 TS AllowedPrompt: 将 Dictionary[] 转换为结构化 AllowedPrompt[]
        AllowedPrompt[]? typedPrompts = allowed_prompts?.Select(d => new AllowedPrompt
        {
            Tool = d.TryGetValue("tool", out var toolEl) ? toolEl.GetString() ?? AllowedPromptToolConstants.Bash : AllowedPromptToolConstants.Bash,
            Prompt = d.TryGetValue("prompt", out var promptEl) ? promptEl.GetString() ?? "" : ""
        }).ToArray();

        var result = await _planModeManager.ExitPlanModeAsync(
            execute_remaining_steps ?? false,
            allowedPrompts: typedPrompts,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
            return McpResultBuilder.Error().WithText(result.ErrorMessage ?? "Failed to exit plan mode").Build();

        // 对齐 TS: teammate 退出 PlanMode 时等待 leader 审批
        // TS 返回 { awaitingLeaderApproval: true, requestId, plan, filePath }
        if (result.AwaitingLeaderApproval)
        {
            var approvalSb = new System.Text.StringBuilder();
            approvalSb.AppendLine("Plan approval request sent to team lead. Waiting for approval...");
            if (!string.IsNullOrEmpty(result.ApprovalRequestId))
                approvalSb.AppendLine($"Approval Request ID: {result.ApprovalRequestId}");
            if (!string.IsNullOrEmpty(result.PlanFileContent))
            {
                approvalSb.AppendLine();
                approvalSb.AppendLine("Plan content:");
                approvalSb.AppendLine(result.PlanFileContent);
            }
            return McpResultBuilder.Success().WithText(approvalSb.ToString()).Build();
        }

        var sb = new System.Text.StringBuilder();
        // 对齐 TS mapToolResult: planWasEdited 标记
        var editedTag = result.PlanState?.WasEditedByUser == true ? " (edited by user)" : "";
        sb.AppendLine($"User has approved your plan{editedTag}. You can now start coding. Start with updating your todo list if applicable");

        if (result.PlanState != null)
        {
            sb.AppendLine();
            sb.AppendLine($"Plan ID: {result.PlanState.PlanId}");
            sb.AppendLine($"Completed steps: {result.PlanState.CompletedStepsCount}/{result.PlanState.TotalSteps}");
        }

        if (!string.IsNullOrEmpty(result.ExecutionResult))
        {
            sb.AppendLine();
            sb.AppendLine("Execution result:");
            sb.AppendLine(result.ExecutionResult);
        }

        // 对齐 TS getPlan(): 输出从磁盘读取的 plan 文件内容（LLM 可能通过 FileWriteTool 修改了 plan）
        if (!string.IsNullOrEmpty(result.PlanFileContent))
        {
            sb.AppendLine();
            sb.AppendLine("Plan file content:");
            sb.AppendLine(result.PlanFileContent);
        }

        return McpResultBuilder.Success().WithText(sb.ToString()).Build();
    }

    /// <summary>
    /// 获取计划状态
    /// </summary>
    [McpTool(PlanToolNameConstants.GetPlanStatus, "Get current plan status", "plan")]
    public async Task<ToolResult> GetPlanStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var plan = await _planModeManager.GetPlanStatusAsync(cancellationToken).ConfigureAwait(false);

        if (plan == null)
        {
            return McpResultBuilder.Success().WithText("当前不在计划模式中").Build();
        }

        var response = FormatPlanStateResponse(plan, "计划状态");
        return McpResultBuilder.Success().WithText(response).Build();
    }

    /// <summary>
    /// 添加计划步骤
    /// </summary>
    [McpTool(PlanToolNameConstants.AddPlanStep, "Add a plan step", "plan")]
    public async Task<ToolResult> AddPlanStepAsync(
        [McpToolParameter("Step description")] string description,
        [McpToolParameter("Tool name (optional)", Required = false)] string? tool_name = null,
        [McpToolParameter("Tool parameters (optional)", Required = false)] Dictionary<string, JsonElement>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var command = new AddPlanStepCommand(description, tool_name, parameters);
        var validationError = ValidateCommand(command);
        if (validationError != null)
        {
            return McpResultBuilder.Error().WithText(validationError).Build();
        }

        var result = await _planModeManager.AddStepAsync(
            command.Description,
            command.ToolName,
            command.Parameters,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return McpResultBuilder.Error().WithText(result.ErrorMessage ?? "添加步骤失败").Build();
        }

        var response = new System.Text.StringBuilder();
        response.AppendLine("步骤已添加");
        response.AppendLine($"当前步骤数: {result.PlanState!.Steps.Count}");
        response.AppendLine();
        response.AppendLine(FormatStepSummary(result.PlanState.Steps.Last()));

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 批准计划步骤
    /// </summary>
    [McpTool(PlanToolNameConstants.ApprovePlanStep, "Approve a plan step", "plan")]
    public async Task<ToolResult> ApprovePlanStepAsync(
        [McpToolParameter("Step index (0-based)")] int step_index,
        CancellationToken cancellationToken = default)
    {
        var command = new ApprovePlanStepCommand(step_index);
        var validationError = ValidateCommand(command);
        if (validationError != null)
        {
            return McpResultBuilder.Error().WithText(validationError).Build();
        }

        var result = await _planModeManager.ApproveStepAsync(command.StepIndex, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return McpResultBuilder.Error().WithText(result.ErrorMessage ?? "批准步骤失败").Build();
        }

        var response = $"步骤 {command.StepIndex} 已批准";
        return McpResultBuilder.Success().WithText(response).Build();
    }

    /// <summary>
    /// 拒绝计划步骤
    /// </summary>
    [McpTool(PlanToolNameConstants.RejectPlanStep, "Reject a plan step", "plan")]
    public async Task<ToolResult> RejectPlanStepAsync(
        [McpToolParameter("Step index (0-based)")] int step_index,
        [McpToolParameter("Rejection reason (optional)", Required = false)] string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var command = new RejectPlanStepCommand(step_index, reason);
        var validationError = ValidateCommand(command);
        if (validationError != null)
        {
            return McpResultBuilder.Error().WithText(validationError).Build();
        }

        var result = await _planModeManager.RejectStepAsync(command.StepIndex, command.Reason, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return McpResultBuilder.Error().WithText(result.ErrorMessage ?? "拒绝步骤失败").Build();
        }

        var response = new System.Text.StringBuilder();
        response.AppendLine($"步骤 {command.StepIndex} 已拒绝");
        if (!string.IsNullOrEmpty(command.Reason))
        {
            response.AppendLine($"原因: {command.Reason}");
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 执行计划步骤
    /// </summary>
    [McpTool(PlanToolNameConstants.ExecutePlanSteps, "Execute approved plan steps", "plan")]
    public async Task<ToolResult> ExecutePlanStepsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _planModeManager.ExecuteApprovedStepsAsync(cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return McpResultBuilder.Error().WithText(result.ErrorMessage ?? "执行步骤失败").Build();
        }

        var response = new System.Text.StringBuilder();
        response.AppendLine("步骤执行完成");

        if (result.PlanState != null)
        {
            response.AppendLine($"完成进度: {result.PlanState.CompletedStepsCount}/{result.PlanState.TotalSteps}");
        }

        if (!string.IsNullOrEmpty(result.ExecutionResult))
        {
            response.AppendLine();
            response.AppendLine("执行详情:");
            response.AppendLine(result.ExecutionResult);
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 修改计划步骤
    /// </summary>
    [McpTool(PlanToolNameConstants.ModifyPlanStep, "Modify a plan step", "plan")]
    public async Task<ToolResult> ModifyPlanStepAsync(
        [McpToolParameter("Step index (0-based)")] int step_index,
        [McpToolParameter("New description (optional)", Required = false)] string? new_description = null,
        [McpToolParameter("New tool name (optional)", Required = false)] string? new_tool_name = null,
        [McpToolParameter("New parameters (optional)", Required = false)] Dictionary<string, JsonElement>? new_parameters = null,
        CancellationToken cancellationToken = default)
    {
        var command = new ModifyPlanStepCommand(step_index, new_description, new_tool_name, new_parameters);
        var validationError = ValidateCommand(command);
        if (validationError != null)
        {
            return McpResultBuilder.Error().WithText(validationError).Build();
        }

        var result = await _planModeManager.ModifyStepAsync(
            command.StepIndex,
            command.NewDescription,
            command.NewToolName,
            command.NewParameters,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return McpResultBuilder.Error().WithText(result.ErrorMessage ?? "修改步骤失败").Build();
        }

        var response = $"步骤 {command.StepIndex} 已修改";
        return McpResultBuilder.Success().WithText(response).Build();
    }

    /// <summary>
    /// 删除计划步骤
    /// </summary>
    [McpTool(PlanToolNameConstants.RemovePlanStep, "Remove a plan step", "plan")]
    public async Task<ToolResult> RemovePlanStepAsync(
        [McpToolParameter("Step index (0-based)")] int step_index,
        CancellationToken cancellationToken = default)
    {
        var command = new RemovePlanStepCommand(step_index);
        var validationError = ValidateCommand(command);
        if (validationError != null)
        {
            return McpResultBuilder.Error().WithText(validationError).Build();
        }

        var result = await _planModeManager.RemoveStepAsync(command.StepIndex, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return McpResultBuilder.Error().WithText(result.ErrorMessage ?? "删除步骤失败").Build();
        }

        var response = $"步骤 {command.StepIndex} 已删除";
        return McpResultBuilder.Success().WithText(response).Build();
    }

    /// <summary>
    /// 获取计划历史
    /// </summary>
    [McpTool(PlanToolNameConstants.GetPlanHistory, "Get plan history", "plan")]
    public async Task<ToolResult> GetPlanHistoryAsync(
        [McpToolParameter("Result count limit (optional, default 10)", Required = false)] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var command = new GetPlanHistoryCommand(limit);

        var history = await _planModeManager.GetPlanHistoryAsync(
            command.Limit ?? 10,
            cancellationToken).ConfigureAwait(false);

        var response = new System.Text.StringBuilder();
        response.AppendLine($"计划历史 (共 {history.Count} 个)");
        response.AppendLine();

        if (history.Count == 0)
        {
            response.AppendLine("暂无计划历史");
        }
        else
        {
            foreach (var plan in history)
            {
                response.AppendLine($"[{plan.PlanId}] {plan.Description ?? "无描述"}");
                response.AppendLine($"  状态: {plan.Status} | 步骤: {plan.CompletedStepsCount}/{plan.TotalSteps} | 时间: {plan.CreatedAt:yyyy-MM-dd HH:mm}");
                response.AppendLine();
            }
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    #region Private Methods

    private static string? ValidateCommand<TCommand>(TCommand command)
    {
        return command switch
        {
            AddPlanStepCommand cmd => string.IsNullOrWhiteSpace(cmd.Description) ? "description 不能为空" : null,
            ApprovePlanStepCommand cmd => cmd.StepIndex < 0 ? "step_index 必须是非负数" : null,
            RejectPlanStepCommand cmd => cmd.StepIndex < 0 ? "step_index 必须是非负数" : null,
            ModifyPlanStepCommand cmd => cmd.StepIndex < 0 ? "step_index 必须是非负数" : null,
            RemovePlanStepCommand cmd => cmd.StepIndex < 0 ? "step_index 必须是非负数" : null,
            GetPlanHistoryCommand cmd => cmd.Limit is < 1 or > WorkflowConstants.Limits.DefaultSearchResultLimit ? $"limit 必须在1-{WorkflowConstants.Limits.DefaultSearchResultLimit}之间" : null,
            _ => null
        };
    }

    private static string FormatPlanStateResponse(PlanState plan, string header)
    {
        var response = new System.Text.StringBuilder();
        response.AppendLine($"{header}");
        response.AppendLine($"计划ID: {plan.PlanId}");

        if (!string.IsNullOrEmpty(plan.Description))
        {
            response.AppendLine($"描述: {plan.Description}");
        }

        response.AppendLine($"状态: {plan.Status}");
        response.AppendLine($"进度: {plan.CompletedStepsCount}/{plan.TotalSteps} ({plan.GetProgressPercentage():F1}%)");
        response.AppendLine($"当前步骤: {plan.CurrentStepIndex}");
        response.AppendLine($"创建时间: {plan.CreatedAt:yyyy-MM-dd HH:mm:ss}");

        if (plan.Steps.Count > 0)
        {
            response.AppendLine();
            response.AppendLine("步骤列表:");
            foreach (var step in plan.Steps)
            {
                response.AppendLine(FormatStepSummary(step));
            }
        }

        return response.ToString();
    }

    private static string FormatStepSummary(PlanStep step)
    {
        var statusIcon = step.Status switch
        {
            PlanStepStatus.Pending => StatusSymbol.Circle.ToValue(),
            PlanStepStatus.Approved => StatusSymbol.Tick.ToValue(),
            PlanStepStatus.Rejected => StatusSymbol.Cross.ToValue(),
            PlanStepStatus.Executing => StatusSymbol.Play.ToValue(),
            PlanStepStatus.Completed => "✓",
            PlanStepStatus.Failed => "✗",
            PlanStepStatus.Skipped => StatusSymbol.Skip.ToValue(),
            _ => "?"
        };

        var toolInfo = !string.IsNullOrEmpty(step.ToolName) ? $" [{step.ToolName}]" : "";
        return $"  [{step.Index}] {statusIcon} {step.Description}{toolInfo}";
    }

    #endregion
}
