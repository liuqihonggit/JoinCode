namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Workflows, Description = "管理工作流", Usage = "/workflows [list|run|status] [name]", Category = ChatCommandCategory.System)]
public sealed class WorkflowsCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Workflows;
    public string Description => "管理工作流";
    public string Usage => "/workflows [list|run|status] [name]";
    public string[] Aliases => [];
    public string ArgumentHint => "[list|run|status]";
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetNormalizedArgs(context);

        if (string.IsNullOrEmpty(args) || args.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            return ListWorkflows(context.Services.PluginManager);
        }

        if (args.StartsWith("run", StringComparison.OrdinalIgnoreCase))
        {
            var name = args["run".Length..].Trim();
            if (string.IsNullOrEmpty(name))
            {
                TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsRunUsage));
                return ChatCommandResult.Continue();
            }
            return await RunWorkflowAsync(context, name);
        }

        if (args.StartsWith("status", StringComparison.OrdinalIgnoreCase))
        {
            var workflowId = args["status".Length..].Trim();
            if (string.IsNullOrEmpty(workflowId))
            {
                TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsStatusUsage));
                return ChatCommandResult.Continue();
            }
            return await GetWorkflowStatusAsync(context, workflowId);
        }

        TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsUnknownAction, args));
        TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsSupportedActions));
        return ChatCommandResult.Continue();
    }

    private static ChatCommandResult ListWorkflows(IPluginManager? pluginManager)
    {
        TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsListHeader));
        TerminalHelper.NewLine();

        if (pluginManager is not null)
        {
            var workflowPlugins = pluginManager.LoadedWorkflowPluginNames;

            if (workflowPlugins.Count > 0)
            {
                foreach (var name in workflowPlugins)
                {
                    TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsLoadedWorkflow, name));
                }
            }
            else
            {
                TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsNoLoadedWorkflows));
            }
        }
        else
        {
            TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsPluginManagerNotInitialized));
        }

        TerminalHelper.NewLine();
        TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsRunHint));
        return ChatCommandResult.Continue();
    }

    private static async Task<ChatCommandResult> RunWorkflowAsync(ChatCommandContext context, string name)
    {
        var executor = context.Services.WorkflowTaskExecutor;
        if (executor is null)
        {
            TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsExecutorNotInitialized));
            return ChatCommandResult.Continue();
        }

        var pluginManager = context.Services.PluginManager;
        if (pluginManager is null)
        {
            TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsPluginManagerNull));
            return ChatCommandResult.Continue();
        }

        var pluginHost = pluginManager.GetWorkflowPlugin(name);
        if (pluginHost is null)
        {
            TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsPluginNotFound, name));
            TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsListHint));
            return ChatCommandResult.Continue();
        }

        var definition = pluginHost.GetService<WorkflowDefinition>();
        if (definition is null)
        {
            TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsPluginNoDefinition, name));
            TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsPluginRegisterDefinition));
            return ChatCommandResult.Continue();
        }

        TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsRunning, name, definition.Steps.Count));
        TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsExecutionMode, definition.ExecutionMode));
        TerminalHelper.NewLine();

        try
        {
            var result = await executor.ExecuteWorkflowAsync(definition, context.CancellationToken).ConfigureAwait(false);

            TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsExecutionComplete, result.Status));
            TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsWorkflowId, result.WorkflowId));
            TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsDuration, result.Duration.TotalMilliseconds));

            if (result.ErrorMessage is not null)
            {
                TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsError, result.ErrorMessage));
            }

            var completedSteps = result.StepResults.Count;
            TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsStepResults, completedSteps, definition.Steps.Count));

            foreach (var kvp in result.StepResults)
            {
                var value = kvp.Value.ValueKind == JsonValueKind.Null ? L.T(StringKey.HostWorkflowsNoResult) : kvp.Value.GetRawText() ?? string.Empty;
                if (value.Length > 100)
                {
                    value = string.Concat(value.AsSpan(0, 97), "...");
                }
                TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsStepResult, kvp.Key, value));
            }
        }
        catch (OperationCanceledException)
        {
            TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsExecutionCancelled));
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("工作流执行", ex);
        }

        return ChatCommandResult.Continue();
    }

    private static async Task<ChatCommandResult> GetWorkflowStatusAsync(ChatCommandContext context, string workflowId)
    {
        var executor = context.Services.WorkflowTaskExecutor;
        if (executor is null)
        {
            TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsExecutorNotInitialized));
            return ChatCommandResult.Continue();
        }

        try
        {
            var status = await executor.GetWorkflowStatusAsync(workflowId, context.CancellationToken).ConfigureAwait(false);

            TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsStatusHeader, workflowId));
            TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsStateLabel, status.State));
            TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsProgress, status.CompletedSteps, status.TotalSteps));

            if (status.StepStatuses.Count > 0)
            {
                TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsStepDetails));
                foreach (var kvp in status.StepStatuses)
                {
                    var stepStatus = kvp.Value;
                    var duration = stepStatus.Duration.HasValue ? $" ({stepStatus.Duration.Value.TotalMilliseconds:F0}ms)" : "";
                    var error = stepStatus.Error is not null ? $" - 错误: {stepStatus.Error}" : "";
                    TerminalHelper.WriteLine(L.T(StringKey.HostWorkflowsStepStatusDetail, kvp.Key, stepStatus.State, duration, error));
                }
            }
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("查询工作流状态", ex);
        }

        return ChatCommandResult.Continue();
    }
}
