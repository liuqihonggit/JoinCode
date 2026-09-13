
namespace Core.Bridge;

#region CCR WorkSecret 数据模型 — 对齐 TS 端 types.ts WorkSecret

/// <summary>
/// CCR 工作密钥 — 对齐 TS 端 WorkSecret
/// base64url 解码后的 JSON 结构，包含会话入口令牌和 API 基础 URL
/// </summary>
public sealed class BridgeWorkSecret
{
    /// <summary>工作密钥版本</summary>
    [JsonPropertyName("version")]
    public int Version { get; init; }

    /// <summary>会话入口令牌</summary>
    [JsonPropertyName("session_ingress_token")]
    public required string SessionIngressToken { get; init; }

    /// <summary>API 基础 URL</summary>
    [JsonPropertyName("api_base_url")]
    public required string ApiBaseUrl { get; init; }

    /// <summary>工作密钥来源列表</summary>
    [JsonPropertyName("sources")]
    public List<BridgeWorkSecretSource> Sources { get; init; } = [];

    /// <summary>认证信息列表</summary>
    [JsonPropertyName("auth")]
    public List<BridgeWorkSecretAuth> Auth { get; init; } = [];

    /// <summary>CLI 参数字典</summary>
    [JsonPropertyName(ClaudeCompatConstants.JsonClaudeCodeArgs)]
    public Dictionary<string, string> CliArgs { get; init; } = [];

    /// <summary>MCP 配置（原始 JSON 元素）</summary>
    [JsonPropertyName("mcp_config")]
    public JsonElement? McpConfig { get; init; }

    /// <summary>环境变量字典</summary>
    [JsonPropertyName("environment_variables")]
    public Dictionary<string, string> EnvironmentVariables { get; init; } = [];

    /// <summary>是否使用 code sessions 模式</summary>
    [JsonPropertyName("use_code_sessions")]
    public bool UseCodeSessions { get; init; }
}

/// <summary>工作密钥来源</summary>
public sealed class BridgeWorkSecretSource
{
    /// <summary>来源类型</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>Git 信息</summary>
    [JsonPropertyName("git_info")]
    public BridgeWorkSecretGitInfo? GitInfo { get; init; }
}

/// <summary>Git 信息</summary>
public sealed class BridgeWorkSecretGitInfo
{
    /// <summary>Git 类型</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>仓库名</summary>
    [JsonPropertyName("repo")]
    public string? Repo { get; init; }

    /// <summary>引用（分支/tag/commit）</summary>
    [JsonPropertyName("ref")]
    public string? Ref { get; init; }

    /// <summary>访问令牌</summary>
    [JsonPropertyName("token")]
    public string? Token { get; init; }
}

/// <summary>工作密钥认证信息</summary>
public sealed class BridgeWorkSecretAuth
{
    /// <summary>认证类型</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>认证令牌</summary>
    [JsonPropertyName("token")]
    public string? Token { get; init; }
}

/// <summary>
/// Worker 注册响应 — 对齐 TS 端 registerWorker 返回值
/// </summary>
public sealed class BridgeWorkerRegisterResponse
{
    /// <summary>Worker epoch（protojson 序列化的 int64，可能是字符串或数字）</summary>
    [JsonPropertyName("worker_epoch")]
    public JsonElement WorkerEpoch { get; init; }
}

#endregion

/// <summary>
/// WorkSecret 解码 + URL 构建 — 对齐 TS 端 workSecret.ts
/// </summary>
public static class BridgeWorkSecretDecoder
{
    /// <summary>
    /// 解码 base64url 编码的工作密钥 — 对齐 TS 端 decodeWorkSecret
    /// </summary>
    /// <param name="secret">base64url 编码的工作密钥字符串</param>
    /// <returns>解码后的 BridgeWorkSecret 实例</returns>
    public static BridgeWorkSecret DecodeWorkSecret(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        // base64url → base64
        var base64 = secret
            .Replace('-', '+')
            .Replace('_', '/');

        // 补齐 padding
        var padding = base64.Length % 4;
        if (padding > 0)
        {
            base64 += new string('=', 4 - padding);
        }

        var jsonBytes = Convert.FromBase64String(base64);
        var json = System.Text.Encoding.UTF8.GetString(jsonBytes);

        var parsed = JsonSerializer.Deserialize<BridgeWorkSecret>(json, BridgeJsonContext.Default.BridgeWorkSecret);

        if (parsed is null)
        {
            throw new InvalidOperationException("Invalid work secret: failed to parse JSON");
        }

        if (parsed.Version != 1)
        {
            throw new InvalidOperationException($"Unsupported work secret version: {parsed.Version}");
        }

        if (string.IsNullOrEmpty(parsed.SessionIngressToken))
        {
            throw new InvalidOperationException("Invalid work secret: missing or empty session_ingress_token");
        }

        if (string.IsNullOrEmpty(parsed.ApiBaseUrl))
        {
            throw new InvalidOperationException("Invalid work secret: missing api_base_url");
        }

        return parsed;
    }

