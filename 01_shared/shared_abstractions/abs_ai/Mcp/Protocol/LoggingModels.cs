namespace JoinCode.Abstractions.Mcp.Protocol;

public class PingResult
{
}

public class LoggingSetLevelRequestParams
{
    [JsonPropertyName("level")]
    public string Level { get; set; } = "info";
}
