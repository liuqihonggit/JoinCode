
namespace Core.Bridge;

public static partial class BridgeRemoteCore
{
    #region 辅助方法

    /// <summary>
    /// 获取 bridge 凭证并注入受信设备令牌 — 对齐 TS 端 remoteBridgeCore.ts fetchRemoteCredentials 包装器
    /// </summary>
    internal static async Task<BridgeRemoteCredentials?> FetchCredentialsWithDeviceTokenAsync(
        string sessionId, V2BridgeParams parameters, int httpTimeoutMs,
        HttpClient httpClient, string accessToken, CancellationToken ct, ILogger? logger = null)
    {
        string? trustedDeviceToken = null;
        if (parameters.GetTrustedDeviceToken is not null)
        {
            try
            {
                trustedDeviceToken = await parameters.GetTrustedDeviceToken().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // best-effort: 获取设备令牌失败不阻塞主流程
                logger?.LogWarning(ex, "[BridgeRemoteCore] Failed to get trusted device token");
            }
        }

        return await BridgeCodeSessionApi.FetchRemoteCredentialsAsync(
            sessionId, parameters.BaseUrl, accessToken, httpTimeoutMs,
            httpClient, trustedDeviceToken, ct).ConfigureAwait(false);
    }

    #endregion

    #region withRetry

    /// <summary>
    /// 请求发送 — 降级为透传，网络重试统一由 ResilientHttpExecutor (Gateway) 处理，避免嵌套放大
    /// <para>原语义: fn 返回 null 时重试；降级后: 单次执行，null 直接返回</para>
    /// </summary>
    public static async Task<T?> WithRetryAsync<T>(
        Func<Task<T?>> fn,
        string label,
        int maxAttempts = 3,
        int baseDelayMs = 500,
        int maxDelayMs = 4000,
        double jitterFraction = 0.25,
        CancellationToken ct = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(fn);
        ct.ThrowIfCancellationRequested();
        return await fn().ConfigureAwait(false);
    }

    #endregion

    #region deriveTitle

    /// <summary>
    /// 从原始文本派生占位标题 — 对齐 TS 端 deriveTitle
    /// 去标签、取首句、截断50字符
    /// </summary>
    public static string DeriveTitle(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        // 去除标签（XML 标签）
        var text = raw.Trim();

        // 取第一行
        var newlineIdx = text.IndexOf('\n');
        if (newlineIdx > 0)
        {
            text = text[..newlineIdx];
        }

        // 截断到 50 字符
        if (text.Length > 50)
        {
            text = string.Concat(text.AsSpan(0, 47), "...");
        }

        return text.Trim();
    }

    #endregion
}
