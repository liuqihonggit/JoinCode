
namespace MockServer.E2E.Tests.Core;

/// <summary>
/// HTTP 响应构建配置
/// </summary>
public class HttpResponseConfig
{
    public int StatusCode { get; set; } = 200;
    public string StatusText { get; set; } = "OK";
    public Dictionary<string, string> Headers { get; set; } = new();
    public string Body { get; set; } = string.Empty;

    public HttpResponseConfig()
    {
        Headers["Content-Type"] = "application/json";
        Headers["Connection"] = "keep-alive";
    }
}

/// <summary>
/// HTTP 响应构建器
/// </summary>
internal static class HttpResponseBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 构建标准 Chat Completion 响应
    /// </summary>
    public static string BuildResponse(ChatCompletionResponse response)
    {
        var json = JsonSerializer.Serialize(response, JsonOptions);
        return BuildHttpResponse(200, "OK", json);
    }

    /// <summary>
    /// 构建流式响应块 (SSE 格式)
    /// </summary>
    public static string BuildStreamChunk(ChatCompletionChunk chunk)
    {
        var json = JsonSerializer.Serialize(chunk, JsonOptions);
        return $"data: {json}\n\n";
    }

    /// <summary>
    /// 构建流式响应结束标记
    /// </summary>
    public static string BuildStreamEnd()
    {
        return "data: [DONE]\n\n";
    }

    /// <summary>
    /// 构建错误响应
    /// </summary>
    public static string BuildErrorResponse(int statusCode, string errorMessage, string? errorType = null)
    {
        var errorResponse = new
        {
            error = new
            {
                message = errorMessage,
                type = errorType ?? "invalid_request_error",
                code = GetErrorCode(statusCode)
            }
        };

        var json = JsonSerializer.Serialize(errorResponse, JsonOptions);
        var statusText = GetStatusText(statusCode);
        return BuildHttpResponse(statusCode, statusText, json);
    }

    /// <summary>
    /// 构建完整的 HTTP 响应字符串
    /// </summary>
    private static string BuildHttpResponse(int statusCode, string statusText, string body)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"HTTP/1.1 {statusCode} {statusText}");
        sb.AppendLine($"Content-Type: application/json");
        sb.AppendLine($"Content-Length: {Encoding.UTF8.GetByteCount(body)}");
        sb.AppendLine($"Connection: keep-alive");
        sb.AppendLine();
        sb.Append(body);
        return sb.ToString();
    }

    /// <summary>
    /// 构建 SSE 流式响应头
    /// </summary>
    public static string BuildStreamHeaders()
    {
        var sb = new StringBuilder();
        sb.AppendLine("HTTP/1.1 200 OK");
        sb.AppendLine("Content-Type: text/event-stream");
        sb.AppendLine("Cache-Control: no-cache");
        sb.AppendLine("Connection: keep-alive");
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>
    /// 构建自定义配置响应
    /// </summary>
    public static string BuildCustomResponse(HttpResponseConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var sb = new StringBuilder();
        sb.AppendLine($"HTTP/1.1 {config.StatusCode} {config.StatusText}");

        foreach (var header in config.Headers)
        {
            sb.AppendLine($"{header.Key}: {header.Value}");
        }

        if (!string.IsNullOrEmpty(config.Body))
        {
            var contentLength = Encoding.UTF8.GetByteCount(config.Body);
            sb.AppendLine($"Content-Length: {contentLength}");
        }

        sb.AppendLine();
        sb.Append(config.Body);
        return sb.ToString();
    }

    /// <summary>
    /// 获取状态码对应的状态文本
    /// </summary>
    private static string GetStatusText(int statusCode)
    {
        return StatusCodeTexts.TryGetValue(statusCode, out var text)
            ? text
            : "Unknown";
    }

    /// <summary>
    /// 获取错误代码
    /// </summary>
    private static string? GetErrorCode(int statusCode)
    {
        return ErrorCodes.TryGetValue(statusCode, out var code)
            ? code
            : null;
    }

    private static readonly Dictionary<int, string> StatusCodeTexts = new()
    {
        [200] = "OK",
        [201] = "Created",
        [400] = "Bad Request",
        [401] = "Unauthorized",
        [403] = "Forbidden",
        [404] = "Not Found",
        [429] = "Too Many Requests",
        [500] = "Internal Server Error",
        [502] = "Bad Gateway",
        [503] = "Service Unavailable"
    };

    private static readonly Dictionary<int, string> ErrorCodes = new()
    {
        [400] = "invalid_request_error",
        [401] = "unauthorized",
        [403] = "forbidden",
        [404] = "not_found",
        [429] = "rate_limit_exceeded",
        [500] = "internal_error",
        [502] = "bad_gateway",
        [503] = "service_unavailable"
    };
}
