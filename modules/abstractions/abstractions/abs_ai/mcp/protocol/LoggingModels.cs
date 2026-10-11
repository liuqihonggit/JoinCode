namespace JoinCode.Abstractions.Mcp.Protocol;

public sealed record PingResult {
}

public sealed record LoggingSetLevelRequestParams {
    /// <summary>获取或设置日志级别。</summary>
    [JsonPropertyName("level")]
    public string Level { get; init; } = "info";
}