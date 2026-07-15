namespace Core.Hooks.ToolPermission.Handlers;

public sealed record SwarmWorkerPermissionParams
{
    public required PermissionContext Context { get; init; }
    public required string Description { get; init; }
    public object? PendingClassifierCheck { get; init; }
    public Dictionary<string, JsonElement>? UpdatedInput { get; init; }
    public List<PermissionUpdate>? Suggestions { get; init; }
    public required bool IsSwarmWorker { get; init; }
    public ISwarmPermissionCallbacks? SwarmCallbacks { get; init; }
    public ICommandClassifier? Classifier { get; init; }
}

[Register]
public sealed partial class SwarmWorkerHandler
{
    [Inject] private readonly ILogger<SwarmWorkerHandler>? _logger;
    private readonly ISwarmPermissionCallbacks? _injectedCallbacks;

    private static readonly TimeSpan LeaderResponseTimeout = TimeSpan.FromSeconds(30);

    public SwarmWorkerHandler(
        ILogger<SwarmWorkerHandler>? logger = null,
        ISwarmPermissionCallbacks? swarmCallbacks = null)
    {
        _logger = logger;
        _injectedCallbacks = swarmCallbacks;
    }

    public async Task<PermissionDecision?> HandleAsync(SwarmWorkerPermissionParams @params)
    {
        if (!@params.IsSwarmWorker)
        {
            return null;
        }

        var ctx = @params.Context;

        var classifierDecision = await TryClassifierAsync(@params).ConfigureAwait(false);
        if (classifierDecision != null)
        {
            _logger?.LogDebug("Swarm 权限由分类器解决: Tool={ToolName}", ctx.ToolName);
            return classifierDecision;
        }

        var effectiveCallbacks = @params.SwarmCallbacks ?? _injectedCallbacks;

        if (effectiveCallbacks == null)
        {
            _logger?.LogWarning("Swarm 回调未配置，回退到本地处理: Tool={ToolName}", ctx.ToolName);
            return null;
        }

        var effectiveParams = @params.SwarmCallbacks == null
            ? @params with { SwarmCallbacks = effectiveCallbacks }
            : @params;

        try
        {
            return await ForwardToLeaderAsync(effectiveParams).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Swarm 权限转发失败，回退到本地处理: Tool={ToolName}", ctx.ToolName);
            return null;
        }
    }

    private async Task<PermissionDecision?> TryClassifierAsync(SwarmWorkerPermissionParams @params)
    {
        if (@params.PendingClassifierCheck == null)
        {
            return null;
        }

        if (@params.Context.ToolName != "bash" && @params.Context.ToolName != "shell")
        {
            return null;
        }

        var command = ExtractCommand(@params.Context.Input);
        if (string.IsNullOrEmpty(command))
        {
            return null;
        }

        var classifier = @params.Classifier;
        if (classifier == null)
        {
            return null;
        }

        var workingDir = Environment.CurrentDirectory;
        var classification = classifier.Classify(
            ShellCommand.Parse(command),
            workingDir);

        if (classification.Category == CommandCategory.ReadOnly)
        {
            _logger?.LogDebug("Swarm 分类器自动批准只读命令: {Command}", command);
            return @params.Context.BuildAllow(@params.UpdatedInput ?? @params.Context.Input);
        }

        if (classification.Category == CommandCategory.Destructive ||
            classification.Category == CommandCategory.PathViolation)
        {
            _logger?.LogDebug("Swarm 分类器拒绝危险命令: {Command}, Category={Category}", command, classification.Category);
            return @params.Context.BuildDeny(
                $"命令被分类器拒绝: {classification.Category}",
                new ClassifierPermissionDecisionReason
                {
                    Classifier = "CommandClassifier",
                    Reason = $"{classification.Category}: {classification.Details}"
                });
        }

        return null;
    }

    private static string? ExtractCommand(Dictionary<string, JsonElement> input)
    {
        if (input.TryGetValue("command", out var cmd) && cmd.ValueKind == JsonValueKind.String)
            return cmd.GetString();
        return null;
    }

    private async Task<PermissionDecision> ForwardToLeaderAsync(SwarmWorkerPermissionParams @params)
    {
        var ctx = @params.Context;
        var callbacks = @params.SwarmCallbacks ?? throw new InvalidOperationException("SwarmCallbacks is not available.");

        var tcs = new TaskCompletionSource<PermissionDecision>();
        var resolveOnce = new ResolveOnce<PermissionDecision>(decision => tcs.TrySetResult(decision));

        var request = callbacks.CreatePermissionRequest(
            ctx.ToolName,
            ctx.ToolUseId,
            ctx.Input,
            @params.Description,
            @params.Suggestions);

        callbacks.RegisterPermissionCallback(new SwarmPermissionCallback
        {
            RequestId = request.Id,
            ToolUseId = ctx.ToolUseId,
            OnAllow = async (allowedInput, permissionUpdates, feedback) =>
            {
                if (!resolveOnce.Claim()) return;

                var finalInput = allowedInput != null && allowedInput.Count > 0
                    ? allowedInput
                    : ctx.Input;

                var decision = await ctx.HandleUserAllowAsync(
                    finalInput,
                    permissionUpdates ?? new List<PermissionUpdate>(),
                    feedback).ConfigureAwait(false);

                resolveOnce.Resolve(decision);
            },
            OnReject = async (feedback) =>
            {
                if (!resolveOnce.Claim()) return;

                ctx.LogDecision(
                    new RejectDecisionArgs
                    {
                        RejectionSource = new PermissionRejectionSource
                        {
                            Type = PermissionDecisionSourceType.UserReject,
                            HasFeedback = !string.IsNullOrEmpty(feedback)
                        }
                    });

                resolveOnce.Resolve(ctx.CancelAndAbort(feedback));
            }
        });

        await callbacks.SendPermissionRequestViaMailboxAsync(request).ConfigureAwait(false);

        _logger?.LogInformation("等待 Leader 批准: Tool={ToolName}, RequestId={RequestId}",
            ctx.ToolName, request.Id);

        using (ctx.CancellationToken.Register(() =>
        {
            if (resolveOnce.Claim())
            {
                ctx.LogCancelled();
                resolveOnce.Resolve(ctx.CancelAndAbort());
            }
        }))
        {
            using var timeoutCts = new CancellationTokenSource(LeaderResponseTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                ctx.CancellationToken, timeoutCts.Token);

            linkedCts.Token.Register(() =>
            {
                if (resolveOnce.Claim())
                {
                    _logger?.LogWarning("等待 Leader 响应超时: Tool={ToolName}, RequestId={RequestId}",
                        ctx.ToolName, request.Id);
                    resolveOnce.Resolve(ctx.CancelAndAbort("Leader response timeout"));
                }
            });

            return await tcs.Task.ConfigureAwait(false);
        }
    }
}
