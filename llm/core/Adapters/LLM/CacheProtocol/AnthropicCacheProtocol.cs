namespace Api.LLM.CacheProtocol;

internal enum CacheScope {
    None,
    Org,
    Global
}

internal sealed class AnthropicCacheProtocol : CacheProtocol {
    public override bool RequiresExplicitCacheMarkers => true;

    public override string? DefaultCacheScope => "ephemeral";

    /// <summary>解析缓存作用域。</summary>
    /// <param name="hasMcpTools">是否包含 MCP 工具。</param>
    /// <returns>缓存作用域字符串。</returns>
    public string? ResolveScope(bool hasMcpTools) => hasMcpTools ? "org" : null;

    /// <summary>创建缓存控制对象。</summary>
    /// <param name="hasMcpTools">是否包含 MCP 工具。</param>
    /// <param name="ttl">缓存存活时间。</param>
    /// <param name="scope">缓存作用域。</param>
    /// <returns>Anthropic 缓存控制对象。</returns>
    public AnthropicCacheControl CreateCacheControl(bool hasMcpTools, string? ttl = null, CacheScope scope = CacheScope.None) {
        return new AnthropicCacheControl {
            Scope = scope switch {
                CacheScope.Global => "global",
                CacheScope.Org => "org",
                _ => ResolveScope(hasMcpTools)
            },
            Ttl = ttl
        };
    }

    /// <summary>判断是否为静态系统块。</summary>
    /// <param name="msg">API 消息。</param>
    /// <returns>是否为静态系统块。</returns>
    public bool IsStaticSystemBlock(ApiMessage msg) {
        return msg.Metadata == null ||
            !msg.Metadata.TryGetValue(CacheBreakMarker.MetadataKey, out var cb) ||
            cb.ValueKind != JsonValueKind.True;
    }

    /// <summary>在系统块上放置缓存控制标记。</summary>
    /// <param name="blocks">系统内容块列表。</param>
    /// <param name="hasMcpTools">是否包含 MCP 工具。</param>
    public void PlaceCacheControlOnSystemBlocks(List<AnthropicSystemContentBlock> blocks, bool hasMcpTools) {
        if (blocks.Count == 0) return;

        var cacheControl = CreateCacheControl(hasMcpTools);
        var staticIndex = FindLastStaticBlock(blocks);
        if (staticIndex >= 0)
            blocks[staticIndex].CacheControl = cacheControl;
        else
            blocks[^1].CacheControl = cacheControl;
    }

    /// <summary>在工具列表上放置缓存控制标记。</summary>
    /// <param name="tools">工具定义列表。</param>
    /// <param name="hasMcpTools">是否包含 MCP 工具。</param>
    public void PlaceCacheControlOnTools(List<AnthropicToolDefinition> tools, bool hasMcpTools) {
        if (tools.Count == 0) return;
        tools[^1].CacheControl = CreateCacheControl(hasMcpTools);
    }

    /// <summary>在工具结果块上放置缓存控制标记。</summary>
    /// <param name="results">工具结果块列表。</param>
    /// <param name="hasMcpTools">是否包含 MCP 工具。</param>
    public void PlaceCacheControlOnToolResults(List<AnthropicToolResultBlock> results, bool hasMcpTools) {
        if (results.Count == 0) return;
        results[^1].CacheControl = CreateCacheControl(hasMcpTools);
    }

    /// <summary>在消息列表中的工具结果上放置缓存控制标记。</summary>
    /// <param name="messages">消息列表。</param>
    /// <param name="hasMcpTools">是否包含 MCP 工具。</param>
    public void PlaceCacheControlOnToolResults(List<AnthropicMessage> messages, bool hasMcpTools) {
        AnthropicToolResultBlock? lastResult = null;
        foreach (var msg in messages) {
            if (msg.Content?.Blocks is { Count: > 0 } blocks) {
                foreach (var block in blocks) {
                    if (block is AnthropicToolResultBlock result)
                        lastResult = result;
                }
            }
        }

        if (lastResult is not null)
            lastResult.CacheControl = CreateCacheControl(hasMcpTools);
    }

    /// <summary>添加缓存断点 — 在系统块、工具和工具结果上放置缓存控制标记。</summary>
    /// <param name="systemBlocks">系统内容块列表。</param>
    /// <param name="tools">工具定义列表。</param>
    /// <param name="messages">消息列表。</param>
    /// <param name="hasMcpTools">是否包含 MCP 工具。</param>
    public void AddCacheBreakpoints(
        List<AnthropicSystemContentBlock> systemBlocks,
        List<AnthropicToolDefinition> tools,
        List<AnthropicMessage> messages,
        bool hasMcpTools) {
        PlaceCacheControlOnSystemBlocks(systemBlocks, hasMcpTools);
        PlaceCacheControlOnTools(tools, hasMcpTools);
        PlaceCacheControlOnToolResults(messages, hasMcpTools);
    }

    /// <summary>映射 Anthropic 用量到 TokenUsage。</summary>
    /// <param name="usage">Anthropic 用量。</param>
    /// <returns>Token 用量。</returns>
    public TokenUsage MapUsage(AnthropicUsage usage) {
        return CreateTokenUsage(
            usage.InputTokens, usage.OutputTokens,
            usage.CacheCreationInputTokens ?? 0,
            usage.CacheReadInputTokens ?? 0,
            usage.OutputTokensDetails?.ReasoningTokens ?? 0);
    }

    private static int FindLastStaticBlock(List<AnthropicSystemContentBlock> blocks) {
        for (var i = blocks.Count - 1; i >= 0; i--) {
            if (blocks[i].IsStatic) return i;
        }
        return -1;
    }
}
