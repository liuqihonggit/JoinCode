namespace Core.Hooks.ToolPermission.Handlers;

public interface IPermissionPersistence
{
    Task PersistPermissionUpdatesAsync(List<PermissionUpdate> updates, CancellationToken cancellationToken = default);
}

public sealed record InteractivePermissionParams
{
    public required PermissionContext Context { get; init; }
    public required string Description { get; init; }
    public required PermissionAskDecision Result { get; init; }
    public bool AwaitAutomatedChecksBeforeDialog { get; init; }
    public IPermissionCallbacks? BridgeCallbacks { get; init; }
    public IPermissionPersistence? PermissionPersistence { get; init; }
    public required IPermissionHookExecutor HookExecutor { get; init; }
    public ICommandClassifier? Classifier { get; init; }
    public IAutoModeClassifier? AutoModeClassifier { get; init; }
}

[Register]
public sealed partial class InteractiveHandler
{
    [Inject] private readonly ILogger<InteractiveHandler>? _logger;
    [Inject] private readonly IClockService _clock;

    public InteractiveHandler(ILogger<InteractiveHandler>? logger = null, IClockService? clock = null)
    {
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
    }

    public void Handle(InteractivePermissionParams @params, Action<PermissionDecision> resolve)
    {
        var ctx = @params.Context;
        var resolveOnce = new ResolveOnce<PermissionDecision>(resolve);
        var userInteracted = false;
        var permissionPromptStartTimeMs = Environment.TickCount;
        var displayInput = @params.Result.UpdatedInput ?? ctx.Input;

        var queueItem = new PermissionQueueItem
        {
            ToolUseId = ctx.ToolUseId,
            ToolName = ctx.ToolName,
            Description = @params.Description,
            Input = displayInput,
            PermissionResult = CreatePermissionResult(@params.Result),
            PermissionPromptStartTime = _clock.GetUtcNowOffset(),
            ClassifierCheckInProgress = @params.Result.PendingClassifierCheck != null && !@params.AwaitAutomatedChecksBeforeDialog,
            OnUserInteraction = () =>
            {
                const int GRACE_PERIOD_MS = 200;
                if (Environment.TickCount - permissionPromptStartTimeMs < GRACE_PERIOD_MS)
                {
                    return Task.CompletedTask;
                }

                userInteracted = true;
                return Task.CompletedTask;
            },
            OnAbort = () =>
            {
                if (!resolveOnce.Claim()) return Task.CompletedTask;

                ctx.LogDecision(
                    new RejectDecisionArgs
                    {
                        RejectionSource = new PermissionRejectionSource
                        {
                            Type = PermissionDecisionSourceType.UserAbort
                        }
                    },
                    permissionPromptStartTimeMs);

                resolveOnce.Resolve(ctx.CancelAndAbort());
                return Task.CompletedTask;
            },
            OnAllow = async (updatedInput, permissionUpdates, feedback) =>
            {
                if (!resolveOnce.Claim()) return;

                var decision = await ctx.HandleUserAllow(
                    updatedInput ?? displayInput,
                    permissionUpdates ?? new List<PermissionUpdate>(),
                    feedback,
                    permissionPromptStartTimeMs).ConfigureAwait(false);

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
                    },
                    permissionPromptStartTimeMs);

                resolveOnce.Resolve(ctx.CancelAndAbort(feedback));
            },
            RecheckPermission = async () =>
            {
                return null;
            }
        };

        ctx.PushToQueue(queueItem);

        SetupBridgeCallbacks(@params, resolveOnce, permissionPromptStartTimeMs, displayInput);

        if (!@params.AwaitAutomatedChecksBeforeDialog)
        {
            _ = ExecuteHooksAsync(@params, resolveOnce, permissionPromptStartTimeMs, userInteracted).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }

        if (@params.Result.PendingClassifierCheck != null && !@params.AwaitAutomatedChecksBeforeDialog)
        {
            _ = ExecuteClassifierAsync(@params, resolveOnce, permissionPromptStartTimeMs, userInteracted).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
    }

    private void SetupBridgeCallbacks(
        InteractivePermissionParams @params,
        ResolveOnce<PermissionDecision> resolveOnce,
        int permissionPromptStartTimeMs,
        Dictionary<string, JsonElement> displayInput)
    {
        if (@params.BridgeCallbacks == null) return;

        var ctx = @params.Context;
        var bridgeRequestId = Guid.NewGuid().ToString();

        // PermissionUpdate → PermissionCallbackUpdate 映射
        var bridgeSuggestions = @params.Result.Suggestions?.ConvertAll(s =>
            new PermissionCallbackUpdate
            {
                ToolName = s.ToolName,
                PermissionMode = s.Action,
            });

        @params.BridgeCallbacks.SendRequest(
            bridgeRequestId,
            ctx.ToolName,
            displayInput,
            ctx.ToolUseId,
            @params.Description,
            bridgeSuggestions,
            @params.Result.BlockedPath);

        var unsubscribe = @params.BridgeCallbacks.OnResponse(bridgeRequestId, async response =>
        {
            if (!resolveOnce.Claim()) return;

            ctx.RemoveFromQueue();

            if (response.Behavior == PermissionBehaviorConstants.Allow)
            {
                // PermissionCallbackUpdate → PermissionUpdate 映射
                var permissionUpdates = response.UpdatedPermissions?.ConvertAll(u =>
                    new PermissionUpdate
                    {
                        ToolName = u.ToolName ?? string.Empty,
                        Action = u.PermissionMode ?? string.Empty,
                        Destination = string.Empty,
                    });

                if (permissionUpdates?.Count > 0 && @params.PermissionPersistence != null)
                {
                    try
                    {
                        await @params.PermissionPersistence.PersistPermissionUpdatesAsync(
                            permissionUpdates, ctx.CancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "持久化权限更新失败: Tool={ToolName}", ctx.ToolName);
                    }
                }

                ctx.LogDecision(
                    new AcceptDecisionArgs
                    {
                        ApprovalSource = new PermissionApprovalSource
                        {
                            Type = PermissionDecisionSourceType.User,
                            Permanent = permissionUpdates?.Count > 0
                        }
                    },
                    permissionPromptStartTimeMs);

                resolveOnce.Resolve(ctx.BuildAllow(response.UpdatedInput ?? displayInput));
            }
            else
            {
                ctx.LogDecision(
                    new RejectDecisionArgs
                    {
                        RejectionSource = new PermissionRejectionSource
                        {
                            Type = PermissionDecisionSourceType.UserReject,
                            HasFeedback = !string.IsNullOrEmpty(response.Message)
                        }
                    },
                    permissionPromptStartTimeMs);

                resolveOnce.Resolve(ctx.CancelAndAbort(response.Message));
            }
        });

        ctx.CancellationToken.Register(() =>
        {
            @params.BridgeCallbacks?.CancelRequest(bridgeRequestId);
            unsubscribe?.Invoke();
        });
    }

    private async Task ExecuteHooksAsync(
        InteractivePermissionParams @params,
        ResolveOnce<PermissionDecision> resolveOnce,
        int permissionPromptStartTimeMs,
        bool userInteracted)
    {
        var ctx = @params.Context;

        if (resolveOnce.IsResolved() || userInteracted) return;

        try
        {
            await foreach (var hookResult in @params.HookExecutor.ExecuteHooksAsync(
                ctx.ToolName,
                ctx.ToolUseId,
                ctx.Input,
                null,
                @params.Result.Suggestions,
                ctx.CancellationToken))
            {
                if (resolveOnce.IsResolved() || userInteracted) return;

                if (hookResult.PermissionRequestResult != null)
                {
                    if (!resolveOnce.Claim()) return;

                    ctx.RemoveFromQueue();

                    var result = hookResult.PermissionRequestResult;
                    if (result.Behavior == PermissionBehavior.Allow)
                    {
                        var finalInput = result.UpdatedInput ?? @params.Result.UpdatedInput ?? ctx.Input;
                        var decision = await ctx.HandleHookAllow(
                            finalInput,
                            result.UpdatedPermissions ?? new List<PermissionUpdate>(),
                            permissionPromptStartTimeMs).ConfigureAwait(false);
                        resolveOnce.Resolve(decision);
                    }
                    else
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
                            },
                            permissionPromptStartTimeMs);

                        resolveOnce.Resolve(ctx.BuildDeny(
                            result.Message ?? "Permission denied by hook",
                            new HookDecisionReason
                            {
                                HookName = hookResult.HookName,
                                Reason = result.Message
                            }));
                    }

                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "异步 Hook 执行失败: Tool={ToolName}", ctx.ToolName);
        }
    }

    private async Task ExecuteClassifierAsync(
        InteractivePermissionParams @params,
        ResolveOnce<PermissionDecision> resolveOnce,
        int permissionPromptStartTimeMs,
        bool userInteracted)
    {
        var ctx = @params.Context;

        if (@params.Result.PendingClassifierCheck == null) return;
        if (resolveOnce.IsResolved() || userInteracted) return;

        try
        {
            var command = ExtractCommand(ctx.Input);
            if (string.IsNullOrEmpty(command)) return;

            var workingDir = Environment.CurrentDirectory;
            var autoApproved = false;
            string? matchedRule = null;

            if (@params.Classifier != null)
            {
                var classification = @params.Classifier.Classify(
                    ShellCommand.Parse(command),
                    workingDir);

                if (classification.Category == CommandCategory.ReadOnly)
                {
                    autoApproved = true;
                    matchedRule = "read-only command";
                }
            }

            if (!autoApproved && @params.AutoModeClassifier != null)
            {
                var request = new ClassificationRequest
                {
                    ToolName = ctx.ToolName,
                    Parameters = ctx.Input,
                    OperationType = OperationType.Execute
                };

                var result = await @params.AutoModeClassifier.ClassifyAsync(request, ctx.CancellationToken).ConfigureAwait(false);

                if (result.Action == SecurityAction.AutoApprove && result.Confidence >= 0.85)
                {
                    autoApproved = true;
                    matchedRule = result.Reason;
                }
            }

            if (resolveOnce.IsResolved() || userInteracted) return;

            if (autoApproved)
            {
                if (!resolveOnce.Claim()) return;

                ctx.UpdateQueueItem(item =>
                {
                    item.ClassifierCheckInProgress = false;
                    item.ClassifierAutoApproved = true;
                    item.ClassifierMatchedRule = matchedRule;
                });

                var decision = await ctx.HandleUserAllow(
                    @params.Result.UpdatedInput ?? ctx.Input,
                    new List<PermissionUpdate>(),
                    permissionPromptStartTimeMs: permissionPromptStartTimeMs,
                    decisionReason: new ClassifierPermissionDecisionReason
                    {
                        Classifier = "BashClassifier",
                        Reason = matchedRule ?? "auto-approved"
                    }).ConfigureAwait(false);

                resolveOnce.Resolve(decision);
            }
            else
            {
                ctx.UpdateQueueItem(item =>
                {
                    item.ClassifierCheckInProgress = false;
                });
            }
        }
        catch (OperationCanceledException)
        {
            ctx.UpdateQueueItem(item =>
            {
                item.ClassifierCheckInProgress = false;
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "异步分类器检查失败: Tool={ToolName}", ctx.ToolName);

            ctx.UpdateQueueItem(item =>
            {
                item.ClassifierCheckInProgress = false;
            });
        }
    }

    private static string? ExtractCommand(Dictionary<string, JsonElement> input)
    {
        if (input.TryGetValue("command", out var cmd) && cmd.ValueKind == JsonValueKind.String)
            return cmd.GetString();
        return null;
    }

    private static PermissionResult CreatePermissionResult(PermissionAskDecision result)
    {
        return result.Behavior switch
        {
            PermissionBehavior.Allow => PermissionResult.Granted(),
            PermissionBehavior.Deny => PermissionResult.Denied(result.Message ?? "权限被拒绝"),
            _ => PermissionResult.PendingConfirmation(result.Message ?? "需要用户确认")
        };
    }
}
