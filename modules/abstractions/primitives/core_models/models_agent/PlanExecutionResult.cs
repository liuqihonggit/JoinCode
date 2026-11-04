
namespace JoinCode.Abstractions.Models;

public sealed record PlanExecutionResult {
    /// <summary>获取或设置是否成功。</summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>获取或设置提示词。</summary>
    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    /// <summary>获取或设置执行结果文本。</summary>
    [JsonPropertyName("result")]
    public string Result { get; set; } = string.Empty;

    /// <summary>获取或设置执行耗时（毫秒）。</summary>
    [JsonPropertyName("executionTimeMs")]
    public long ExecutionTimeMs { get; set; }

    /// <summary>获取或设置 Token 用量。</summary>
    [JsonPropertyName("tokenUsage")]
    public TokenUsage TokenUsage { get; set; } = new();

    /// <summary>获取或设置函数调用列表。</summary>
    [JsonPropertyName("functionCalls")]
    public List<FunctionCallInfo> FunctionCalls { get; set; } = new();

    /// <summary>获取或设置错误消息。</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>获取或设置时间戳。</summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public sealed record FunctionCallInfo {
    /// <summary>获取或设置插件名称。</summary>
    [JsonPropertyName("pluginName")]
    public string PluginName { get; set; } = string.Empty;

    /// <summary>获取或设置函数名称。</summary>
    [JsonPropertyName("functionName")]
    public string FunctionName { get; set; } = string.Empty;

    /// <summary>获取或设置调用参数字典。</summary>
    [JsonPropertyName("arguments")]
    public Dictionary<string, JsonElement> Arguments { get; set; } = new();

    /// <summary>获取或设置调用结果文本。</summary>
    [JsonPropertyName("result")]
    public string Result { get; set; } = string.Empty;

    /// <summary>获取或设置调用耗时（毫秒）。</summary>
    [JsonPropertyName("executionTimeMs")]
    public long ExecutionTimeMs { get; set; }
}