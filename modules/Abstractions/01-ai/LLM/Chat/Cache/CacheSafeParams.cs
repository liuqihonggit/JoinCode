namespace JoinCode.Abstractions.LLM.Chat;

public sealed class CacheSafeParams
{
    public string? RenderedSystemPrompt { get; init; }
    public string? ModelId { get; init; }
    public IReadOnlyList<string>? ToolNames { get; init; }
    public Dictionary<string, string>? UserContext { get; init; }
    public Dictionary<string, string>? SystemContext { get; init; }
    public ContentReplacementState? ContentReplacementState { get; init; }

    public CacheSafeParams Clone()
    {
        return new CacheSafeParams
        {
            RenderedSystemPrompt = RenderedSystemPrompt,
            ModelId = ModelId,
            ToolNames = ToolNames,
            UserContext = UserContext is not null
                ? new Dictionary<string, string>(UserContext)
                : null,
            SystemContext = SystemContext is not null
                ? new Dictionary<string, string>(SystemContext)
                : null,
            ContentReplacementState = ContentReplacementState?.Clone()
        };
    }
}
