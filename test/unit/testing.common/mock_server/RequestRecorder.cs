
namespace Testing.Common.MockServer;

/// <summary>
/// 记录请求的角色类型
/// </summary>
public static class MessageRoles {
    public const string System = "system";
    public const string User = "user";
    public const string Assistant = "assistant";
}

/// <summary>
/// HTTP 请求信息
/// </summary>
public sealed class HttpRequestInfo {
    /// <summary>获取 HTTP 方法</summary>
    public string Method { get; init; } = string.Empty;
    /// <summary>获取请求路径</summary>
    public string Path { get; init; } = string.Empty;
    /// <summary>获取请求头字典</summary>
    public Dictionary<string, string> Headers { get; init; } = new();
    /// <summary>获取请求体</summary>
    public string Body { get; init; } = string.Empty;
}

/// <summary>
/// 聊天完成请求
/// </summary>
public sealed class ChatCompletionRequest {
    /// <summary>获取模型名称</summary>
    public string Model { get; init; } = string.Empty;
    /// <summary>获取消息列表</summary>
    public List<ApiMessage> Messages { get; init; } = new();
    /// <summary>获取采样温度</summary>
    public double? Temperature { get; init; }
    /// <summary>获取最大令牌数</summary>
    public int? MaxTokens { get; init; }
    /// <summary>获取是否流式输出</summary>
    public bool? Stream { get; init; }
}

/// <summary>
/// 聊天消息
/// </summary>
public sealed class ApiMessage {
    /// <summary>获取角色类型</summary>
    public string Role { get; init; } = string.Empty;
    /// <summary>获取文本内容</summary>
    public string Content { get; init; } = string.Empty;
}

/// <summary>
/// 记录的 HTTP 请求
/// </summary>
public sealed class RecordedRequest {
    /// <summary>获取请求时间戳</summary>
    public DateTimeOffset Timestamp { get; init; }
    /// <summary>获取 HTTP 方法</summary>
    public string Method { get; init; } = string.Empty;
    /// <summary>获取请求路径</summary>
    public string Path { get; init; } = string.Empty;
    /// <summary>获取请求头字典</summary>
    public Dictionary<string, string> Headers { get; init; } = new();
    /// <summary>获取请求体</summary>
    public string Body { get; init; } = string.Empty;
    /// <summary>获取解析后的请求对象</summary>
    public ChatCompletionRequest? ParsedRequest { get; init; }

    /// <summary>
    /// 获取系统提示词列表
    /// </summary>
    public IReadOnlyList<string> GetSystemPrompts() =>
        ParsedRequest?.Messages
            .Where(m => m.Role == MessageRoles.System)
            .Select(m => m.Content)
            .ToList() ?? new List<string>();

    /// <summary>
    /// 获取用户提示词列表
    /// </summary>
    public IReadOnlyList<string> GetUserPrompts() =>
        ParsedRequest?.Messages
            .Where(m => m.Role == MessageRoles.User)
            .Select(m => m.Content)
            .ToList() ?? new List<string>();
}

/// <summary>
/// HTTP 请求记录器
/// 用于测试场景记录和验证接收到的请求
/// </summary>
public sealed class RequestRecorder {
    private readonly ConcurrentQueue<RecordedRequest> _requests = new();

    /// <summary>
    /// 记录请求信息
    /// </summary>
    public void Record(HttpRequestInfo requestInfo) {
        ArgumentNullException.ThrowIfNull(requestInfo);

        ChatCompletionRequest? parsedRequest = null;

        if (!string.IsNullOrWhiteSpace(requestInfo.Body)) {
            try {
                parsedRequest = JsonSerializer.Deserialize(requestInfo.Body, MockServerJsonContext.Default.ChatCompletionRequest);
            } catch (JsonException ex) {
                // 解析失败时保留原始 body，parsedRequest 为 null
                System.Diagnostics.Trace.WriteLine($"JSON解析请求体失败: {ex.Message}");
            }
        }

        var recorded = new RecordedRequest {
            Timestamp = DateTimeOffset.UtcNow,
            Method = requestInfo.Method,
            Path = requestInfo.Path,
            Headers = requestInfo.Headers,
            Body = requestInfo.Body,
            ParsedRequest = parsedRequest
        };

        _requests.Enqueue(recorded);
    }

    /// <summary>
    /// 异步记录请求信息
    /// </summary>
    public Task RecordAsync(HttpRequestInfo requestInfo, CancellationToken ct = default) {
        Record(requestInfo);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 从 JSON body 记录请求
    /// </summary>
    public void RecordJson(string method, string path, string jsonBody) {
        ArgumentException.ThrowIfNullOrEmpty(method);
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(jsonBody);
        Record(new HttpRequestInfo {
            Method = method,
            Path = path,
            Body = jsonBody,
            Headers = new Dictionary<string, string>()
        });
    }

    /// <summary>
    /// 获取所有记录的请求
    /// </summary>
    public IReadOnlyList<RecordedRequest> GetRequests() => _requests.ToList();

    /// <summary>
    /// 获取最后一条记录
    /// </summary>
    public RecordedRequest? GetLastRequest() => _requests.LastOrDefault();

    /// <summary>
    /// 清空所有记录
    /// </summary>
    public void Clear() => _requests.Clear();

    /// <summary>
    /// 获取记录数量
    /// </summary>
    public int Count => _requests.Count;
}