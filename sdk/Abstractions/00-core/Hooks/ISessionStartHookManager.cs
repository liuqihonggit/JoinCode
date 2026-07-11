namespace JoinCode.Abstractions.Hooks;

public interface ISessionStartHookManager
{
    Task<SessionStartHookResult> OnSessionStartAsync(SessionStartHookContext context, CancellationToken ct = default);
}

public sealed partial class SessionStartHookContext
{
    public required string SessionId { get; init; }
    public required string Source { get; init; }
    public Dictionary<string, JsonElement> Configuration { get; init; } = new();
}

public sealed partial class SessionStartHookResult
{
    public bool ShouldProceed { get; init; } = true;
    public string? Message { get; init; }
    public Dictionary<string, JsonElement> AdditionalConfig { get; init; } = new();
}
