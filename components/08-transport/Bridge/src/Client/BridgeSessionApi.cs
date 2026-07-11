namespace Core.Bridge;

/// <summary>
/// Bridge 会话管理 API — 对齐 TS 端 createSession.ts
/// 独立的服务层，提供会话 CRUD 操作
/// 使用 StringBuilder 构建 JSON（AOT 兼容）
/// </summary>
public static class BridgeSessionApi
{
    private const string BetaHeader = "ccr-byoc-2025-07-29";
    private const int DefaultTimeoutMs = 10_000;

    #region createBridgeSession

    /// <summary>
    /// 创建 Bridge 会话 — 对齐 TS 端 createBridgeSession
    /// POST /v1/code/sessions — OAuth 认证，含 git 源 + org UUID headers
    /// 返回 session ID，失败返回 null（非致命错误）
    /// </summary>
    public static async Task<string?> CreateAsync(
        string environmentId,
        string? title,
        string[] events,
        string? gitRepoUrl,
        string branch,
        string baseUrl,
        string accessToken,
        string orgUUID,
        string? permissionMode,
        HttpClient httpClient,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(orgUUID))
        {
            return null;
        }

        var gitSourceJson = BuildGitSource(gitRepoUrl, branch);
        var eventsJson = BuildEventsJson(events);

        // 构建请求体 JSON 字符串 — 手动构建以兼容 NativeAOT
        var sb = new StringBuilder(256);
        sb.Append("{\"environment_id\":\"").Append(EscapeJsonString(environmentId)).Append("\",");
        sb.Append("\"source\":\"remote-control\",");

        if (!string.IsNullOrEmpty(title))
        {
            sb.Append("\"title\":").Append(EscapeJsonString(title)).Append(',');
        }

        sb.Append("\"events\":").Append(eventsJson);

        var hasSources = !string.IsNullOrEmpty(gitSourceJson);
        sb.Append(",\"session_context\":{\"sources\":");
        if (hasSources)
        {
            sb.Append('[').Append(gitSourceJson).Append(']');
        }
        else
        {
            sb.Append("[]");
        }

        sb.Append(",\"outcomes\":[]");
        sb.Append("}");

        if (!string.IsNullOrEmpty(permissionMode))
        {
            sb.Append(",\"permission_mode\":").Append(EscapeJsonString(permissionMode));
        }

        var jsonBody = sb.ToString();

