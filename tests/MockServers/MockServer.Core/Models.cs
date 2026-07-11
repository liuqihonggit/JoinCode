namespace MockServer.Core;

public sealed class CapturedRequest
{
    public required string Method { get; init; }
    public required string Path { get; init; }
    public required string Body { get; init; }
    public required Dictionary<string, string> Headers { get; init; }
    public int Index { get; init; }
}

public sealed class MockServerStats
{
    public int TotalRequests { get; set; }
    public int CacheHits { get; set; }
    public int CacheMisses { get; set; }
}

public sealed class CacheStats
{
    public required int CacheCreationTokens { get; init; }
    public required int CacheReadTokens { get; init; }
    public required int InputTokens { get; init; }
    public required int OutputTokens { get; init; }
}
