namespace Core.Hooks.ToolPermission;

/// <summary>
/// 权限队列操作接口 — 管理权限请求队列的增删改
/// </summary>
public interface IPermissionQueueOperations
{
    /// <summary>
    /// 推入权限队列项
    /// </summary>
    void Push(PermissionQueueItem item);

    /// <summary>
    /// 按工具使用ID移除队列项
    /// </summary>
    void Remove(string toolUseId);

    /// <summary>
    /// 按工具使用ID更新队列项
    /// </summary>
    void Update(string toolUseId, Action<PermissionQueueItem> patch);
}

/// <summary>
/// 权限队列项 — 表示一个待处理的权限请求及其回调
/// </summary>
public sealed class PermissionQueueItem
{
    /// <summary>
    /// 工具使用ID
    /// </summary>
    public required string ToolUseId { get; init; }

    /// <summary>
    /// 工具名称
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// 工具描述
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// 工具输入参数
    /// </summary>
    public Dictionary<string, JsonElement> Input { get; init; } = [];

    /// <summary>
    /// 权限检查结果
    /// </summary>
    public PermissionResult? PermissionResult { get; init; }

    /// <summary>
    /// 权限提示开始时间
    /// </summary>
    public DateTimeOffset PermissionPromptStartTime { get; init; }

    /// <summary>
    /// 分类器检查是否进行中
    /// </summary>
    public bool ClassifierCheckInProgress { get; set; }

    /// <summary>
    /// 分类器是否自动批准
    /// </summary>
    public bool ClassifierAutoApproved { get; set; }

    /// <summary>
    /// 分类器匹配的规则名称
    /// </summary>
    public string? ClassifierMatchedRule { get; set; }

    /// <summary>
    /// 中止回调
    /// </summary>
    public Func<Task>? OnAbort { get; set; }

    /// <summary>
    /// 允许回调 — 参数为更新后的输入、权限更新列表、反馈文本
    /// </summary>
    public Func<Dictionary<string, JsonElement>?, List<PermissionUpdate>?, string?, Task>? OnAllow { get; set; }

    /// <summary>
    /// 拒绝回调 — 参数为反馈文本
    /// </summary>
    public Func<string?, Task>? OnReject { get; set; }

    /// <summary>
    /// 用户交互回调
    /// </summary>
    public Func<Task>? OnUserInteraction { get; set; }

    /// <summary>
    /// 关闭勾选框回调
    /// </summary>
    public Func<Task>? OnDismissCheckmark { get; set; }

    /// <summary>
    /// 重新检查权限回调
    /// </summary>
    public Func<Task<PermissionResult?>>? RecheckPermission { get; set; }
}

/// <summary>
/// 权限决策基类 — 派生 Allow/Deny/Ask 三种决策
/// </summary>
public abstract record PermissionDecision
{
    /// <summary>
    /// 决策行为类型
    /// </summary>
    public abstract PermissionBehavior Behavior { get; }
}

/// <summary>
/// 允许决策 — 表示权限请求被批准
/// </summary>
public sealed record PermissionAllowDecision : PermissionDecision
{
    /// <summary>
    /// 决策行为 — 固定为 Allow
    /// </summary>
    public override PermissionBehavior Behavior => PermissionBehavior.Allow;

    /// <summary>
    /// 更新后的工具输入参数
    /// </summary>
    public required Dictionary<string, JsonElement> UpdatedInput { get; init; }

    /// <summary>
    /// 用户是否修改了输入
    /// </summary>
    public bool UserModified { get; init; }

    /// <summary>
    /// 决策原因
    /// </summary>
    public PermissionDecisionReason? DecisionReason { get; init; }

    /// <summary>
    /// 接受反馈文本
    /// </summary>
    public string? AcceptFeedback { get; init; }
}

/// <summary>
/// 拒绝决策 — 表示权限请求被拒绝
/// </summary>
public sealed record PermissionDenyDecision : PermissionDecision
{
    /// <summary>
    /// 决策行为 — 固定为 Deny
    /// </summary>
    public override PermissionBehavior Behavior => PermissionBehavior.Deny;

    /// <summary>
    /// 拒绝消息
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// 决策原因
    /// </summary>
    public required PermissionDecisionReason DecisionReason { get; init; }
}

/// <summary>
/// 询问决策 — 表示权限请求需要用户确认
/// </summary>
public sealed record PermissionAskDecision : PermissionDecision
{
    /// <summary>
    /// 决策行为 — 固定为 Ask
    /// </summary>
    public override PermissionBehavior Behavior => PermissionBehavior.Ask;

