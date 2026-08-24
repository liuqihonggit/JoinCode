namespace Core.Hooks.Execution.Interception.Guards;

/// <summary>
/// VPN 路由守卫 — VPN 激活时为 git/curl 命令自动添加代理(迁移自旧 VpnRouteRewriter)
/// <para>
/// 解决问题:VPN 开启时网络请求需要走代理。检测到 VPN 激活且命令为 git/curl 时,
/// 从上下文读取 proxy_url 并返回 <see cref="CommandDecision.Rewrite"/> 加代理。
/// </para>
/// <para>
/// 迁移自 VpnRouteRewriter(Priority=30)。
/// </para>
/// </summary>
[Register]
public sealed partial class VpnRouteGuard : ICommandGuard
{
    private readonly ILogger<VpnRouteGuard>? _logger;
    private readonly INetworkConnectivityService? _networkService;

    /// <summary>
    /// 构造 VPN 路由守卫
    /// </summary>
    /// <param name="logger">日志器(可选)</param>
    /// <param name="networkService">网络连通性服务(可选,用于 VPN 检测)</param>
    public VpnRouteGuard(ILogger<VpnRouteGuard>? logger = null, INetworkConnectivityService? networkService = null)
    {
        _logger = logger;
        _networkService = networkService;
    }

    /// <inheritdoc/>
    public string Name => "VpnRouteGuard";

    /// <inheritdoc/>
    public int Priority => 30;

    /// <inheritdoc/>
    public bool CanHandle(string command, IReadOnlyDictionary<string, object> context)
    {
        if (!IsVpnActive()) return false;

        var normalized = command.TrimStart();
        return normalized.StartsWith("gh ", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("git ", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("curl ", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("wget ", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public CommandDecision Evaluate(string command, IReadOnlyDictionary<string, object> context)
    {
        if (!context.TryGetValue("proxy_url", out var proxyObj) || proxyObj is not string proxyUrl)
        {
            return new CommandDecision.Allow();
        }

        if (command.StartsWith("git ", StringComparison.OrdinalIgnoreCase))
        {
            var rewritten = $"git -c http.proxy={proxyUrl} -c https.proxy={proxyUrl} {command[4..]}";
            _logger?.LogInformation("为 git 命令添加 VPN 代理: {Proxy}", proxyUrl);
            return new CommandDecision.Rewrite(rewritten, "VPN 代理");
        }

        if (command.StartsWith("curl ", StringComparison.OrdinalIgnoreCase))
        {
            var rewritten = $"curl --proxy {proxyUrl} {command[5..]}";
            _logger?.LogInformation("为 curl 命令添加 VPN 代理: {Proxy}", proxyUrl);
            return new CommandDecision.Rewrite(rewritten, "VPN 代理");
        }

        // gh 命令通过环境变量控制代理,不改写命令本身
        return new CommandDecision.Allow();
    }

    /// <summary>
    /// 实时查询 VPN 是否活跃 — 优先使用注入的 INetworkConnectivityService,fallback 到静态检测
    /// </summary>
    private bool IsVpnActive() => _networkService?.IsVpnActive() ?? DetectVpn();

    /// <summary>
    /// 静态 VPN 检测(fallback) — 进程名 + 环境变量
    /// </summary>
    private static bool DetectVpn()
    {
        try
        {
            var vpnProcesses = new[] { "vpn", "openvpn", "wireguard", "clash", "v2ray" };
            foreach (var proc in vpnProcesses)
            {
                if (System.Diagnostics.Process.GetProcessesByName(proc).Length > 0)
                {
                    return true;
                }
            }

            var httpProxy = Environment.GetEnvironmentVariable("HTTP_PROXY");
            var httpsProxy = Environment.GetEnvironmentVariable("HTTPS_PROXY");
            if (!string.IsNullOrEmpty(httpProxy) || !string.IsNullOrEmpty(httpsProxy))
            {
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
