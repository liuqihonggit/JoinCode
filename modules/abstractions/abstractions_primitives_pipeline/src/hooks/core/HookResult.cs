namespace JoinCode.Abstractions.Hooks;

public sealed record HookBlockingError {
    /// <summary>获取阻塞错误信息。</summary>
    public required string BlockingError { get; init; }

    /// <summary>获取触发阻塞的命令。</summary>
    public required string Command { get; init; }
}

public abstract record PermissionRequestResult {
    /// <summary>获取权限行为类型。</summary>
    public abstract PermissionBehavior Behavior { get; }

    /// <summary>创建允许权限的请求结果。</summary>
    public static PermissionRequestResult Allow(
        Dictionary<string, JsonElement>? updatedInput = null,
        List<PermissionUpdate>? updatedPermissions = null) {
        return new PermissionAllowResult {
            UpdatedInput = updatedInput,
            UpdatedPermissions = updatedPermissions
        };
    }

    /// <summary>创建拒绝权限的请求结果。</summary>
    public static PermissionRequestResult Deny(
        string message,
        bool interrupt = false) {
        return new PermissionDenyResult {
            Message = message,
            Interrupt = interrupt
        };
    }
}

public sealed record PermissionAllowResult : PermissionRequestResult {
    /// <summary>获取权限行为类型。</summary>
    public override PermissionBehavior Behavior => PermissionBehavior.Allow;
    /// <summary>获取更新后的输入数据。</summary>
    public Dictionary<string, JsonElement>? UpdatedInput { get; init; }
    /// <summary>获取更新后的权限列表。</summary>
    public IReadOnlyList<PermissionUpdate>? UpdatedPermissions { get; init; }
}

public sealed record PermissionDenyResult : PermissionRequestResult {
    /// <summary>获取权限行为类型。</summary>
    public override PermissionBehavior Behavior => PermissionBehavior.Deny;
    /// <summary>获取拒绝消息。</summary>
    public string? Message { get; init; }
    /// <summary>获取是否中断执行。</summary>
    public bool Interrupt { get; init; }
}

public sealed record HookResult {
    /// <summary>获取钩子执行结果类型。</summary>
    public required HookOutcome Outcome { get; init; }

    /// <summary>获取结果消息。</summary>
    public string? Message { get; init; }

    /// <summary>获取系统消息。</summary>
    public string? SystemMessage { get; init; }

    /// <summary>获取阻塞错误信息。</summary>
    public HookBlockingError? BlockingError { get; init; }

    /// <summary>获取是否阻止后续继续执行。</summary>
    public bool PreventContinuation { get; init; }

    /// <summary>获取停止原因。</summary>
    public string? StopReason { get; init; }

    /// <summary>获取更新后的输入数据。</summary>
    public Dictionary<string, JsonElement>? UpdatedInput { get; init; }

    /// <summary>获取权限请求结果。</summary>
    public PermissionRequestResult? PermissionRequestResult { get; init; }

    /// <summary>获取是否需要重试。</summary>
    public bool Retry { get; init; }

    /// <summary>获取附加上下文信息。</summary>
    public string? AdditionalContext { get; init; }

    /// <summary>获取初始用户消息。</summary>
    public string? InitialUserMessage { get; init; }

    /// <summary>获取监视路径列表。</summary>
    public IReadOnlyList<string>? WatchPaths { get; init; }

    /// <summary>创建成功结果。</summary>
    public static HookResult Success(
        string? message = null,
        Dictionary<string, JsonElement>? updatedInput = null,
        string? additionalContext = null) {
        return new HookResult {
            Outcome = HookOutcome.Success,
            Message = message,
            UpdatedInput = updatedInput,
            AdditionalContext = additionalContext
        };
    }

    /// <summary>创建阻塞结果。</summary>
    public static HookResult Blocking(
        string error,
        string command,
        string? message = null) {
        return new HookResult {
            Outcome = HookOutcome.Blocking,
            Message = message,
            BlockingError = new HookBlockingError {
                BlockingError = error,
                Command = command
            },
            PreventContinuation = true
        };
    }

    /// <summary>创建非阻塞错误结果。</summary>
    public static HookResult NonBlockingError(
        string error,
        string? message = null) {
        return new HookResult {
            Outcome = HookOutcome.NonBlockingError,
            Message = message ?? error
        };
    }

    /// <summary>创建已取消结果。</summary>
    public static HookResult Cancelled() {
        return new HookResult {
            Outcome = HookOutcome.Cancelled
        };
    }

    /// <summary>创建权限允许结果。</summary>
    public static HookResult PermissionAllow(
        Dictionary<string, JsonElement>? updatedInput = null,
        List<PermissionUpdate>? updatedPermissions = null) {
        return new HookResult {
            Outcome = HookOutcome.Success,
            PermissionRequestResult = PermissionRequestResult.Allow(updatedInput, updatedPermissions)
        };
    }

    /// <summary>创建权限拒绝结果。</summary>
    public static HookResult PermissionDeny(
        string message,
        bool interrupt = false) {
        return new HookResult {
            Outcome = HookOutcome.Success,
            PermissionRequestResult = PermissionRequestResult.Deny(message, interrupt)
        };
    }
}