    /// <summary>
    /// 询问消息
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// 建议的权限更新列表
    /// </summary>
    public List<PermissionUpdate>? Suggestions { get; init; }

    /// <summary>
    /// 被阻止的路径
    /// </summary>
    public string? BlockedPath { get; init; }

    /// <summary>
    /// 待处理的分类器检查
    /// </summary>
    public object? PendingClassifierCheck { get; init; }

    /// <summary>
    /// 更新后的工具输入参数
    /// </summary>
    public Dictionary<string, JsonElement>? UpdatedInput { get; init; }
}

/// <summary>
/// 一次性解析器 — 确保回调只被解析一次的线程安全包装器
/// </summary>
public sealed class ResolveOnce<T>
{
    private bool _claimed;
    private bool _delivered;
    private readonly Action<T> _resolve;

    /// <summary>
    /// 构造一次性解析器
    /// </summary>
    public ResolveOnce(Action<T> resolve)
    {
        _resolve = resolve;
    }

    /// <summary>
    /// 解析值 — 仅首次调用生效，后续调用为空操作
    /// </summary>
    public void Resolve(T value)
    {
        if (_delivered) return;
        _delivered = true;
        _claimed = true;
        _resolve(value);
    }

    /// <summary>
    /// 是否已解析
    /// </summary>
    public bool IsResolved() => _claimed;

    /// <summary>
    /// 尝试认领 — 仅首个调用者返回 true，用于独占执行权
    /// </summary>
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
    /// <summary>
    /// 工具名称
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// 工具输入参数
    /// </summary>
    public required Dictionary<string, JsonElement> Input { get; init; }

    /// <summary>
    /// 消息ID
    /// </summary>
    public required string MessageId { get; init; }

    /// <summary>
    /// 工具使用ID
    /// </summary>
    public required string ToolUseId { get; init; }
}

/// <summary>
/// 权限上下文 — 封装单次权限检查所需的工具调用信息、日志器和队列操作
/// </summary>
public sealed class PermissionContext
{
    private readonly IPermissionLogger _logger;
    private readonly IPermissionQueueOperations? _queueOps;

    /// <summary>
    /// 工具调用标识
    /// </summary>
    public PermissionToolCall ToolCall { get; }

    /// <summary>
    /// 工具名称
    /// </summary>
    public string ToolName => ToolCall.ToolName;

    /// <summary>
    /// 工具输入参数
    /// </summary>
    public Dictionary<string, JsonElement> Input => ToolCall.Input;

    /// <summary>
    /// 消息ID
    /// </summary>
    public string MessageId => ToolCall.MessageId;

    /// <summary>
    /// 工具使用ID
    /// </summary>
    public string ToolUseId => ToolCall.ToolUseId;

    /// <summary>
    /// 取消令牌
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// 构造权限上下文
    /// </summary>
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

    /// <summary>
    /// 记录权限决策日志
    /// </summary>
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

    /// <summary>
    /// 记录权限取消日志
    /// </summary>
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

    /// <summary>
    /// 若已取消则解析为中止决策 — 返回 true 表示已解析
    /// </summary>
    public bool ResolveIfAborted(Action<PermissionDecision> resolve)
    {
        if (!CancellationToken.IsCancellationRequested) return false;

        LogCancelled();
        resolve(CancelAndAbort());
        return true;
    }

    /// <summary>
    /// 构造取消并中止的拒绝决策
    /// </summary>
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

    /// <summary>
    /// 构造允许决策
    /// </summary>
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

    /// <summary>
    /// 构造拒绝决策
    /// </summary>
    public PermissionDecision BuildDeny(string message, PermissionDecisionReason decisionReason)
    {
        return new PermissionDenyDecision
        {
            Message = message,
            DecisionReason = decisionReason
        };
    }

    /// <summary>
    /// 处理用户批准 — 记录日志并构造允许决策
    /// </summary>
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

    /// <summary>
    /// 处理 Hook 批准 — 记录日志并构造允许决策
    /// </summary>
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

    /// <summary>
    /// 推入权限队列项
    /// </summary>
    public void PushToQueue(PermissionQueueItem item) => _queueOps?.Push(item);

    /// <summary>
    /// 从权限队列移除当前项
    /// </summary>
    public void RemoveFromQueue() => _queueOps?.Remove(ToolUseId);

    /// <summary>
    /// 更新当前队列项
    /// </summary>
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
