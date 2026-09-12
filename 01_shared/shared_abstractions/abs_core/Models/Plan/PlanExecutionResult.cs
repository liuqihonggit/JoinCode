
namespace JoinCode.Abstractions.Models;

public sealed record PlanExecutionResult {
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    [JsonPropertyName("result")]
    public string Result { get; set; } = string.Empty;

    [JsonPropertyName("executionTimeMs")]
    public long ExecutionTimeMs { get; set; }

    [JsonPropertyName("tokenUsage")]
    public TokenUsage TokenUsage { get; set; } = new();

    [JsonPropertyName("functionCalls")]
    public List<FunctionCallInfo> FunctionCalls { get; set; } = new();

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public sealed record FunctionCallInfo {
    [JsonPropertyName("pluginName")]
    public string PluginName { get; set; } = string.Empty;

    [JsonPropertyName("functionName")]
    public string FunctionName { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public Dictionary<string, JsonElement> Arguments { get; set; } = new();

    [JsonPropertyName("result")]
    public string Result { get; set; } = string.Empty;

    [JsonPropertyName("executionTimeMs")]
    public long ExecutionTimeMs { get; set; }
}
