namespace JoinCode.Abstractions.Mcp.Protocol;

public class PingResult {
}

public class LoggingSetLevelRequestParams {
    /// <summary>获取或设置日志级别。</summary>
    [JsonPropertyName("level")]
    public string Level { get; set; } = "info";
}