    /// <summary>
    /// 构建 WebSocket SDK URL — 对齐 TS 端 buildSdkUrl
    /// localhost 使用 v2/ws（直连 session-ingress），生产使用 v1/wss（Envoy 重写）
    /// </summary>
    /// <param name="apiBaseUrl">API 基础 URL</param>
    /// <param name="sessionId">会话 ID</param>
    /// <returns>WebSocket SDK URL</returns>
    public static string BuildSdkUrl(string apiBaseUrl, string sessionId)
    {
        var isLocalhost = apiBaseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                       || apiBaseUrl.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase);
        var protocol = isLocalhost ? "ws" : "wss";
        var version = isLocalhost ? "v2" : "v1";

        // 去除协议前缀和尾部斜杠
        var host = apiBaseUrl.AsSpan();
        if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            host = host.Slice(8);
        }
        else if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            host = host.Slice(7);
        }

        var hostStr = host.TrimEnd('/').ToString();

        return $"{protocol}://{hostStr}/{version}/session_ingress/ws/{sessionId}";
    }

    /// <summary>
    /// 构建 CCR v2 会话 URL — 对齐 TS 端 buildCCRv2SdkUrl
    /// 返回 HTTP(S) URL（非 ws://），指向 /v1/code/sessions/{id}
    /// </summary>
    /// <param name="apiBaseUrl">API 基础 URL</param>
    /// <param name="sessionId">会话 ID</param>
    /// <returns>CCR v2 会话 HTTP(S) URL</returns>
    public static string BuildCCRv2SdkUrl(string apiBaseUrl, string sessionId)
    {
        var baseSpan = apiBaseUrl.AsSpan().TrimEnd('/');
        return $"{baseSpan}/v1/code/sessions/{sessionId}";
    }

    /// <summary>
    /// 注册 Worker — 对齐 TS 端 registerWorker
    /// POST /v1/code/sessions/{id}/worker/register，返回 worker_epoch
    /// </summary>
    /// <param name="sessionUrl">会话 URL</param>
    /// <param name="accessToken">访问令牌</param>
    /// <param name="httpClient">HTTP 客户端</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>Worker epoch（int64）</returns>
    public static async Task<long> RegisterWorkerAsync(
        string sessionUrl,
        string accessToken,
        HttpClient httpClient,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{sessionUrl}/worker/register")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("anthropic-version", "2023-06-01");

        using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var parsed = JsonSerializer.Deserialize<BridgeWorkerRegisterResponse>(responseBody, BridgeJsonContext.Default.BridgeWorkerRegisterResponse);

        if (parsed is null)
        {
            throw new InvalidOperationException("registerWorker: invalid response");
        }

        // protojson 序列化 int64 为字符串避免 JS 精度丢失
        long epoch;
        if (parsed.WorkerEpoch.ValueKind == JsonValueKind.String)
        {
            if (!long.TryParse(parsed.WorkerEpoch.GetString(), out epoch))
            {
                throw new InvalidOperationException($"registerWorker: invalid worker_epoch string: {parsed.WorkerEpoch}");
            }
        }
        else if (parsed.WorkerEpoch.ValueKind == JsonValueKind.Number)
        {
            epoch = parsed.WorkerEpoch.GetInt64();
        }
        else
        {
            throw new InvalidOperationException($"registerWorker: invalid worker_epoch type: {parsed.WorkerEpoch.ValueKind}");
        }

        return epoch;
    }
}
