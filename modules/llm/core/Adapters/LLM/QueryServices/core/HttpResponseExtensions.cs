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

        var preview = body.Length > 200 ? body[..200] + "..." : body;
        throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.StatusCode}: {preview}");
    }
}
