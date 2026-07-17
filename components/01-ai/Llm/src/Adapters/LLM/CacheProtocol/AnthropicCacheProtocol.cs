namespace Api.LLM.CacheProtocol;

internal sealed class AnthropicCacheProtocol : CacheProtocol
{
    public override bool RequiresExplicitCacheMarkers => true;

    public override string? DefaultCacheScope => "ephemeral";

    public string? ResolveScope(bool hasMcpTools) => hasMcpTools ? "org" : null;

    public TokenUsage MapUsage(AnthropicUsage usage)
    {
        return CreateTokenUsage(
            usage.InputTokens, usage.OutputTokens,
            usage.CacheCreationInputTokens ?? 0,
            usage.CacheReadInputTokens ?? 0,
            usage.OutputTokensDetails?.ReasoningTokens ?? 0);
    }
}
