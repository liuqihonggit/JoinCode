namespace Core.Hooks.ToolPermission;

public interface IPermissionQueueOperations
{
    void Push(PermissionQueueItem item);
    void Remove(string toolUseId);
    void Update(string toolUseId, Action<PermissionQueueItem> patch);
}

public sealed class PermissionQueueItem
{
    public required string ToolUseId { get; init; }
    public required string ToolName { get; init; }
    public required string Description { get; init; }
    public Dictionary<string, JsonElement>? Input { get; init; }
    public PermissionResult? PermissionResult { get; init; }
    public DateTimeOffset PermissionPromptStartTime { get; init; }
    public bool ClassifierCheckInProgress { get; set; }
    public bool ClassifierAutoApproved { get; set; }
    public string? ClassifierMatchedRule { get; set; }

    public Func<Task>? OnAbort { get; set; }
    public Func<Dictionary<string, JsonElement>?, List<PermissionUpdate>?, string?, Task>? OnAllow { get; set; }
    public Func<string?, Task>? OnReject { get; set; }
    public Func<Task>? OnUserInteraction { get; set; }
    public Func<Task>? OnDismissCheckmark { get; set; }
    public Func<Task<PermissionResult?>>? RecheckPermission { get; set; }
}

public abstract record PermissionDecision
{
    public abstract PermissionBehavior Behavior { get; }
}

public sealed record PermissionAllowDecision : PermissionDecision
{
    public override PermissionBehavior Behavior => PermissionBehavior.Allow;
    public required Dictionary<string, JsonElement> UpdatedInput { get; init; }
    public bool UserModified { get; init; }
    public PermissionDecisionReason? DecisionReason { get; init; }
    public string? AcceptFeedback { get; init; }
}

public sealed record PermissionDenyDecision : PermissionDecision
{
    public override PermissionBehavior Behavior => PermissionBehavior.Deny;
    public required string Message { get; init; }
    public required PermissionDecisionReason DecisionReason { get; init; }
}

public sealed record PermissionAskDecision : PermissionDecision
{
    public override PermissionBehavior Behavior => PermissionBehavior.Ask;
    public string? Message { get; init; }
    public List<PermissionUpdate>? Suggestions { get; init; }
    public string? BlockedPath { get; init; }
    public object? PendingClassifierCheck { get; init; }
    public Dictionary<string, JsonElement>? UpdatedInput { get; init; }
}

public sealed class ResolveOnce<T>
{
    private bool _claimed;
    private bool _delivered;
    private readonly Action<T> _resolve;

    public ResolveOnce(Action<T> resolve)
    {
        _resolve = resolve;
    }

    public void Resolve(T value)
    {
        if (_delivered) return;
        _delivered = true;
        _claimed = true;
        _resolve(value);
    }

    public bool IsResolved() => _claimed;

    public bool Claim()
    {
        if (Interlocked.CompareExchange(ref _claimed, true, false))
        {
            return false;
        }
        return true;
    }
}

/// <summary>
/// 权限工具调用标识 — 聚合工具名称、参数、消息ID和工具使用ID
/// </summary>
public sealed record PermissionToolCall
{
    public required string ToolName { get; init; }
    public required Dictionary<string, JsonElement> Input { get; init; }
    public required string MessageId { get; init; }
    public required string ToolUseId { get; init; }
}

public sealed class PermissionContext
{
    private readonly IPermissionLogger _logger;
    private readonly IPermissionQueueOperations? _queueOps;

    public PermissionToolCall ToolCall { get; }
    public string ToolName => ToolCall.ToolName;
    public Dictionary<string, JsonElement> Input => ToolCall.Input;
    public string MessageId => ToolCall.MessageId;
    public string ToolUseId => ToolCall.ToolUseId;
    public CancellationToken CancellationToken { get; }

    public PermissionContext(
        PermissionToolCall toolCall,
        IPermissionLogger logger,
        IPermissionQueueOperations? queueOps = null,
        CancellationToken cancellationToken = default)
    {
        ToolCall = toolCall ?? throw new ArgumentNullException(nameof(toolCall));
        _logger = logger;
        _queueOps = queueOps;
        CancellationToken = cancellationToken;
    }

    /// <summary>
    /// 向后兼容的构造函数 — 逐步迁移到 PermissionToolCall 版本
    /// </summary>
    public PermissionContext(
        string toolName,
        Dictionary<string, JsonElement> input,
        string messageId,
        string toolUseId,
        IPermissionLogger logger,
        IPermissionQueueOperations? queueOps = null,
        CancellationToken cancellationToken = default)
        : this(new PermissionToolCall { ToolName = toolName, Input = input, MessageId = messageId, ToolUseId = toolUseId }, logger, queueOps, cancellationToken)
    {
    }

