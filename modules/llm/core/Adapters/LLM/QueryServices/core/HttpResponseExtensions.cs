namespace Api.LLM.QueryServices;

/// <summary>
/// HTTP 响应扩展方法 — 提供用户友好的错误消息（包含状态码和响应体内容）。
/// 替代 <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/>，避免技术化的 "Response status code does not indicate success" 消息。
/// </summary>
internal static class HttpResponseExtensions {
    /// <summary>
    /// 确保 HTTP 响应成功，失败时抛出包含状态码和响应体内容的 <see cref="HttpRequestException"/>。
    /// </summary>
    /// <param name="response">HTTP 响应消息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// 错误消息格式："HTTP 429 TooManyRequests: {响应体前200字符}"，
    /// 让用户一眼看出是限流(429)、服务器错误(500)还是其他问题。
    /// </remarks>
    public static async Task EnsureSuccessWithBodyAsync(this HttpResponseMessage response, CancellationToken cancellationToken) {
        if (response.IsSuccessStatusCode) return;

        var body = string.Empty;
        try {
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            Console.Error.WriteLine($"[HttpResponseExtensions] 读取错误响应体失败: {ex.Message}");
        }

        var message = ExtractErrorMessage(body);
        throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.StatusCode}: {message}", null, response.StatusCode);
    }

    /// <summary>
    /// 从错误响应体提取用户友好消息。优先解析 OpenAI 格式 <c>{"error":{"message":"..."}}</c>，
    /// 失败则返回原始 body 前 200 字符预览。
    /// </summary>
    private static string ExtractErrorMessage(string body) {
        if (string.IsNullOrEmpty(body)) return "(空响应体)";
        try {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error", out var errEl)) {
                if (errEl.ValueKind == System.Text.Json.JsonValueKind.String)
                    return errEl.GetString() ?? body;
                if (errEl.ValueKind == System.Text.Json.JsonValueKind.Object
                    && errEl.TryGetProperty("message", out var msgEl))
                    return msgEl.GetString() ?? body;
            }
        } catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) {
            Console.Error.WriteLine($"[HttpResponseExtensions] 错误响应体非 JSON 格式，回退到原始文本: {body[..Math.Min(80, body.Length)]}");
        }
        return body.Length > 200 ? body[..200] + "..." : body;
    }
}
