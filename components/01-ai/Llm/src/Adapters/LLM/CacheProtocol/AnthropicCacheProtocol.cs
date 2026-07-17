namespace Api.LLM.CacheProtocol;

internal enum CacheScope
{
    None,
    Org,
    Global
}

internal sealed class AnthropicCacheProtocol : CacheProtocol
{
    public override bool RequiresExplicitCacheMarkers => true;

    public override string? DefaultCacheScope => "ephemeral";

    public string? ResolveScope(bool hasMcpTools) => hasMcpTools ? "org" : null;

    public AnthropicCacheControl CreateCacheControl(bool hasMcpTools, string? ttl = null, CacheScope scope = CacheScope.None)
    {
        return new AnthropicCacheControl
        {
            Scope = scope switch
            {
                CacheScope.Global => "global",
                CacheScope.Org => "org",
                _ => ResolveScope(hasMcpTools)
            },
            Ttl = ttl
        };
    }

    public bool IsStaticSystemBlock(ApiMessage msg)
    {
        return msg.Metadata == null ||
            !msg.Metadata.TryGetValue(CacheBreakMarker.MetadataKey, out var cb) ||
            cb.ValueKind != JsonValueKind.True;
    }

    public void PlaceCacheControlOnSystemBlocks(List<AnthropicSystemContentBlock> blocks, bool hasMcpTools)
    {
        if (blocks.Count == 0) return;

        var cacheControl = CreateCacheControl(hasMcpTools);
        var staticIndex = FindLastStaticBlock(blocks);
        if (staticIndex >= 0)
            blocks[staticIndex].CacheControl = cacheControl;
        else
            blocks[^1].CacheControl = cacheControl;
    }

    public void PlaceCacheControlOnTools(List<AnthropicToolDefinition> tools, bool hasMcpTools)
    {
        if (tools.Count == 0) return;
        tools[^1].CacheControl = CreateCacheControl(hasMcpTools);
    }

    public void PlaceCacheControlOnToolResults(List<AnthropicToolResultBlock> results, bool hasMcpTools)
    {
        if (results.Count == 0) return;
        results[^1].CacheControl = CreateCacheControl(hasMcpTools);
    }

    public void PlaceCacheControlOnToolResults(List<AnthropicMessage> messages, bool hasMcpTools)
    {
        AnthropicToolResultBlock? lastResult = null;
        foreach (var msg in messages)
        {
            if (msg.Content is List<AnthropicContentBlock> blocks)
            {
                foreach (var block in blocks)
                {
                    if (block is AnthropicToolResultBlock result)
                        lastResult = result;
                }
            }
        }

        if (lastResult is not null)
            lastResult.CacheControl = CreateCacheControl(hasMcpTools);
    }

    public TokenUsage MapUsage(AnthropicUsage usage)
    {
        return CreateTokenUsage(
            usage.InputTokens, usage.OutputTokens,
            usage.CacheCreationInputTokens ?? 0,
            usage.CacheReadInputTokens ?? 0,
            usage.OutputTokensDetails?.ReasoningTokens ?? 0);
    }

    private static int FindLastStaticBlock(List<AnthropicSystemContentBlock> blocks)
    {
        for (var i = blocks.Count - 1; i >= 0; i--)
        {
            if (blocks[i].IsStatic) return i;
        }
        return -1;
    }
}