        var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/v1/code/sessions")
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("anthropic-beta", BetaHeader);
        request.Headers.Add("x-organization-uuid", orgUUID);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(DefaultTimeoutMs));

        try
        {
            using var response = await httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);

            var status = (int)response.StatusCode;
            if (status >= 500 || status < 200 || status > 201)
            {
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            return ExtractSessionId(responseJson);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>构建 git source JSON 字符串 — 对齐 TS 端 parseGitRemote</summary>
    private static string? BuildGitSource(string? gitRepoUrl, string branch)
    {
        if (string.IsNullOrEmpty(gitRepoUrl))
        {
            return null;
        }

        var url = gitRepoUrl.Trim();
        string? owner = null, name = null;
        string host = "github.com";

        var repoMatch = System.Text.RegularExpressions.Regex.Match(url, @"(?:[^/]+/){3}([^/]+)/([^/.]+)");
        if (repoMatch.Success)
        {
            owner = repoMatch.Groups[1].Value;
            name = repoMatch.Groups[2].Value;
            var hm = System.Text.RegularExpressions.Regex.Match(url, @"^https?://([^/]+)");
            if (hm.Success)
            {
                host = hm.Groups[1].Value;
            }
        }
        else
        {
            var sm = System.Text.RegularExpressions.Regex.Match(url, @"^([^/]+)/([^/]+)$");
            if (sm.Success)
            {
                owner = sm.Groups[1].Value;
                name = sm.Groups[2].Value;
            }
            else
            {
                return null;
            }
        }

        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(name))
        {
            return null;
        }

        var sb = new StringBuilder(128);
        sb.Append("{\"type\":\"git_repository\",\"url\":\"https://").Append(EscapeJsonString($"{host}/{owner}/{name}"));
        if (!string.IsNullOrEmpty(branch))
        {
            sb.Append("\",\"revision\":").Append(EscapeJsonString(branch));
        }
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>构建 events JSON 字符串 — 对齐 TS 端 SessionEvent[]</summary>
    private static string BuildEventsJson(string[] events)
    {
        if (events is null or { Length: 0 })
        {
            return "[]";
        }

        var sb = new StringBuilder(events.Length * 64);
        sb.Append('[');
        for (var i = 0; i < events.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"type\":\"event\",\"data\":").Append(events[i]).Append('}');
        }
        sb.Append(']');
        return sb.ToString();
    }

    #endregion

    #region getBridgeSession

    /// <summary>
    /// 获取 Bridge 会话信息 — 对齐 TS 端 getBridgeSession
    /// GET /v1/sessions/{sessionId} — 返回 (environment_id, title)
    /// </summary>
    public static async Task<(string? EnvironmentId, string? Title)> GetAsync(
        string sessionId,
        string baseUrl,
        string accessToken,
        string orgUUID,
        HttpClient httpClient,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(orgUUID))
        {
            return (null, null);
        }

        var compatId = SessionIdCompat.ToCompatSessionId(sessionId);
        var url = $"{baseUrl.TrimEnd('/')}/v1/sessions/{compatId}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("anthropic-beta", BetaHeader);
        request.Headers.Add("x-organization-uuid", orgUUID);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(DefaultTimeoutMs));

        try
        {
            using var response = await httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return (null, null);
            }

            var responseJson = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var envId = ExtractJsonString(responseJson, "environment_id");
            var title = ExtractJsonString(responseJson, "title");
            return (envId, title);
        }
        catch
        {
            return (null, null);
        }
    }

    #endregion

    #region updateBridgeSessionTitle

    /// <summary>
    /// 更新 Bridge 会话标题 — 对齐 TS 端 updateBridgeSessionTitle
    /// PATCH /v1/sessions/{sessionId} — best-effort, 错误被吞掉
    /// </summary>
    public static async Task UpdateTitleAsync(
        string sessionId,
        string title,
        string baseUrl,
        string accessToken,
        string orgUUID,
        HttpClient httpClient,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(title))
        {
            return;
        }

        var compatId = SessionIdCompat.ToCompatSessionId(sessionId);
        var url = $"{baseUrl.TrimEnd('/')}/v1/sessions/{compatId}";

        var body = "{\"title\":" + EscapeJsonString(title) + '}';
        var request = new HttpRequestMessage(new HttpMethod("PATCH"), url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("anthropic-beta", BetaHeader);
        request.Headers.Add("x-organization-uuid", orgUUID);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(DefaultTimeoutMs));

        try
        {
            using var response = await httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
            _ = response;
        }
        catch (Exception ex)
        {
            // best-effort
            System.Diagnostics.Trace.WriteLine($"[BridgeSessionApi] Send request failed: {ex.Message}");
        }
    }

    #endregion

    #region archiveBridgeSession

    /// <summary>
    /// 归档 Bridge 会话 — 对齐 TS 端 archiveBridgeSession
    /// POST /v1/sessions/{sessionId}/archive — 409=已归档
    /// </summary>
    public static async Task<string> ArchiveAsync(
        string sessionId,
        string baseUrl,
        string? accessToken,
        string? orgUUID,
        int timeoutMs,
        HttpClient httpClient,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(accessToken))
        {
            return "skipped_no_token";
        }

        var compatId = SessionIdCompat.ToCompatSessionId(sessionId);
        var url = $"{baseUrl.TrimEnd('/')}/v1/sessions/{compatId}/archive";

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Headers.Add("anthropic-beta", BetaHeader);
        request.Headers.Add("x-organization-uuid", orgUUID);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

        try
        {
            using var response = await httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);

            var statusCode = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                return "ok";
            }

            // 409 = 已归档
            if (statusCode == 409)
            {
                return "already_archived";
            }

            return statusCode >= 400 && statusCode < 500 ? "server_4xx" : "server_5xx";
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return "timeout";
        }
        catch (HttpRequestException)
        {
            return "network_error";
        }
    }

    #endregion

    #region reconnectBridgeSession

    /// <summary>
    /// 重连 Bridge 会话 — 对齐 TS 端 reconnectBridgeSession
    /// POST /v1/environments/{environmentId}/bridge/reconnect
    /// </summary>
    public static async Task<string?> ReconnectAsync(
        string environmentId,
        string sessionId,
        string baseUrl,
        string accessToken,
        string orgUUID,
        HttpClient httpClient,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(orgUUID))
        {
            return null;
        }

        var url = $"{baseUrl.TrimEnd('/')}/v1/environments/{environmentId}/bridge/reconnect";

        var body = "{\"session_id\":" + EscapeJsonString(sessionId) + '}';
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("anthropic-beta", BetaHeader);
        request.Headers.Add("x-organization-uuid", orgUUID);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(DefaultTimeoutMs));

        try
        {
            using var response = await httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);

            var status = (int)response.StatusCode;
            if (status >= 500 || status != 200)
            {
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            return ExtractJsonString(responseJson, "sdk_url");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 重连 Bridge 会话（完整端点） — 对齐 TS 端 reconnectSession / BridgeApiClient.ReconnectSessionAsync
    /// POST /v1/environments/bridge/{environmentId}/sessions/{sessionId}/bridge/reconnect
    /// 与 BridgeApiClient.ReconnectSessionAsync 端点一致，消除重复实现
    /// </summary>
    public static async Task<BridgeReconnectResponse?> ReconnectSessionAsync(
        string environmentId,
        string sessionId,
        HttpClient httpClient,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        var url = $"/v1/environments/bridge/{environmentId}/sessions/{sessionId}/bridge/reconnect";
        var body = new System.Text.StringBuilder("{\"environment_id\":\"")
            .Append(EscapeJsonString(environmentId))
            .Append("\",\"session_id\":\"")
            .Append(EscapeJsonString(sessionId))
            .Append("\"}");

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json"),
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(DefaultTimeoutMs));

        try
        {
            using var response = await httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return null; // caller 处理 401
            }

            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            return JsonSerializer.Deserialize(responseJson, BridgeJsonContext.Default.BridgeReconnectResponse);
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region JSON 辅助方法

    /// <summary>提取 session ID 字段 — 对齐 TS 端: sessionData.id</summary>
    private static string? ExtractSessionId(string json)
    {
        const string key = "\"id\":";
        var idx = json.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0)
        {
            return null;
        }

        idx += key.Length;
        // 跳过空白
        while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;

        if (idx >= json.Length || json[idx] != '"')
        {
            return null;
        }

        idx++; // skip opening quote
        var start = idx;
        while (idx < json.Length && json[idx] != '"') idx++;

        return json.Substring(start, idx - start);
    }

    /// <summary>从 JSON 字符串中提取字段值（简单解析，AOT 兼容）</summary>
    private static string? ExtractJsonString(string json, string fieldName)
    {
        var key = $"\"{fieldName}\":";
        var idx = json.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0)
        {
            return null;
        }

        idx += key.Length;
        while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;

        if (idx >= json.Length || json[idx] != '"')
        {
            return null;
        }

        idx++;
        var start = idx;
        while (idx < json.Length && json[idx] != '"') idx++;

        return json.Substring(start, idx - start);
    }

    /// <summary>转义 JSON 字符串值 — 防止注入</summary>
    private static string EscapeJsonString(ReadOnlySpan<char> value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        var needsEscape = false;
        foreach (var c in value)
        {
            if (c is '"' or '\\' or '\n' or '\r' or '\t')
            {
                needsEscape = true;
                break;
            }
        }

        if (!needsEscape)
        {
            return "\"" + value.ToString() + "\"";
        }

        var sb = new StringBuilder(value.Length + 16);
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

    #endregion
}
