namespace MockServer.Core;

public interface ICacheSimulator {
    /// <summary>计算缓存统计信息。</summary>
    CacheStats ComputeCacheStats(JsonElement request);
    /// <summary>重置缓存。</summary>
    void ResetCache();
}

public interface IResponseStrategy {
    /// <summary>构建非流式响应。</summary>
    string BuildResponse(JsonElement request, CacheStats cacheStats);
    /// <summary>获取是否支持流式响应。</summary>
    bool SupportsStreaming { get; }
    /// <summary>构建流式响应分片。</summary>
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
    string BuildStreamToolCallResponse(string id, CacheStats cacheStats) => "";

    /// <summary>
    /// 当前轮次是否有思考内容（reasoning/thinking）
    /// </summary>
    bool HasThinkingContent() => false;

    /// <summary>
    /// 构建思考内容的流式响应（一次性返回完整思考流）
    /// </summary>
    string BuildStreamThinkingResponse(string id) => "";

    /// <summary>
    /// 两阶段工具加载 — 构建工具描述请求。
    /// 当请求包含 tool_groups（只有分组没有完整 schema）时调用。
    /// 返回 JSON: {"type":"tool_description_request","tools":["read","bash"]}
    /// 返回 null 表示不需要两阶段加载（请求已包含完整 tools）。
    /// </summary>
    string? BuildToolDescriptionRequest(JsonElement request) => null;
}

public interface IHttpMockServer : IAsyncDisposable {
    /// <summary>启动 Mock 服务器。</summary>
    Task StartAsync(int port = 0);
    /// <summary>停止 Mock 服务器。</summary>
    Task StopAsync();
    /// <summary>获取服务器 URL。</summary>
    string Url { get; }
    /// <summary>获取服务器统计信息。</summary>
    MockServerStats Stats { get; }
    /// <summary>按索引获取捕获的请求。</summary>
    CapturedRequest GetRequest(int index);
    /// <summary>获取所有捕获的请求列表。</summary>
    IReadOnlyList<CapturedRequest> GetAllRequests();
    /// <summary>清除所有捕获的请求和统计。</summary>
    void Clear();
}