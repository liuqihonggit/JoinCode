namespace JoinCode.Transport.Bridge;

/// <summary>
/// Bridge 调试工具集 — 对齐 TS 端 debugUtils.ts
/// 敏感信息脱敏 + 错误信息提取 + 调试日志截断
/// </summary>
public static partial class BridgeDebugUtils
{
    /// <summary>调试日志截断上限</summary>
    private const int DebugTruncateLimit = 2000;

    /// <summary>
    /// JSON 敏感字段脱敏 — 对齐 TS 端 redactSecrets
    /// 匹配字段: session_ingress_token, environment_secret, access_token, secret, token
    /// 长度>=16 保留前8后4，短的替换为 [REDACTED]
    /// </summary>
    public static string RedactSecrets(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        return SecretRedactionRegex().Replace(s, static match =>
        {
            var value = match.Groups[2].Value;
            if (value.Length >= 16)
            {
                return $"{match.Groups[1].Value}{value[..8]}...{value[^4..]}\"";
            }

            return $"{match.Groups[1].Value}[REDACTED]\"";
        });
    }

    /// <summary>
    /// 截断调试日志字符串 — 对齐 TS 端 debugTruncate
    /// 上限 2000 字符，折叠换行
    /// </summary>
    public static string DebugTruncate(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        // 折叠换行
        var collapsed = s.Replace("\n", " ").Replace("\r", "");

        return collapsed.Length <= DebugTruncateLimit
            ? collapsed
            : $"{collapsed[..DebugTruncateLimit]}...[truncated {collapsed.Length - DebugTruncateLimit} chars]";
    }

    /// <summary>
    /// 序列化+脱敏+截断 — 对齐 TS 端 debugBody
    /// </summary>
    public static string DebugBody(object? data)
    {
        if (data is null) return "null";

        try
        {
            var json = data is JsonElement je
                ? je.GetRawText()
                : data.ToString() ?? "null";

            return DebugTruncate(RedactSecrets(json));
        }
        catch
        {
            return DebugTruncate(data.ToString() ?? string.Empty);
        }
    }

    /// <summary>
    /// 从 HTTP 错误提取描述 — 对齐 TS 端 describeAxiosError
    /// 优先提取 response.data.message 或 response.data.error.message
    /// </summary>
    public static string DescribeHttpError(Exception err)
    {
        if (err is HttpRequestException httpEx)
        {
            var msg = httpEx.Message;
            if (TryExtractErrorMessage(httpEx, out var errorDetail))
            {
                return errorDetail;
            }

            return msg;
        }

        return err.Message;
    }

    /// <summary>
    /// 从 HTTP 错误提取状态码 — 对齐 TS 端 extractHttpStatus
    /// </summary>
    public static int? ExtractHttpStatus(Exception err)
    {
        if (err is HttpRequestException httpEx && httpEx.StatusCode.HasValue)
        {
            return (int)httpEx.StatusCode.Value;
        }

        return null;
    }

    /// <summary>
    /// 从 API 错误响应体提取详情 — 对齐 TS 端 extractErrorDetail
    /// 提取 data.message 或 data.error.message
    /// </summary>
    public static string? ExtractErrorDetail(JsonElement? data)
    {
        if (data is not JsonElement je || je.ValueKind != JsonValueKind.Object) return null;

        // 尝试 data.message
        if (je.TryGetProperty("message", out var msgProp) && msgProp.ValueKind == JsonValueKind.String)
        {
            return msgProp.GetString();
        }

        // 尝试 data.error.message
        if (je.TryGetProperty("error", out var errorProp) && errorProp.ValueKind == JsonValueKind.Object
            && errorProp.TryGetProperty("message", out var errorMsgProp) && errorMsgProp.ValueKind == JsonValueKind.String)
        {
            return errorMsgProp.GetString();
        }

        return null;
    }

    /// <summary>
    /// 尝试从 HttpRequestException 的 Data 中提取错误消息
    /// </summary>
    private static bool TryExtractErrorMessage(HttpRequestException httpEx, out string message)
    {
        message = string.Empty;

        // 检查 Data 字典中是否有响应体
        if (httpEx.Data["ResponseBody"] is string body)
        {
            try
            {
                var je = JsonDocument.Parse(body).RootElement;
                var detail = ExtractErrorDetail(je);
                if (detail is not null)
                {
                    message = detail;
                    return true;
                }
            }
            catch (Exception ex)
            {
                // 忽略解析失败
                System.Diagnostics.Trace.WriteLine($"[BridgeDebugUtils] Parse error detail failed: {ex.Message}");
            }
        }

        return false;
    }

    /// <summary>
    /// 匹配 JSON 中的敏感字段值 — 对齐 TS 端正则
    /// 匹配: "session_ingress_token":"xxx" / "environment_secret":"xxx" / "access_token":"xxx" / "secret":"xxx" / "token":"xxx"
    /// </summary>
    [GeneratedRegex(@"(""(?:session_ingress_token|environment_secret|access_token|secret|token)""\s*:\s*"")([^""]+)""", RegexOptions.Compiled)]
    private static partial Regex SecretRedactionRegex();
}
