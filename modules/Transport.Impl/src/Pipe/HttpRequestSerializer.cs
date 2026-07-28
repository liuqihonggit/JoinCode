namespace JoinCode.Transport;

/// <summary>
/// HTTP 请求序列化器
/// </summary>
public static class HttpRequestSerializer
{
    private const string HttpVersion = "HTTP/1.1";
    private const string HeaderSeparator = ": ";
    private const string LineTerminator = "\r\n";
    private const int DefaultBufferSize = 4096;

    /// <summary>
    /// 将 HttpRequestMessage 序列化为 HTTP 格式字符串
    /// </summary>
    /// <param name="request">HTTP 请求消息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>HTTP 格式字符串</returns>
    public static async Task<string> SerializeAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new StringBuilder(DefaultBufferSize);

        // 请求行: METHOD /path HTTP/1.1
        var requestUri = request.RequestUri?.PathAndQuery ?? "/";
        builder.Append(request.Method.Method);
        builder.Append(' ');
        builder.Append(requestUri);
        builder.Append(' ');
        builder.Append(HttpVersion);
        builder.Append(LineTerminator);

        // 请求头
        foreach (var header in request.Headers)
        {
            foreach (var value in header.Value)
            {
                builder.Append(header.Key);
                builder.Append(HeaderSeparator);
                builder.Append(value);
                builder.Append(LineTerminator);
            }
        }

        // 内容头
        if (request.Content != null)
        {
            foreach (var header in request.Content.Headers)
            {
                foreach (var value in header.Value)
                {
                    builder.Append(header.Key);
                    builder.Append(HeaderSeparator);
                    builder.Append(value);
                    builder.Append(LineTerminator);
                }
            }
        }

        // 空行分隔头和体
        builder.Append(LineTerminator);

        // 请求体
        if (request.Content != null)
        {
            var body = await request.Content.ReadAsStringAsync(cancellationToken);
            builder.Append(body);
        }

        return builder.ToString();
    }

    /// <summary>
    /// 将 HTTP 响应字符串解析为 HttpResponseMessage
    /// </summary>
    /// <param name="responseText">HTTP 响应字符串</param>
    /// <returns>HTTP 响应消息</returns>
    public static HttpResponseMessage Deserialize(string responseText)
    {
        ArgumentException.ThrowIfNullOrEmpty(responseText);

        using var reader = new StringReader(responseText);
        var response = new HttpResponseMessage();

        // 解析状态行
        var statusLine = reader.ReadLine();
        if (string.IsNullOrEmpty(statusLine))
        {
            throw new InvalidOperationException("无效的 HTTP 响应: 空状态行");
        }

        ParseStatusLine(statusLine, response);

        // 解析响应头
        var contentHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? line;
        while (!string.IsNullOrEmpty(line = reader.ReadLine()))
        {
            ParseHeaderLine(line, response, contentHeaders);
        }

        // 读取响应体
        var body = reader.ReadToEnd();
        if (!string.IsNullOrEmpty(body))
        {
            response.Content = new StringContent(body);

            // 将内容头应用到内容
            foreach (var header in contentHeaders)
            {
                response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return response;
    }

    private static void ParseStatusLine(string statusLine, HttpResponseMessage response)
    {
        var parts = statusLine.Split(' ', 3);
        if (parts.Length < 2)
        {
            throw new InvalidOperationException($"无效的 HTTP 状态行: {statusLine}");
        }

        // 解析状态码
        if (int.TryParse(parts[1], out var statusCode))
        {
            response.StatusCode = (System.Net.HttpStatusCode)statusCode;
        }
        else
        {
            throw new InvalidOperationException($"无效的 HTTP 状态码: {parts[1]}");
        }

        // 解析原因短语（可选）
        if (parts.Length > 2)
        {
            response.ReasonPhrase = parts[2];
        }
    }

    private static void ParseHeaderLine(string line, HttpResponseMessage response, Dictionary<string, string> contentHeaders)
    {
        var separatorIndex = line.IndexOf(HeaderSeparator, StringComparison.Ordinal);
        if (separatorIndex <= 0)
        {
            return;
        }

        var key = line[..separatorIndex];
        var value = line[(separatorIndex + HeaderSeparator.Length)..];

        // 内容相关的头需要特殊处理
        if (IsContentHeader(key))
        {
            contentHeaders[key] = value;
        }
        else
        {
            response.Headers.TryAddWithoutValidation(key, value);
        }
    }

    private static readonly FrozenSet<string> ContentHeaders = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "Content-Type",
        "Content-Length",
        "Content-Encoding",
        "Content-Language",
        "Content-Location",
        "Content-MD5",
        "Content-Range",
        "Expires",
        "Last-Modified");

    private static bool IsContentHeader(string headerName)
    {
        return ContentHeaders.Contains(headerName);
    }
}
