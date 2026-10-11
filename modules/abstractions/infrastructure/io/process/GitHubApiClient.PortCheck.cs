// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003

namespace IO.ProcessService;

/// <summary>
/// GitHub API 端口检测与路由切换 — 网络不稳定时检测端口可达性,给出友好提示,支持备用路由切换
/// <para>检测模式: 请求失败时用 TcpClient 检测 host:port 是否可达,不可达时返回友好错误提示</para>
/// <para>路由模式: 主 URL 不可达时自动切换到备用 URL(通过 JCC_GITHUB_API_FALLBACK_URLS 环境变量配置,逗号分隔)</para>
/// </summary>
public sealed partial class GitHubApiClient {
    private static readonly TimeSpan PortCheckTimeout = TimeSpan.FromSeconds(2);
    private static readonly string[] FallbackUrls = ParseFallbackUrls();

    /// <summary>
    /// 解析备用 URL 列表 — 从 JCC_GITHUB_API_FALLBACK_URLS 环境变量读取(逗号分隔)
    /// </summary>
    private static string[] ParseFallbackUrls() {
        var env = Environment.GetEnvironmentVariable("JCC_GITHUB_API_FALLBACK_URLS");
        if (string.IsNullOrWhiteSpace(env)) return [];
        return env.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// 从 URI 提取 host 和 port — 默认端口补全(https→443, http→80)
    /// </summary>
    internal static (string host, int port) ExtractHostPort(Uri uri) {
        var host = uri.Host;
        var port = uri.Port == -1 ? (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80) : uri.Port;
        return (host, port);
    }

    /// <summary>
    /// 检测 host:port 是否可达 — TcpClient 带 2s 超时,不抛异常
    /// </summary>
    internal static async Task<bool> IsPortReachableAsync(string host, int port, CancellationToken ct) {
        try {
            using var timeoutCts = new CancellationTokenSource(PortCheckTimeout);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
            return true;
        } catch {
            return false;
        }
    }

    /// <summary>
    /// 构建网络错误友好提示 — 检测端口可达性,给出可能原因和建议
    /// </summary>
    internal async Task<string> BuildNetworkErrorMessageAsync(Exception ex, CancellationToken ct) {
        var baseUrl = _httpClient.BaseAddress;
        if (baseUrl is null) return $"GitHub API 连接失败: {ex.Message}";

        var (host, port) = ExtractHostPort(baseUrl);
        var reachable = await IsPortReachableAsync(host, port, ct).ConfigureAwait(false);

        if (reachable) {
            return $"GitHub API 连接失败（{host}:{port} 端口可达，可能是 API 服务端问题或 SSL 握手失败）。\n原始错误: {ex.Message}\n建议: 稍后重试，或检查 GitHub Status (https://www.githubstatus.com)";
        }

        return $"GitHub API 端口不可达: {host}:{port} 连接失败。\n可能原因: ① 网络断开 ② 代理/VPN 断开 ③ DNS 解析失败 ④ 防火墙拦截端口 {port}\n建议: 检查网络连接、代理配置、DNS 解析(nslookup {host})";
    }

    /// <summary>
    /// 尝试切换到备用路由 — 遍历 FallbackUrls,找到第一个端口可达的 URL 并切换 BaseAddress
    /// <para>返回 true 表示切换成功,_httpClient.BaseAddress 已更新</para>
    /// </summary>
    internal async Task<bool> TrySwitchToFallbackRouteAsync(CancellationToken ct) {
        foreach (var url in FallbackUrls) {
            try {
                var uri = new Uri(EnsureTrailingSlash(url));
                var (host, port) = ExtractHostPort(uri);
                if (await IsPortReachableAsync(host, port, ct).ConfigureAwait(false)) {
                    _httpClient.BaseAddress = uri;
                    _logger?.LogWarning("GitHub API 路由切换: → {Url}", url);
                    return true;
                }
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "备用路由解析失败: {Url}", url);
            }
        }
        return false;
    }
}