    public void LogDecision(PermissionDecisionArgs args, int? permissionPromptStartTimeMs = null)
    {
        var waitMs = permissionPromptStartTimeMs.HasValue
            ? (int?)(Environment.TickCount - permissionPromptStartTimeMs.Value)
            : null;

        var context = new PermissionLogContext
        {
            ToolName = ToolName,
            Input = Input,
            MessageId = MessageId,
            ToolUseId = ToolUseId,
            WaitingForUserPermissionMs = waitMs
        };

        _logger.LogPermissionDecision(context, args);
    }

    public void LogCancelled()
    {
        var context = new PermissionLogContext
        {
            ToolName = ToolName,
            Input = Input,
            MessageId = MessageId,
            ToolUseId = ToolUseId
        };

        _logger.LogPermissionCancelled(context);
    }

    public bool ResolveIfAborted(Action<PermissionDecision> resolve)
    {
        if (!CancellationToken.IsCancellationRequested) return false;

        LogCancelled();
        resolve(CancelAndAbort());
        return true;
    }

    public PermissionDecision CancelAndAbort(string? feedback = null)
    {
        var baseMessage = string.IsNullOrEmpty(feedback)
            ? "Permission request cancelled"
            : $"Permission denied: {feedback}";

        return new PermissionDenyDecision
        {
            Message = baseMessage,
            DecisionReason = new HookDecisionReason
            {
                HookName = "CancelAndAbort",
                Reason = feedback
            }
        };
    }

    public PermissionDecision BuildAllow(
        Dictionary<string, JsonElement> updatedInput,
        PermissionDecisionReason? decisionReason = null,
        bool userModified = false,
        string? acceptFeedback = null)
    {
        return new PermissionAllowDecision
        {
            UpdatedInput = updatedInput,
            DecisionReason = decisionReason,
            UserModified = userModified,
            AcceptFeedback = acceptFeedback
        };
    }

    public PermissionDecision BuildDeny(string message, PermissionDecisionReason decisionReason)
    {
        return new PermissionDenyDecision
        {
            Message = message,
            DecisionReason = decisionReason
        };
    }

    public async Task<PermissionAllowDecision> HandleUserAllowAsync(
        Dictionary<string, JsonElement> updatedInput,
        List<PermissionUpdate> permissionUpdates,
        string? feedback = null,
        int? permissionPromptStartTimeMs = null,
        PermissionDecisionReason? decisionReason = null)
    {
        LogDecision(
            new AcceptDecisionArgs
            {
                ApprovalSource = new PermissionApprovalSource
                {
                    Type = PermissionDecisionSourceType.User,
                    Permanent = permissionUpdates.Count > 0
                }
            },
            permissionPromptStartTimeMs);

        var userModified = !DictionaryEquals(Input, updatedInput);
        var trimmedFeedback = feedback?.Trim();

        return new PermissionAllowDecision
        {
            UpdatedInput = updatedInput,
            UserModified = userModified,
            DecisionReason = decisionReason,
            AcceptFeedback = trimmedFeedback
        };
    }

    public async Task<PermissionAllowDecision> HandleHookAllowAsync(
        Dictionary<string, JsonElement> finalInput,
        List<PermissionUpdate> permissionUpdates,
        int? permissionPromptStartTimeMs = null)
    {
        LogDecision(
            new AcceptDecisionArgs
            {
                ApprovalSource = new PermissionApprovalSource
                {
                    Type = PermissionDecisionSourceType.Hook,
                    Permanent = permissionUpdates.Count > 0,
                    HookName = "PermissionRequest"
                }
            },
            permissionPromptStartTimeMs);

        return new PermissionAllowDecision
        {
            UpdatedInput = finalInput,
            DecisionReason = new HookDecisionReason
            {
                HookName = "PermissionRequest"
            }
        };
    }

    public void PushToQueue(PermissionQueueItem item) => _queueOps?.Push(item);
    public void RemoveFromQueue() => _queueOps?.Remove(ToolUseId);
    public void UpdateQueueItem(Action<PermissionQueueItem> patch) => _queueOps?.Update(ToolUseId, patch);

    private static bool DictionaryEquals(Dictionary<string, JsonElement>? a, Dictionary<string, JsonElement>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null) return false;
        if (a.Count != b.Count) return false;

        foreach (var kvp in a)
        {
            if (!b.TryGetValue(kvp.Key, out var bValue)) return false;
            if (kvp.Value.ValueKind != bValue.ValueKind) return false;
            if (!JsonElementEquals(kvp.Value, bValue)) return false;
        }

        return true;
    }

    private static bool JsonElementEquals(JsonElement a, JsonElement b)
    {
        return a.ValueKind switch
        {
            JsonValueKind.String => a.GetString() == b.GetString(),
            JsonValueKind.Number => a.GetRawText() == b.GetRawText(),
            JsonValueKind.True or JsonValueKind.False => a.GetBoolean() == b.GetBoolean(),
            JsonValueKind.Null => true,
            _ => a.GetRawText() == b.GetRawText()
        };
    }
}
