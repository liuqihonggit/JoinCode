namespace MockServer.Core;

public sealed class CapturedRequest {
    /// <summary>获取或设置 HTTP 方法。</summary>
    public required string Method { get; init; }
    /// <summary>获取或设置请求路径。</summary>
    public required string Path { get; init; }
    /// <summary>获取或设置请求体。</summary>
    public required string Body { get; init; }
    /// <summary>获取或设置请求头。</summary>
    public required Dictionary<string, string> Headers { get; init; }
    /// <summary>获取或设置请求索引。</summary>
    public int Index { get; init; }
}

public sealed class MockServerStats {
    /// <summary>获取或设置总请求数。</summary>
    public int TotalRequests { get; set; }
    /// <summary>获取或设置缓存命中数。</summary>
    public int CacheHits { get; set; }
    /// <summary>获取或设置缓存未命中数。</summary>
    public int CacheMisses { get; set; }
}

public sealed class CacheStats {
    /// <summary>获取或设置缓存创建令牌数。</summary>
    public required int CacheCreationTokens { get; init; }
    /// <summary>获取或设置缓存读取令牌数。</summary>
    public required int CacheReadTokens { get; init; }
    /// <summary>获取或设置输入令牌数。</summary>
    public required int InputTokens { get; init; }
    /// <summary>获取或设置输出令牌数。</summary>
    public required int OutputTokens { get; init; }
}