namespace JoinCode.Abstractions.Hooks;

public sealed record HookBlockingError
{
    public required string BlockingError { get; init; }

    public required string Command { get; init; }
}

public abstract record PermissionRequestResult
{
    public abstract PermissionBehavior Behavior { get; }

    public static PermissionRequestResult Allow(
        Dictionary<string, JsonElement>? updatedInput = null,
        List<PermissionUpdate>? updatedPermissions = null)
    {
        return new PermissionAllowResult
        {
            UpdatedInput = updatedInput,
            UpdatedPermissions = updatedPermissions
        };
    }

    public static PermissionRequestResult Deny(
        string message,
        bool interrupt = false)
    {
        return new PermissionDenyResult
        {
            Message = message,
            Interrupt = interrupt
        };
    }
}

public sealed record PermissionAllowResult : PermissionRequestResult
{
    public override PermissionBehavior Behavior => PermissionBehavior.Allow;
    public Dictionary<string, JsonElement>? UpdatedInput { get; init; }
    public IReadOnlyList<PermissionUpdate>? UpdatedPermissions { get; init; }
}

public sealed record PermissionDenyResult : PermissionRequestResult
{
    public override PermissionBehavior Behavior => PermissionBehavior.Deny;
    public string? Message { get; init; }
    public bool Interrupt { get; init; }
}

public sealed record HookResult
{
    public required HookOutcome Outcome { get; init; }

    public string? Message { get; init; }

    public string? SystemMessage { get; init; }

    public HookBlockingError? BlockingError { get; init; }

    public bool PreventContinuation { get; init; }

    public string? StopReason { get; init; }

    public Dictionary<string, JsonElement>? UpdatedInput { get; init; }

    public PermissionRequestResult? PermissionRequestResult { get; init; }

    public bool Retry { get; init; }

    public string? AdditionalContext { get; init; }

    public string? InitialUserMessage { get; init; }

    public IReadOnlyList<string>? WatchPaths { get; init; }

    public static HookResult Success(
        string? message = null,
        Dictionary<string, JsonElement>? updatedInput = null,
        string? additionalContext = null)
    {
        return new HookResult
        {
            Outcome = HookOutcome.Success,
            Message = message,
            UpdatedInput = updatedInput,
            AdditionalContext = additionalContext
        };
    }

    public static HookResult Blocking(
        string error,
        string command,
        string? message = null)
    {
        return new HookResult
        {
            Outcome = HookOutcome.Blocking,
            Message = message,
            BlockingError = new HookBlockingError
            {
                BlockingError = error,
                Command = command
            },
            PreventContinuation = true
        };
    }

    public static HookResult NonBlockingError(
        string error,
        string? message = null)
    {
        return new HookResult
        {
            Outcome = HookOutcome.NonBlockingError,
            Message = message ?? error
        };
    }

    public static HookResult Cancelled()
    {
        return new HookResult
        {
            Outcome = HookOutcome.Cancelled
        };
    }

    public static HookResult PermissionAllow(
        Dictionary<string, JsonElement>? updatedInput = null,
        List<PermissionUpdate>? updatedPermissions = null)
    {
        return new HookResult
        {
            Outcome = HookOutcome.Success,
            PermissionRequestResult = PermissionRequestResult.Allow(updatedInput, updatedPermissions)
        };
    }

    public static HookResult PermissionDeny(
        string message,
        bool interrupt = false)
    {
        return new HookResult
        {
            Outcome = HookOutcome.Success,
            PermissionRequestResult = PermissionRequestResult.Deny(message, interrupt)
        };
    }
}
