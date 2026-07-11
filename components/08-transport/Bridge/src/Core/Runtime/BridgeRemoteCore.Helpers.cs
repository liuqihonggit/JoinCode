
namespace Core.Bridge;

public static partial class BridgeRemoteCore
{
    #region 辅助方法

    /// <summary>
    /// 获取 bridge 凭证并注入受信设备令牌 — 对齐 TS 端 remoteBridgeCore.ts fetchRemoteCredentials 包装器
    /// </summary>
    internal static async Task<BridgeRemoteCredentials?> FetchCredentialsWithDeviceTokenAsync(
        string sessionId, BridgeEnvLessParams parameters, int httpTimeoutMs,
        HttpClient httpClient, string accessToken, CancellationToken ct)
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
                System.Diagnostics.Trace.WriteLine($"[BridgeRemoteCore] Failed to get trusted device token: {ex.Message}");
            }
        }

        return await BridgeCodeSessionApi.FetchRemoteCredentialsAsync(
            sessionId, parameters.BaseUrl, accessToken, httpTimeoutMs,
            httpClient, trustedDeviceToken, ct).ConfigureAwait(false);
    }

    #endregion

    #region withRetry

    /// <summary>
    /// 带指数退避+抖动的重试 — 对齐 TS 端 withRetry
    /// TS 端语义: fn 返回 null 时重试，非 null 立即返回；耗尽重试返回 null
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

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var result = await fn().ConfigureAwait(false);
            if (result is not null) return result;

            if (attempt < maxAttempts)
            {
                // 指数退避: baseDelay * 2^(attempt-1)
                var delay = baseDelayMs * (1 << (attempt - 1));
                delay = Math.Min(delay, maxDelayMs);

                // 抖动: 在 [1-jitter, 1+jitter] 范围内随机
                var jitter = 1.0 + (Random.Shared.NextDouble() * 2.0 - 1.0) * jitterFraction;
                var actualDelay = (int)(delay * jitter);

                await Task.Delay(actualDelay, ct).ConfigureAwait(false);
            }
        }

        return null;
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
