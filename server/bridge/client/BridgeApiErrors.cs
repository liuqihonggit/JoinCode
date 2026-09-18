namespace Core.Bridge;

/// <summary>
/// Bridge API 错误处理与验证 — 从 BridgeApiClient 提取的单一职责静态小类
/// <para>职责: ID 验证 + HTTP 错误状态处理 + 错误信息提取 + 错误类型判断</para>
/// </summary>
internal static class BridgeApiErrors
{
    /// <summary>
    /// 验证 Bridge ID 格式 — 只允许字母数字、连字符、下划线(防路径遍历)
    /// </summary>
    public static bool ValidateBridgeId(string bridgeId)
    {
        if (string.IsNullOrWhiteSpace(bridgeId))
        {
            return false;
        }

        // 只允许字母数字、连字符、下划线
        foreach (var c in bridgeId)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 处理非 2xx 响应状态 — 对齐 TS 端 handleErrorStatus
    /// 从响应体提取 errorType 和 detail，按状态码抛出 BridgeFatalError 或 Exception
    /// </summary>
    internal static void HandleErrorStatus(int status, string? responseBody, string context, ILogger? logger = null)
    {
        if (status is 200 or 204) return;

        var detail = ExtractErrorDetail(responseBody, logger);
        var errorType = ExtractErrorTypeFromData(responseBody, logger);

        switch (status)
        {
            case 401:
                throw new BridgeFatalError(
                    $"{context}: Authentication failed (401){(detail is not null ? $": {detail}" : "")}. Please run `{BrandConstants.CliCommandName} remote-control` to authenticate.",
                    status, errorType);
            case 403:
                throw new BridgeFatalError(
                    IsExpiredErrorType(errorType)
                        ? $"Remote Control session has expired. Please restart with `{BrandConstants.CliCommandName} remote-control` or /remote-control."
                        : $"{context}: Access denied (403){(detail is not null ? $": {detail}" : "")}. Check your organization permissions.",
                    status, errorType);
            case 404:
                throw new BridgeFatalError(
                    detail ?? $"{context}: Not found (404). Remote Control may not be available for this organization.",
                    status, errorType);
            case 410:
                throw new BridgeFatalError(
                    detail ?? $"Remote Control session has expired. Please restart with `{BrandConstants.CliCommandName} remote-control` or /remote-control.",
                    status, errorType ?? "environment_expired");
            case 429:
                throw new InvalidOperationException($"{context}: Rate limited (429). Polling too frequently.");
            default:
                throw new InvalidOperationException(
                    $"{context}: Failed with status {status}{(detail is not null ? $": {detail}" : "")}");
        }
    }

    /// <summary>
    /// 从响应体 JSON 提取 errorType — 对齐 TS 端 extractErrorTypeFromData
    /// 路径: data.error.type
    /// </summary>
    internal static string? ExtractErrorTypeFromData(string? responseBody, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(responseBody)) return null;
        try
        {
            var data = RelaxedJsonSerializer.Deserialize(responseBody, BridgeJsonContext.Default.DictionaryStringJsonElement);
            if (data is not null &&
                data.TryGetValue("error", out var errorEl) &&
                errorEl.ValueKind == JsonValueKind.Object)
            {
                // error 是对象，尝试提取 error.type
                var errorDict = RelaxedJsonSerializer.Deserialize(errorEl.GetRawText(), BridgeJsonContext.Default.DictionaryStringJsonElement);
                if (errorDict is not null &&
                    errorDict.TryGetValue("type", out var typeEl) &&
                    typeEl.ValueKind == JsonValueKind.String)
                {
                    return typeEl.GetString();
                }
            }
        }
        catch (Exception ex) { /* 解析失败返回 null */ logger?.LogWarning(ex, "[BridgeApiClient] Extract error type failed"); }
        return null;
    }

    /// <summary>
    /// 从响应体 JSON 提取错误详情 — 对齐 TS 端 extractErrorDetail
    /// 优先 data.message，其次 data.error.message
    /// </summary>
    internal static string? ExtractErrorDetail(string? responseBody, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(responseBody)) return null;
        try
        {
            var data = RelaxedJsonSerializer.Deserialize(responseBody, BridgeJsonContext.Default.DictionaryStringJsonElement);
            if (data is null) return null;

            // 优先 data.message
            if (data.TryGetValue("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String)
            {
                return msgEl.GetString();
            }

            // 其次 data.error.message
            if (data.TryGetValue("error", out var errorEl) && errorEl.ValueKind == JsonValueKind.Object)
            {
                var errorDict = RelaxedJsonSerializer.Deserialize(errorEl.GetRawText(), BridgeJsonContext.Default.DictionaryStringJsonElement);
                if (errorDict is not null &&
                    errorDict.TryGetValue("message", out var errMsgEl) &&
                    errMsgEl.ValueKind == JsonValueKind.String)
                {
                    return errMsgEl.GetString();
                }
            }
        }
        catch (Exception ex) { /* 解析失败返回 null */ logger?.LogWarning(ex, "[BridgeApiClient] Extract error detail failed"); }
        return null;
    }

    /// <summary>
    /// 判断 errorType 是否为过期类型 — 对齐 TS 端 isExpiredErrorType
    /// </summary>
    public static bool IsExpiredErrorType(string? errorType)
    {
        if (string.IsNullOrEmpty(errorType)) return false;
        return errorType.Contains("expired", StringComparison.OrdinalIgnoreCase) ||
               errorType.Contains("lifetime", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断 403 是否可抑制 — 对齐 TS 端 isSuppressible403
    /// 某些 403 是"装饰性"的（缺少非核心 scope），不应以错误形式打扰用户
    /// </summary>
    public static bool IsSuppressible403(BridgeFatalError err)
    {
        if (err.StatusCode != 403) return false;
        return err.Message.Contains("external_poll_sessions", StringComparison.OrdinalIgnoreCase) ||
               err.Message.Contains("environments:manage", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 描述 HTTP 错误 — 对齐 TS 端 describeAxiosError
    /// 从 HttpResponseMessage 提取基础消息 + 服务器返回的详细信息
    /// </summary>
    public static string DescribeHttpError(Exception ex, ILogger? logger = null)
    {
        var msg = ex.Message;
        if (ex is HttpRequestException httpEx && httpEx.Data.Contains("ResponseBody"))
        {
            var body = httpEx.Data["ResponseBody"] as string;
            var detail = ExtractErrorDetail(body, logger);
            if (detail is not null)
            {
                return $"{msg}: {detail}";
            }
        }
        return msg;
    }
}
