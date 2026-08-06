namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// 前缀缓存 TTL 判定 — 根据 provider 的 base_url 返回其已知的缓存保留时长。
/// 用于冷恢复剪裁：会话空闲超过 TTL 时服务端缓存已过期，此时改写前缀零额外 miss 成本。
/// 对齐 Reasonix Go 版 DefaultCacheTTL（config/cache_policy.go）。
/// </summary>
public static class CacheTtlResolver
{
    /// <summary>
    /// DashScope Session 缓存官方 TTL 5 分钟。
    /// </summary>
    public static readonly TimeSpan DashScopeTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Anthropic ephemeral 缓存官方 TTL 5 分钟。
    /// </summary>
    public static readonly TimeSpan AnthropicTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// DeepSeek 与未知 vendor 的保守默认 24 小时 — DeepSeek Context Caching on Disk
    /// 保留前缀数小时到数天，缩短会误剪仍热的缓存（实测 miss 成本约 4 倍）。
    /// </summary>
    public static readonly TimeSpan DeepSeekDefaultTtl = TimeSpan.FromHours(24);

    /// <summary>
    /// 按 base_url 返回 vendor 的默认缓存 TTL。
    /// 仅 DashScope（5m）与 Anthropic（5m）覆盖，DeepSeek 与未知 vendor 保守回退 24h。
    /// 值刻意保守：过小会烧掉仍热的缓存（全价首请求），过大只是少一次剪裁机会。
    /// </summary>
    /// <param name="baseUrl">provider 的 base_url，可为 null 或空串。</param>
    /// <returns>缓存 TTL 时长。</returns>
    public static TimeSpan DefaultCacheTtl(string? baseUrl)
    {
        switch (DetectCacheVendor(baseUrl))
        {
            case "dashscope":
                return DashScopeTtl;
            case "anthropic":
                return AnthropicTtl;
            default:
                return DeepSeekDefaultTtl;
        }
    }

    /// <summary>
    /// 从 base_url 识别 provider vendor — 基于 host 的精确/后缀匹配，避免无关或
    /// 攻击者控制的 URL 被误判。对齐 Reasonix Go 版 detectCacheVendor。
    /// </summary>
    /// <param name="baseUrl">provider 的 base_url。</param>
    /// <returns>vendor 标识："dashscope"/"anthropic"/"deepseek"/空串。</returns>
    public static string DetectCacheVendor(string? baseUrl)
    {
        var host = OfficialProviderHost(baseUrl);
        switch (true)
        {
            case true when host == "dashscope.aliyuncs.com" || host.EndsWith(".dashscope.aliyuncs.com", StringComparison.Ordinal) || host.EndsWith(".maas.aliyuncs.com", StringComparison.Ordinal):
                return "dashscope";
            case true when host == "api.deepseek.com" || host.EndsWith(".deepseek.com", StringComparison.Ordinal):
                return "deepseek";
            case true when host == "api.anthropic.com" || host.EndsWith(".anthropic.com", StringComparison.Ordinal):
                return "anthropic";
            default:
                return string.Empty;
        }
    }

    /// <summary>
    /// 从 base_url 提取官方 provider host（小写），格式非法或缺失时返回空串。
    /// </summary>
    private static string OfficialProviderHost(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return string.Empty;
        }

        var trimmed = baseUrl.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || uri.Host.Length == 0)
        {
            return string.Empty;
        }

        return uri.Host.ToLowerInvariant();
    }
}
