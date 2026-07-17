namespace Api.LLM.CacheProtocol;

internal abstract class CacheProtocol
{
    public abstract bool RequiresExplicitCacheMarkers { get; }

    public abstract string? DefaultCacheScope { get; }

    protected static TokenUsage CreateTokenUsage(
        int promptTokens, int completionTokens,
        int cacheCreation = 0, int cacheRead = 0, int reasoning = 0)
    {
        return new TokenUsage(promptTokens, completionTokens)
        {
            CacheCreationInputTokens = cacheCreation,
            CacheReadInputTokens = cacheRead,
            ReasoningTokens = reasoning
        };
    }
}
