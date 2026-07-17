namespace Api.LLM.CacheProtocol;

internal sealed class AnthropicCacheProtocol : CacheProtocol
{
    public override bool RequiresExplicitCacheMarkers => true;

    public override string? DefaultCacheScope => "ephemeral";

    public string? ResolveScope(bool hasMcpTools) => hasMcpTools ? "org" : null;

    public AnthropicCacheControl CreateCacheControl(bool hasMcpTools)
    {
        return new AnthropicCacheControl { Scope = ResolveScope(hasMcpTools) };
    }

    public bool IsStaticSystemBlock(ApiMessage msg)
    {
        return msg.Metadata == null ||
            !msg.Metadata.TryGetValue(CacheBreakMarker.MetadataKey, out var cb) ||
            cb.ValueKind != JsonValueKind.True;
    }

    public TokenUsage MapUsage(AnthropicUsage usage)
    {
        return CreateTokenUsage(
            usage.InputTokens, usage.OutputTokens,
            usage.CacheCreationInputTokens ?? 0,
            usage.CacheReadInputTokens ?? 0,
            usage.OutputTokensDetails?.ReasoningTokens ?? 0);
    }
}
