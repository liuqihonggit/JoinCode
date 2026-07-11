
namespace MockServer.E2E.Tests.Core;

/// <summary>
/// HTTP 请求解析结果
/// </summary>
public class HttpRequestParseResult
{
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = new();
    public string Body { get; set; } = string.Empty;
    public ChatCompletionRequest? ChatRequest { get; set; }
}

/// <summary>
/// HTTP 请求解析器
/// </summary>
public static class HttpRequestParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 解析 HTTP 请求
    /// </summary>
    public static async Task<HttpRequestParseResult> ParseAsync(StreamReader reader)
    {
        var result = new HttpRequestParseResult();

        // 解析请求行
        var requestLine = await reader.ReadLineAsync().ConfigureAwait(true);
        if (string.IsNullOrEmpty(requestLine))
        {
            throw new InvalidOperationException("Empty request line");
        }

        var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
        {
            throw new InvalidOperationException($"Invalid request line: {requestLine}");
        }

        result.Method = parts[0];
        result.Path = parts[1];
        result.Version = parts[2];

        // 解析请求头
        string? line;
        while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync().ConfigureAwait(true)))
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex > 0)
            {
                var key = line[..colonIndex].Trim();
                var value = line[(colonIndex + 1)..].Trim();
                result.Headers[key] = value;
            }
        }

        // 读取请求体
        if (result.Headers.TryGetValue("Content-Length", out var contentLengthValue)
            && int.TryParse(contentLengthValue, out var contentLength)
            && contentLength > 0)
        {
            var buffer = new char[contentLength];
            var read = await reader.ReadAsync(buffer, 0, contentLength).ConfigureAwait(true);
            result.Body = new string(buffer, 0, read);

            // 反序列化 JSON 请求体
            if (result.Headers.TryGetValue("Content-Type", out var contentType)
                && contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                result.ChatRequest = JsonSerializer.Deserialize<ChatCompletionRequest>(result.Body, JsonOptions);
            }
        }

        return result;
    }

    /// <summary>
    /// 从字符串解析 HTTP 请求（用于测试）
    /// </summary>
    public static async Task<HttpRequestParseResult> ParseFromStringAsync(string request)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(request));
        using var reader = new StreamReader(stream);
        return await ParseAsync(reader).ConfigureAwait(true);
    }

    /// <summary>
    /// 验证 API Key
    /// </summary>
    public static bool ValidateApiKey(HttpRequestParseResult request, string expectedApiKey)
    {
        if (!request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            return false;
        }

        return ValidateBearerToken(authHeader, expectedApiKey);
    }

    /// <summary>
    /// 验证 Bearer Token 格式
    /// </summary>
    public static bool ValidateBearerToken(string authHeader, string expectedApiKey)
    {
        const string bearerPrefix = "Bearer ";

        if (!authHeader.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var token = authHeader[bearerPrefix.Length..];

        // 验证 token 格式以 "sk-" 开头
        if (!token.StartsWith("sk-", StringComparison.Ordinal))
        {
            return false;
        }

        return token == expectedApiKey;
    }

    /// <summary>
    /// 提取 API Key（不包含 Bearer 前缀）
    /// </summary>
    public static string? ExtractApiKey(HttpRequestParseResult request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            return null;
        }

        const string bearerPrefix = "Bearer ";
        if (!authHeader.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return authHeader[bearerPrefix.Length..];
    }
}
