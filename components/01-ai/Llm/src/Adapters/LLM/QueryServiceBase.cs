namespace Api.LLM;

/// <summary>
/// QueryService 协议层基类 — 仅包含协议无关基础设施（HttpClient / 速率限制 / 角色转换）
/// 三个派生类（OpenAIQueryService / AzureQueryService / AnthropicQueryService）各自实现具体协议
/// 不包含任何 ProviderKind switch/if — 协议差异通过派生类多态实现
/// </summary>
public abstract partial class QueryServiceBase : IQueryService
{
    /// <summary>构造时解析的 Provider 定义（永不为 null，缺失则 ctor 抛异常）</summary>
    protected readonly IProviderDefinition Definition;

    /// <summary>Provider 配置（不可变引用）</summary>
    protected readonly ProviderConfig Config;

    /// <summary>共享 HttpClient（由派生类用于发送请求）</summary>
    protected readonly HttpClient HttpClient;

    /// <summary>可选日志器</summary>
    protected readonly ILogger? Logger;

    /// <summary>可选文件系统（供派生类做 IO 时使用）</summary>
    protected readonly IFileSystem? FileSystem;

    /// <summary>最近一次响应的速率限制头（流式首块注入 metadata 用）</summary>
    private volatile Dictionary<string, string?>? _lastRateLimitHeaders;

    protected QueryServiceBase(ProviderConfig config, HttpClient? httpClient = null, ILogger? logger = null, IFileSystem? fs = null)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Logger = logger;
        FileSystem = fs;

        // Definition 强制非空 — ProviderConfig.Definition 由 ConfigLoader 在加载时注入
        // 若调用方未注入（null），QueryServiceFactory 会自动注入 FallbackProviderDefinition 兜底
        Definition = config.Definition
            ?? throw new InvalidOperationException(
                $"Provider '{config.Provider}' 的 IProviderDefinition 未注入。请通过 QueryServiceFactory 创建或手动注入 Definition。");

        HttpClient = httpClient ?? CreateHttpClient(config);
    }

    /// <summary>非流式聊天补全 — 由派生类按各自协议实现</summary>
    public abstract Task<IReadOnlyList<ApiMessage>> GetApiMessageContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        CancellationToken cancellationToken = default);

    /// <summary>流式聊天补全 — 由派生类按各自协议实现</summary>
    public abstract IAsyncEnumerable<StreamEvent> GetStreamEventContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        CancellationToken cancellationToken = default);

    #region 协议无关基础设施

    /// <summary>
    /// 创建 HttpClient — 完全委托 IProviderDefinition.ConfigureHttpClient 多态配置认证头
    /// 替代原 switch (_providerKind) 分支
    /// </summary>
    private static HttpClient CreateHttpClient(ProviderConfig config)
    {
        var handler = new SocketsHttpHandler();
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };

        var baseUrl = GetBaseUrl(config);
        client.BaseAddress = new Uri(baseUrl);
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        // IProviderDefinition 多态配置认证 Header — 完全消除 switch/if
        // Definition 由 ctor 强制非空，此处无需再校验
        config.Definition!.ConfigureHttpClient(client, config);

        return client;
    }

    /// <summary>Base URL 构建 — 委托 IProviderDefinition.GetBaseUrl，消除 switch</summary>
    protected static string GetBaseUrl(ProviderConfig config)
        => config.Definition?.GetBaseUrl(config)
           ?? throw new InvalidOperationException($"Provider '{config.Provider}' 缺少 IProviderDefinition，无法构建 BaseUrl");

    /// <summary>Chat 端点 — 委托 IProviderDefinition.GetChatEndpoint，消除 switch</summary>
    protected static string GetChatEndpoint(ProviderConfig config)
        => config.Definition?.GetChatEndpoint(config)
           ?? throw new InvalidOperationException($"Provider '{config.Provider}' 缺少 IProviderDefinition，无法获取 Chat 端点");

    /// <summary>
    /// 提取速率限制响应头（协议无关）— OpenAI / Azure / Anthropic 均使用
    /// </summary>
    protected void ExtractRateLimitHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string?>();
        var headerNames = new[]
        {
            "x-ratelimit-limit-requests",
            "x-ratelimit-remaining-requests",
            "x-ratelimit-reset-requests",
            "x-ratelimit-limit-tokens",
            "x-ratelimit-remaining-tokens",
            "x-ratelimit-reset-tokens",
            "retry-after"
        };

        foreach (var name in headerNames)
        {
            if (response.Headers.TryGetValues(name, out var values))
            {
                headers[name] = values.FirstOrDefault();
            }
        }

        if (headers.Count > 0)
        {
            _lastRateLimitHeaders = headers;
        }
    }

    /// <summary>
    /// 将最近一次速率限制头注入流首块 metadata — 协议无关
    /// </summary>
    protected StreamEvent? EnrichWithRateLimitMetadata(StreamEvent msg)
    {
        var headers = _lastRateLimitHeaders;
        if (headers == null) return null;

        var newMetadata = new Dictionary<string, JsonElement>();
        if (msg.Metadata != null)
        {
            foreach (var kvp in msg.Metadata)
            {
                newMetadata[kvp.Key] = kvp.Value;
            }
        }

        foreach (var kvp in headers)
        {
            newMetadata[$"ratelimit_{kvp.Key}"] = JsonElementHelper.FromString(kvp.Value);
        }

        return new StreamEvent(msg.Role, msg.Content, msg.ModelId, newMetadata);
    }

    /// <summary>
    /// 派生类读取最近一次响应的速率限制头（用于流首块 metadata 注入）
    /// </summary>
    protected IReadOnlyDictionary<string, string?>? GetLastRateLimitHeaders() => _lastRateLimitHeaders;

    #endregion

    #region 角色转换共享助手

    /// <summary>字符串 → MessageRole（OpenAI / Azure 流式响应用）</summary>
    internal static MessageRole ConvertRole(string? role)
    {
        var parsed = MessageRoleExtensions.FromValue(role);
        return parsed ?? MessageRole.Assistant;
    }

    /// <summary>MessageRole → 字符串（OpenAI / Azure 请求序列化用）</summary>
    internal static string ConvertRoleToString(MessageRole role)
    {
        return role switch
        {
            MessageRole.System => "system",
            MessageRole.User => "user",
            MessageRole.Assistant => "assistant",
            MessageRole.Tool => "tool",
            _ => "assistant"
        };
    }

    #endregion
}
