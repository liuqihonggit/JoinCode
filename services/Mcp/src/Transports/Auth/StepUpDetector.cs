namespace McpClient.Transports;

/// <summary>
/// Step-Up 认证检测器 — 对齐 TS wrapFetchWithStepUpDetection
/// 检测 403 + WWW-Authenticate: insufficient_scope 响应，通知 AuthProvider
/// </summary>
public static class StepUpDetector
{
    /// <summary>
    /// 检测 HTTP 响应中的 Step-Up 认证需求 — 对齐 TS wrapFetchWithStepUpDetection
    /// </summary>
    /// <param name="response">HTTP 响应</param>
    /// <param name="authProvider">认证提供者（可选）</param>
    /// <returns>检测到的 Step-Up scope，未检测到返回 null</returns>
    public static string? DetectStepUp(HttpResponseMessage response, IMcpAuthProvider? authProvider)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.StatusCode != System.Net.HttpStatusCode.Forbidden)
        {
            return null;
        }

        var wwwAuth = response.Headers.WwwAuthenticate;
        if (wwwAuth.Count == 0)
        {
            return null;
        }

        // 对齐 TS: 检查 WWW-Authenticate 头是否包含 insufficient_scope
        foreach (var headerValue in wwwAuth)
        {
            var scheme = headerValue.Scheme;
            var parameter = headerValue.Parameter;
            if (parameter is null) continue;

            // Bearer error="insufficient_scope" 或 Bearer error=insufficient_scope
            if (!string.Equals(scheme, "Bearer", StringComparison.OrdinalIgnoreCase)) continue;
            if (!parameter.Contains("insufficient_scope", StringComparison.OrdinalIgnoreCase)) continue;

            // 对齐 TS: scope=(?:"([^"]+)"|([^\s,]+)) — 同时匹配引号值和非引号值
            var scope = ExtractScopeFromWwwAuthenticate(parameter);
            if (scope is not null && authProvider is not null)
            {
                authProvider.MarkStepUpPending(scope);
            }

            return scope;
        }

        return null;
    }

    /// <summary>
    /// 从 WWW-Authenticate 参数中提取 scope — 对齐 TS scope=(?:"([^"]+)"|([^\s,]+))
    /// </summary>
    internal static string? ExtractScopeFromWwwAuthenticate(string wwwAuthParameter)
    {
        // 匹配 scope="value" 或 scope=value
        var span = wwwAuthParameter.AsSpan();
        var scopeIndex = span.IndexOf("scope=", StringComparison.OrdinalIgnoreCase);
        if (scopeIndex < 0) return null;

        var valueSpan = span[(scopeIndex + 6)..];

        // 跳过前导空白
        valueSpan = valueSpan.TrimStart();

        if (valueSpan.Length == 0) return null;

        // 引号值: scope="value"
        if (valueSpan[0] == '"')
        {
            var endQuote = valueSpan[1..].IndexOf('"');
            if (endQuote < 0) return null;
            return valueSpan[1..(endQuote + 1)].ToString();
        }

        // 非引号值: scope=value（到空格或逗号结束）
        var endIdx = valueSpan.IndexOfAny(' ', ',');
        return endIdx < 0 ? valueSpan.ToString() : valueSpan[..endIdx].ToString();
    }
}
