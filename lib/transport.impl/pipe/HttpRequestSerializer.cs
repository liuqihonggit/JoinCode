namespace JoinCode.Transport;

/// <summary>
/// HTTP 请求序列化器
/// </summary>
public static class HttpRequestSerializer {
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
    public static async Task<string> SerializeAsync(HttpRequestMessage request, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new StringBuilder(DefaultBufferSize);

        // 请求行: METHOD /path HTTP/1.1
        builder.Append(BuildRequestLine(request.Method, request.RequestUri?.PathAndQuery));

        // 请求头
        AppendHeaders(builder, request.Headers);

        // 内容头
        if (request.Content != null) {
            AppendHeaders(builder, request.Content.Headers);
        }

        // 空行分隔头和体
        builder.Append(LineTerminator);

        // 请求体
        if (request.Content != null) {
            var body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            builder.Append(body);
        }

        return builder.ToString();
    }

    /// <summary>
    /// 构造 HTTP 请求行 — "METHOD /path?query HTTP/1.1\r\n"。
    /// 纯函数，pathAndQuery 为 null/空时回退到 "/"。
    /// 拒绝 pathAndQuery 中含 \r 或 \n 的输入以防 HTTP 请求拆分/注入攻击。
    /// </summary>
    /// <param name="method">HTTP 方法</param>
    /// <param name="pathAndQuery">路径与查询串（RequestUri.PathAndQuery）</param>
    /// <returns>请求行字符串（含行终止符）</returns>
    /// <exception cref="ArgumentException">pathAndQuery 含 \r 或 \n（CRLF 注入守卫）</exception>
    internal static string BuildRequestLine(HttpMethod method, string? pathAndQuery) {
        var path = string.IsNullOrEmpty(pathAndQuery) ? "/" : pathAndQuery;
        RejectCrlf(path, nameof(pathAndQuery), "路径/查询串");
        return $"{method.Method} {path} {HttpVersion}{LineTerminator}";
    }

    /// <summary>
    /// 追加单行 HTTP 头到 StringBuilder — "key: value\r\n"。
    /// 纯函数（除 StringBuilder 副作用外），对齐 TS 端头部序列化。
    /// 拒绝 key/value 中含 \r 或 \n 的输入以防 HTTP 头注入攻击。
    /// </summary>
    /// <param name="builder">目标 StringBuilder</param>
    /// <param name="key">头名</param>
    /// <param name="value">头值</param>
    /// <exception cref="ArgumentException">key 或 value 含 \r 或 \n（CRLF 注入守卫）</exception>
    internal static void AppendHeaderLine(StringBuilder builder, string key, string value) {
        RejectCrlf(key, nameof(key), "头名");
        RejectCrlf(value, nameof(value), "头值");
        builder.Append(key);
        builder.Append(HeaderSeparator);
        builder.Append(value);
        builder.Append(LineTerminator);
    }

    /// <summary>
    /// CRLF 注入守卫 — 检测 \r 或 \n 并抛 ArgumentException。
    /// 防止 HTTP 请求拆分/头注入攻击：攻击者在参数中嵌入 \r\n 可注入额外请求行或头行。
    /// </summary>
    /// <param name="value">待检测的字符串</param>
    /// <param name="paramName">参数名（用于异常定位）</param>
    /// <param name="paramDescription">参数语义描述（用于错误消息）</param>
    /// <exception cref="ArgumentException">value 含 \r 或 \n</exception>
    private static void RejectCrlf(string value, string paramName, string paramDescription) {
        if (value.Contains('\r') || value.Contains('\n')) {
            throw new ArgumentException(
                $"[TRN-CRLF] {paramDescription} 中含 \\r 或 \\n，拒绝构造 HTTP 请求行/头以防止请求拆分/注入攻击。" +
                $"触发参数: {paramName}。正确写法: {paramDescription} 不得含 CR/LF 字符，" +
                $"如需传递换行内容请先编码（如 Base64）或使用请求体。",
                paramName);
        }
    }

    /// <summary>
    /// 批量追加 HTTP 头到 StringBuilder — 遍历 HttpHeaders 所有键值对。
    /// 纯函数（除 StringBuilder 副作用外），处理多值头。
    /// </summary>
    /// <param name="builder">目标 StringBuilder</param>
    /// <param name="headers">HTTP 头集合</param>
    internal static void AppendHeaders(StringBuilder builder, HttpHeaders headers) {
        foreach (var header in headers) {
            foreach (var value in header.Value) {
                AppendHeaderLine(builder, header.Key, value);
            }
        }
    }

    /// <summary>
    /// 将 HTTP 响应字符串解析为 HttpResponseMessage
    /// </summary>
    /// <param name="responseText">HTTP 响应字符串</param>
    /// <returns>HTTP 响应消息</returns>
    public static HttpResponseMessage Deserialize(string responseText) {
        ArgumentException.ThrowIfNullOrEmpty(responseText);

        using var reader = new StringReader(responseText);
        var response = new HttpResponseMessage();

        // 解析状态行
        var statusLine = reader.ReadLine();
        if (string.IsNullOrEmpty(statusLine)) {
            throw new InvalidOperationException("[TRN009] 无效的 HTTP 响应: 空状态行");
        }

        ParseStatusLine(statusLine, response);

        // 解析响应头
        var contentHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? line;
        while (!string.IsNullOrEmpty(line = reader.ReadLine())) {
            ParseHeaderLine(line, response, contentHeaders);
        }

        // 读取响应体
        var body = reader.ReadToEnd();
        if (!string.IsNullOrEmpty(body)) {
            response.Content = new StringContent(body);

            // 将内容头应用到内容
            foreach (var header in contentHeaders) {
                response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return response;
    }

    internal static void ParseStatusLine(string statusLine, HttpResponseMessage response) {
        var parts = statusLine.Split(' ', 3);
        if (parts.Length < 2) {
            throw new InvalidOperationException($"[TRN014] 无效的 HTTP 状态行: {statusLine}");
        }

        // 解析状态码
        if (int.TryParse(parts[1], out var statusCode)) {
            response.StatusCode = (System.Net.HttpStatusCode)statusCode;
        } else {
            throw new InvalidOperationException($"[TRN015] 无效的 HTTP 状态码: {parts[1]}");
        }

        // 解析原因短语（可选）
        if (parts.Length > 2) {
            response.ReasonPhrase = parts[2];
        }
    }

    internal static void ParseHeaderLine(string line, HttpResponseMessage response, Dictionary<string, string> contentHeaders) {
        var separatorIndex = line.IndexOf(HeaderSeparator, StringComparison.Ordinal);
        if (separatorIndex <= 0) {
            return;
        }

        var key = line[..separatorIndex];
        var value = line[(separatorIndex + HeaderSeparator.Length)..];

        // 内容相关的头需要特殊处理
        if (IsContentHeader(key)) {
            contentHeaders[key] = value;
        } else {
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

    internal static bool IsContentHeader(string headerName) {
        return ContentHeaders.Contains(headerName);
    }
}