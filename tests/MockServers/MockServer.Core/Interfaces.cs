namespace MockServer.Core;

public interface ICacheSimulator
{
    CacheStats ComputeCacheStats(JsonElement request);
    void ResetCache();
}

public interface IResponseStrategy
{
    string BuildResponse(JsonElement request, CacheStats cacheStats);
    bool SupportsStreaming { get; }
    string BuildStreamChunk(string id, string content, bool isLast);

    /// <summary>
    /// 构建流式响应的最终 chunk（包含 usage/cache stats）。
    /// 真实 OpenAI API 在 stream_options.include_usage=true 时，最后一个 chunk 包含 usage 字段。
    /// 真实 Anthropic API 在 message_delta 事件中包含 cache_creation_input_tokens/cache_read_input_tokens。
    /// 默认实现回退到 BuildStreamChunk(id, "", true)，不包含 cache stats（向后兼容）。
    /// </summary>
    string BuildStreamFinalChunk(string id, CacheStats cacheStats) => BuildStreamChunk(id, "", true);

    /// <summary>
    /// 构建流式响应的前导事件（如 Anthropic 的 message_start + content_block_start）。
    /// 返回 null 表示不需要前导事件（OpenAI/DeepSeek 格式）。
    /// </summary>
    string? BuildStreamPreamble(string id) => null;
    /// <summary>
    /// 根据请求内容返回 HTTP 状态码。默认 200。
    /// 用于测试错误处理场景（401/429/500 等）。
    /// </summary>
    int GetHttpStatusCode(JsonElement request) => 200;
    /// <summary>
    /// 获取流式响应的内容分片。每个分片通过 BuildStreamChunk 格式化后发送。
    /// 默认返回 "Hello! This is a mock response from the test server."
    /// </summary>
    string[] GetContentChunks() => ["Hello", "!", " This", " is", " a", " mock", " response", " from", " the", " test", " server", "."];

    /// <summary>
    /// 请求开始时调用 — 策略可在此准备当前轮次（如消费脚本项）
    /// </summary>
    void OnRequestStarted(JsonElement request) { }

    /// <summary>
    /// 当前轮次是否包含工具调用
    /// </summary>
    bool HasToolCalls() => false;

    /// <summary>
    /// 构建工具调用的非流式响应
    /// </summary>
    string BuildToolCallResponse(JsonElement request, CacheStats cacheStats) => "{}";

    /// <summary>
    /// 构建工具调用的流式响应（一次性返回完整工具调用流）
    /// </summary>
    string BuildStreamToolCallResponse(string id) => "";

    /// <summary>
    /// 当前轮次是否有思考内容（reasoning/thinking）
    /// </summary>
    bool HasThinkingContent() => false;

    /// <summary>
    /// 构建思考内容的流式响应（一次性返回完整思考流）
    /// </summary>
    string BuildStreamThinkingResponse(string id) => "";
}

public interface IHttpMockServer : IAsyncDisposable
{
    Task StartAsync(int port = 0);
    Task StopAsync();
    string Url { get; }
    MockServerStats Stats { get; }
    CapturedRequest GetRequest(int index);
    IReadOnlyList<CapturedRequest> GetAllRequests();
    void Clear();
}
