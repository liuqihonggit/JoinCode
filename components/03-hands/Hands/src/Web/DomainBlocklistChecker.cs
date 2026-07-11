using JoinCode.Abstractions.Attributes;

namespace Services.Web;

/// <summary>
/// 域名黑名单预检服务
/// 对齐TS版 checkDomainBlocklist — 调用 api.anthropic.com 检查域名安全性
/// </summary>
[Register(typeof(IDomainBlocklistChecker))]
public sealed partial class DomainBlocklistChecker : IDomainBlocklistChecker
{
    private const string BlocklistApiUrl = "https://api.anthropic.com/api/web/domain_info?domain=";
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(10);

    private readonly IApiClient _apiClient;
    private readonly IWebFetchCache _cache;
    [Inject] private readonly ILogger<DomainBlocklistChecker>? _logger;

    public DomainBlocklistChecker(
        IApiClient apiClient,
        IWebFetchCache cache,
        ILogger<DomainBlocklistChecker>? logger = null)
    {
        _apiClient = apiClient;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// 检查域名是否被Anthropic黑名单拦截
    /// 仅缓存allowed状态，blocked/failed每次重查
    /// 对齐 TS 版: skipWebFetchPreflight 设置可跳过预检
    /// </summary>
    public async Task<DomainCheckResult> CheckAsync(string domain, CancellationToken cancellationToken = default)
    {
        // 对齐 TS 版 getSettings_DEPRECATED().skipWebFetchPreflight
        var skipPreflight = Environment.GetEnvironmentVariable(JccEnvVarConstants.SkipWebFetchPreflight);
        if (string.Equals(skipPreflight, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(skipPreflight, "1", StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogDebug("域名预检已跳过(JCC_SKIP_WEB_FETCH_PREFLIGHT): {Domain}", domain);
            return DomainCheckResult.Allowed;
        }

        // 缓存命中 = 之前已allowed
        if (_cache.IsDomainCheckCached(domain))
        {
            _logger?.LogDebug("域名预检缓存命中: {Domain} = allowed", domain);
            return DomainCheckResult.Allowed;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(CheckTimeout);

            var response = await _apiClient.SendAsync(
                ApiRequest.Get($"{BlocklistApiUrl}{Uri.EscapeDataString(domain)}"),
                cts.Token).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                // TS版检查 can_fetch === true
                if (content.Contains("\"can_fetch\":true", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains("\"can_fetch\": true", StringComparison.OrdinalIgnoreCase))
                {
                    _cache.CacheDomainCheck(domain);
                    _logger?.LogDebug("域名预检通过: {Domain}", domain);
                    return DomainCheckResult.Allowed;
                }

                _logger?.LogWarning("域名被黑名单拦截: {Domain}", domain);
                return DomainCheckResult.Blocked;
            }

            _logger?.LogWarning("域名预检请求失败: {Domain}, Status={Status}", domain, response.StatusCode);
            return DomainCheckResult.CheckFailed;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogWarning("域名预检超时: {Domain}", domain);
            return DomainCheckResult.CheckFailed;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "域名预检异常: {Domain}", domain);
            return DomainCheckResult.CheckFailed;
        }
    }
}
