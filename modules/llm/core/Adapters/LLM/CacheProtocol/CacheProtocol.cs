namespace Api.LLM.CacheProtocol;

internal abstract class CacheProtocol {
    /// <summary>获取是否需要显式缓存标记。</summary>
    public abstract bool RequiresExplicitCacheMarkers { get; }

    /// <summary>获取默认缓存作用域。</summary>
    public abstract string? DefaultCacheScope { get; }

    protected static TokenUsage CreateTokenUsage(
        int promptTokens, int completionTokens,
        int cacheCreation = 0, int cacheRead = 0, int reasoning = 0) {
        return new TokenUsage(promptTokens, completionTokens) {
            CacheCreationInputTokens = cacheCreation,
            CacheReadInputTokens = cacheRead,
            ReasoningTokens = reasoning
        };
    }
}