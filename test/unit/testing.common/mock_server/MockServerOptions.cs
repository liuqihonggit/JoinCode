namespace Testing.Common.MockServer;

/// <summary>
/// Mock Server 配置参数封装类
/// </summary>
public sealed record MockServerOptions {
    public const string DefaultApiKey = "sk-test-1234567890";
    public const string DefaultModel = "gpt-4o";

    public string PipeName { get; }
    public string ApiKey { get; }
    public string Model { get; }

    /// <summary>
    /// SSE 响应每个 data 行之间的注入延迟（毫秒，默认 0 = 不延迟）。
    /// 用于测试流式读取的超时防御与慢流场景（run #858 挂死回归背景）。
    /// </summary>
    public int SseChunkDelayMs { get; init; }

    public MockServerOptions(string pipeName, string? apiKey = null, string? model = null) {
        ArgumentException.ThrowIfNullOrEmpty(pipeName);
        PipeName = pipeName;
        ApiKey = apiKey ?? DefaultApiKey;
        Model = model ?? DefaultModel;
    }
}