namespace JoinCode.Transport.Bridge;

/// <summary>
/// 统一 OAuth 401 重试逻辑 — 对齐 TS 端 bridgeApi.ts:106-139 withOAuthRetry
/// 401 → 调用 onAuth401 刷新 token → 用新 token 重试一次 → 仍 401 返回错误
/// </summary>
public static class BridgeOAuthRetry
{
    /// <summary>
    /// 执行 OAuth 401 重试 — 通用 HTTP 请求重试
    /// 对齐 TS 端:
    /// <code>
    /// async function withOAuthRetry(fn, context):
    ///   const response = await fn(accessToken)
    ///   if (response.status !== 401) return response
    ///   const refreshed = await deps.onAuth401(accessToken)
    ///   if (refreshed) { retry with newToken }
    ///   return response
    /// </code>
    /// </summary>
    /// <param name="httpClient">HTTP 客户端</param>
    /// <param name="createRequest">回调：使用给定 token 创建 HttpRequestMessage</param>
    /// <param name="context">请求上下文描述（用于日志）</param>
    /// <param name="getAccessToken">获取当前访问令牌</param>
    /// <param name="onAuth401">401 回调 — 刷新 token，返回 true 表示成功</param>
    /// <param name="timeoutMs">超时（毫秒）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>响应消息（由调用方检查状态码）</returns>
    public static async Task<HttpResponseMessage?> ExecuteWithOAuthRetryAsync(
        HttpClient httpClient,
        Func<string, HttpRequestMessage> createRequest,
        string context,
        Func<string?> getAccessToken,
        Func<string, Task<bool>>? onAuth401,
        int timeoutMs,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(createRequest);
        ArgumentNullException.ThrowIfNull(getAccessToken);

        // 1. 使用当前 token 发起请求
        var accessToken = getAccessToken();
        if (string.IsNullOrEmpty(accessToken))
        {
            return null;
        }

        var request = createRequest(accessToken);

        using var cts = TimeoutHelper.CreateLinkedTimeout(ct, TimeSpan.FromMilliseconds(timeoutMs));

        HttpResponseMessage? response;
        try
        {
            response = await httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
        }
        catch
        {
            // 网络错误 — 不重试，直接返回 null
            return null;
        }

        // 2. 非 401 — 直接返回
        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
        {
            return response;
        }

        // 3. 401 但无刷新处理器
        if (onAuth401 is null)
        {
            return response;
        }

        // 4. 尝试 token 刷新 — 对齐 TS 端: debug(`attempting token refresh`)
        var refreshed = await onAuth401(accessToken).ConfigureAwait(false);

        // 5. 刷新成功 — 用新 token 重试一次
        if (refreshed)
        {
            var newToken = getAccessToken();
            if (string.IsNullOrEmpty(newToken))
            {
                return response;
            }

            var retryRequest = createRequest(newToken);
            using var retryCts = TimeoutHelper.CreateLinkedTimeout(ct, TimeSpan.FromMilliseconds(timeoutMs));

            try
            {
                var retryResponse = await httpClient.SendAsync(retryRequest, retryCts.Token).ConfigureAwait(false);

                // 重试仍 401 — 返回原始 401 响应
                if (retryResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    return response;
                }

                return retryResponse;
            }
            catch
            {
                // 重试网络错误 — 返回原始 401 响应
                return response;
            }
        }

        // 6. 刷新失败 — 返回 401 响应
        return response;
    }

    /// <summary>
    /// 执行带 OAuth 重试的 JSON 请求 — 通用 JSON API 调用
    /// 简化版：直接返回反序列化的响应对象
    /// </summary>
    /// <typeparam name="TResponse">响应类型</typeparam>
    /// <param name="httpClient">HTTP 客户端</param>
    /// <param name="createRequest">回调：使用给定 token 创建 HttpRequestMessage</param>
    /// <param name="context">请求上下文描述</param>
    /// <param name="getAccessToken">获取当前访问令牌</param>
    /// <param name="onAuth401">401 回调</param>
    /// <param name="timeoutMs">超时</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>反序列化的响应对象，失败返回 null</returns>
    public static async Task<TResponse?> ExecuteJsonWithOAuthRetryAsync<TResponse>(
        HttpClient httpClient,
        Func<string, HttpRequestMessage> createRequest,
        string context,
        Func<string?> getAccessToken,
        Func<string, Task<bool>>? onAuth401,
        int timeoutMs,
        CancellationToken ct = default) where TResponse : class
    {
        var response = await ExecuteWithOAuthRetryAsync(
            httpClient, createRequest, context, getAccessToken, onAuth401, timeoutMs, ct).ConfigureAwait(false);

        if (response is null)
        {
            return null;
        }

        var statusCode = (int)response.StatusCode;

        // 非成功非 4xx — 尝试反序列化
        if (statusCode >= 500)
        {
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        // 409 = 幂等 — 返回 null
        if (statusCode == 409)
        {
            return null;
        }

        // AOT 兼容：使用 JsonDocument 解析为 JsonElement 再泛型化
        using var doc = System.Text.Json.JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        // 构建结果对象 — 简化处理：使用 JsonElement 作为通用结果
        // 调用方可自行处理
        return (TResponse)(object)root.Clone();
    }
}
