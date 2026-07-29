
namespace Core.Bridge;

/// <summary>
/// 远程凭证 — 对齐 TS 端 codeSessionApi.ts RemoteCredentials
/// POST /v1/code/sessions/{id}/bridge 返回的 worker 凭证
/// </summary>
public sealed class BridgeRemoteCredentials
{
    /// <summary>Worker JWT 令牌</summary>
    [JsonPropertyName("worker_jwt")]
    public required string WorkerJwt { get; init; }

    /// <summary>API 基础 URL</summary>
    [JsonPropertyName("api_base_url")]
    public required string ApiBaseUrl { get; init; }

    /// <summary>过期时间（秒）</summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    /// <summary>Worker epoch（protojson int64 可能为字符串）</summary>
    [JsonPropertyName("worker_epoch")]
    public JsonElement WorkerEpochRaw { get; init; }

    /// <summary>解析后的 Worker epoch — 对齐 TS 端 protojson int64 字符串兼容</summary>
    public long WorkerEpoch => ParseWorkerEpoch(WorkerEpochRaw);

    /// <summary>
    /// 解析 worker_epoch — 对齐 TS 端 protojson int64 字符串兼容
    /// protojson 将 int64 序列化为字符串避免 JS 精度丢失
    /// </summary>
    public static long ParseWorkerEpoch(JsonElement epoch)
    {
        if (epoch.ValueKind == JsonValueKind.String)
        {
            if (!long.TryParse(epoch.GetString(), out var result))
            {
                throw new InvalidOperationException($"Invalid worker_epoch string: {epoch}");
            }
            return result;
        }

        if (epoch.ValueKind == JsonValueKind.Number)
        {
            return epoch.GetInt64();
        }

        throw new InvalidOperationException($"Invalid worker_epoch type: {epoch.ValueKind}");
    }

    /// <summary>重载：从字符串解析</summary>
    public static long ParseWorkerEpoch(string epoch)
    {
        if (!long.TryParse(epoch, out var result))
        {
            throw new InvalidOperationException($"Invalid worker_epoch string: {epoch}");
        }
        return result;
    }

    /// <summary>重载：从 double 解析（JSON 数字）</summary>
    public static long ParseWorkerEpoch(double epoch)
    {
        return (long)epoch;
    }
}

/// <summary>
/// CCR v2 代码会话 API — 对齐 TS 端 codeSessionApi.ts
/// 独立于 remoteBridgeCore.ts，让 SDK /bridge 子路径可以导出这些函数
/// </summary>
public static class BridgeCodeSessionApi
{
    /// <summary>
    /// 创建代码会话 — 对齐 TS 端 createCodeSession
    /// POST /v1/code/sessions → cse_* 格式的会话 ID
    /// </summary>
    public static async Task<string?> CreateCodeSessionAsync(
        string baseUrl,
        string accessToken,
        string title,
        int timeoutMs,
        HttpClient httpClient,
        string[]? tags = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        var url = $"{baseUrl.TrimEnd('/')}/v1/code/sessions";

        // bridge: {} 是 oneof runner 的正信号 — 省略会导致 400 错误
        // BridgeRunner 当前为空消息，是未来 bridge 专属选项的占位符
        var body = new StringBuilder("{\"title\":");
        body.Append(JsonEncode(title));
        body.Append(",\"bridge\":{}");
        if (tags is { Length: > 0 })
        {
            body.Append(",\"tags\":[");
            for (var i = 0; i < tags.Length; i++)
            {
                if (i > 0) body.Append(',');
                body.Append(JsonEncode(tags[i]));
            }
            body.Append(']');
        }
        body.Append('}');

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToString(), System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("anthropic-version", "2023-06-01");

        using var cts = TimeoutHelper.CreateLinkedTimeout(ct, TimeSpan.FromMilliseconds(timeoutMs));

        try
        {
            using var response = await httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);

            // 对齐 TS 端：仅接受 200/201，非 5xx 优雅返回 null
            if (response.StatusCode != System.Net.HttpStatusCode.OK &&
                response.StatusCode != System.Net.HttpStatusCode.Created)
            {
                return null;
            }

            var responseBody = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var parsed = JsonDocument.Parse(responseBody);
            var root = parsed.RootElement;

            // 对齐 TS 端：响应路径为 data.session.id，且必须以 cse_ 开头
            if (root.TryGetProperty("session", out var sessionProp) &&
                sessionProp.ValueKind == JsonValueKind.Object &&
                sessionProp.TryGetProperty("id", out var idProp) &&
                idProp.ValueKind == JsonValueKind.String)
            {
                var sessionId = idProp.GetString();
                if (sessionId is not null && sessionId.StartsWith("cse_", StringComparison.Ordinal))
                {
                    return sessionId;
                }
            }

            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null; // 超时
        }
    }

    /// <summary>
    /// 获取桥接凭证 — 对齐 TS 端 fetchRemoteCredentials
    /// POST /v1/code/sessions/{id}/bridge → worker_jwt + api_base_url + expires_in + worker_epoch
    /// </summary>
    public static async Task<BridgeRemoteCredentials?> FetchRemoteCredentialsAsync(
        string sessionId,
        string baseUrl,
        string accessToken,
        int timeoutMs,
        HttpClient httpClient,
        string? trustedDeviceToken = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        var url = $"{baseUrl.TrimEnd('/')}/v1/code/sessions/{sessionId}/bridge";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("anthropic-version", "2023-06-01");

        if (trustedDeviceToken is not null)
        {
            request.Headers.Add("x-trusted-device-token", trustedDeviceToken);
        }

        using var cts = TimeoutHelper.CreateLinkedTimeout(ct, TimeSpan.FromMilliseconds(timeoutMs));

        try
        {
            using var response = await httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);

            // 对齐 TS 端：仅接受 200
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                return null;
            }

            var responseBody = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var parsed = JsonDocument.Parse(responseBody);
            var root = parsed.RootElement;

            // 对齐 TS 端：逐字段严格校验类型
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!root.TryGetProperty("worker_jwt", out var jwtProp) || jwtProp.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("api_base_url", out var urlProp) || urlProp.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("expires_in", out var expiresProp) || expiresProp.ValueKind != JsonValueKind.Number ||
                !root.TryGetProperty("worker_epoch", out var epochProp))
            {
                return null;
            }

            return new BridgeRemoteCredentials
            {
                WorkerJwt = jwtProp.GetString()!,
                ApiBaseUrl = urlProp.GetString()!,
                ExpiresIn = expiresProp.GetInt32(),
                WorkerEpochRaw = epochProp.Clone(),
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null; // 超时
        }
    }

    /// <summary>JSON 字符串编码</summary>
    private static string JsonEncode(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
