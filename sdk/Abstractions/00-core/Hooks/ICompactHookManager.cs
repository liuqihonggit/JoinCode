namespace JoinCode.Abstractions.Hooks;

public interface ICompactHookManager
{
    Task<CompactHookResult> OnPreCompactAsync(CompactHookContext context, CancellationToken ct = default);

    Task OnPostCompactAsync(CompactHookContext context, PostCompactData result, CancellationToken ct = default);
}

public sealed partial class CompactHookContext
{
    public required string SessionId { get; init; }
    public required string Trigger { get; init; }
    public int CurrentTokenCount { get; init; }
    public int TargetTokenCount { get; init; }
    public Dictionary<string, JsonElement> Metadata { get; init; } = new();
}

public sealed partial class PostCompactData
{
    public bool Compacted { get; init; }
    public string? Level { get; init; }
    public string? Trigger { get; init; }
    public string? Summary { get; init; }
    public int PreCompactTokenCount { get; init; }
    public int PostCompactTokenCount { get; init; }
    public int MessagesRemoved { get; init; }
    public int MessagesPreserved { get; init; }
    public string? ErrorMessage { get; init; }
    public Dictionary<string, JsonElement> Metadata { get; init; } = new();
}

public sealed partial class CompactHookResult
{
    public bool ShouldCompact { get; init; } = true;
    public string? Message { get; init; }
    public CompactHookAction Action { get; init; } = CompactHookAction.Proceed;
}

public enum CompactHookAction { [EnumValue("proceed")] Proceed, [EnumValue("skip")] Skip, [EnumValue("defer")] Defer, [EnumValue("custom")] Custom }
