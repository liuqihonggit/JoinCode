
namespace Core.Hooks.ToolPermission.Handlers;

/// <summary>
/// 协调器权限参数
/// </summary>
public sealed record CoordinatorPermissionParams
{
    /// <summary>
    /// 权限上下文
    /// </summary>
    public required PermissionContext Context { get; init; }

    /// <summary>
    /// 待处理的分类器检查
    /// </summary>
    public object? PendingClassifierCheck { get; init; }

    /// <summary>
    /// 更新后的输入
    /// </summary>
    public Dictionary<string, JsonElement>? UpdatedInput { get; init; }

    /// <summary>
    /// 权限建议
    /// </summary>
    public List<PermissionUpdate>? Suggestions { get; init; }

    /// <summary>
    /// 权限模式
    /// </summary>
    public string? PermissionMode { get; init; }

    /// <summary>
    /// Hook 执行器
    /// </summary>
    public required IPermissionHookExecutor HookExecutor { get; init; }

    /// <summary>
    /// 命令分类器
    /// </summary>
    public ICommandClassifier? Classifier { get; init; }
}

/// <summary>
/// 协调器权限处理器
/// 
/// 处理协调器工作流的权限流程：
/// 1. 首先尝试权限 hooks（快速、本地）
/// 2. 然后尝试分类器（慢速、推理 - 仅 bash）
/// 3. 如果都未解决，返回 null 让调用者回退到交互式对话框
/// </summary>
[Register]
public sealed partial class CoordinatorHandler
{
    [Inject] private readonly ILogger<CoordinatorHandler>? _logger;

    public CoordinatorHandler(ILogger<CoordinatorHandler>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 处理协调器权限
    /// </summary>
    /// <param name="params">参数</param>
    /// <returns>权限决策，如果未解决则返回 null</returns>
    public async Task<PermissionDecision?> HandleAsync(CoordinatorPermissionParams @params)
    {
        var ctx = @params.Context;

        try
        {
            // 1. 首先尝试权限 hooks（快速、本地）
            var hookDecision = await TryRunHooksAsync(@params).ConfigureAwait(false);
            if (hookDecision != null)
            {
                _logger?.LogDebug("协调器权限由 Hook 解决: Tool={ToolName}", ctx.ToolName);
                return hookDecision;
            }

            // 2. 尝试分类器（慢速、推理 -- 仅 bash）
            var classifierDecision = await TryClassifierAsync(@params).ConfigureAwait(false);
            if (classifierDecision != null)
            {
                _logger?.LogDebug("协调器权限由分类器解决: Tool={ToolName}", ctx.ToolName);
                return classifierDecision;
            }
        }
        catch (Exception ex)
        {
            // 如果自动检查意外失败，回退到显示对话框
            // 让用户可以手动决定
            _logger?.LogError(ex, "自动权限检查失败，回退到对话框: Tool={ToolName}", ctx.ToolName);
        }

        // 3. 都未解决（或检查失败）-- 回退到对话框
        _logger?.LogDebug("协调器权限未解决，回退到对话框: Tool={ToolName}", ctx.ToolName);
        return null;
    }

    /// <summary>
    /// 尝试运行 hooks
    /// </summary>
    private async Task<PermissionDecision?> TryRunHooksAsync(CoordinatorPermissionParams @params)
    {
        var ctx = @params.Context;

        await foreach (var hookResult in @params.HookExecutor.ExecuteHooksAsync(
            ctx.ToolName,
            ctx.ToolUseId,
            ctx.Input,
            @params.PermissionMode,
            @params.Suggestions,
            ctx.CancellationToken))
        {
            if (hookResult.PermissionRequestResult != null)
            {
                var result = hookResult.PermissionRequestResult;

                if (result.Behavior == PermissionBehavior.Allow)
                {
                    var finalInput = result.UpdatedInput ?? @params.UpdatedInput ?? ctx.Input;
                    return await ctx.HandleHookAllow(
                        finalInput,
                        result.UpdatedPermissions ?? new List<PermissionUpdate>()).ConfigureAwait(false);
                }
                else if (result.Behavior == PermissionBehavior.Deny)
                {
                    ctx.LogDecision(
                        new RejectDecisionArgs
                        {
                            RejectionSource = new PermissionRejectionSource
                            {
                                Type = PermissionDecisionSourceType.Hook,
                                HookName = hookResult.HookName,
                                Reason = result.Message
                            }
                        });

                    return ctx.BuildDeny(
                        result.Message ?? "Permission denied by hook",
                        new HookDecisionReason
                        {
                            HookName = hookResult.HookName,
                            Reason = result.Message
                        });
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 尝试分类器
    /// </summary>
    private Task<PermissionDecision?> TryClassifierAsync(CoordinatorPermissionParams @params)
    {
        if (@params.PendingClassifierCheck == null)
        {
            return Task.FromResult<PermissionDecision?>(null);
        }

        if (@params.Context.ToolName != "bash" && @params.Context.ToolName != "shell")
        {
            return Task.FromResult<PermissionDecision?>(null);
        }

        var command = ExtractCommand(@params.Context.Input);
        if (string.IsNullOrEmpty(command))
        {
            return Task.FromResult<PermissionDecision?>(null);
        }

        var classifier = @params.Classifier;
        if (classifier == null)
        {
            return Task.FromResult<PermissionDecision?>(null);
        }

        var workingDir = Environment.CurrentDirectory;
        var classification = classifier.Classify(
            ShellCommand.Parse(command),
            workingDir);

        if (classification.Category == CommandCategory.ReadOnly)
        {
            _logger?.LogDebug("协调器分类器自动批准只读命令: {Command}", command);
            var finalInput = @params.UpdatedInput ?? @params.Context.Input;
            return Task.FromResult<PermissionDecision?>(@params.Context.BuildAllow(finalInput));
        }

        if (classification.Category == CommandCategory.Destructive ||
            classification.Category == CommandCategory.PathViolation)
        {
            _logger?.LogDebug("协调器分类器拒绝危险命令: {Command}, Category={Category}", command, classification.Category);
            return Task.FromResult<PermissionDecision?>(
                @params.Context.BuildDeny(
                    $"命令被分类器拒绝: {classification.Category}",
                    new ClassifierPermissionDecisionReason
                    {
                        Classifier = "CommandClassifier",
                        Reason = $"{classification.Category}: {classification.Details}"
                    }));
        }

        return Task.FromResult<PermissionDecision?>(null);
    }

    private static string? ExtractCommand(Dictionary<string, JsonElement> input)
    {
        if (input.TryGetValue("command", out var cmd) && cmd.ValueKind == JsonValueKind.String)
            return cmd.GetString();
        return null;
    }
}
