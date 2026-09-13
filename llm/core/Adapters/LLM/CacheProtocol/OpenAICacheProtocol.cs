namespace Api.LLM.CacheProtocol;

internal sealed class OpenAICacheProtocol : CacheProtocol
{
    public override bool RequiresExplicitCacheMarkers => false;

    public override string? DefaultCacheScope => null;

    public TokenUsage MapUsage(OpenAIUsage usage)
    {
        var cacheRead = usage.PromptTokensDetails?.CachedTokens ?? 0;
        var cacheCreation = 0;

        if (usage.PromptCacheHitTokens.HasValue || usage.PromptCacheMissTokens.HasValue)
        {
            cacheCreation = usage.PromptCacheMissTokens ?? 0;
            cacheRead = usage.PromptCacheHitTokens ?? 0;
        }

        return CreateTokenUsage(usage.PromptTokens, usage.CompletionTokens, cacheCreation, cacheRead);
    }
}
