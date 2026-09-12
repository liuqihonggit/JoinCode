namespace Infrastructure.Network;

/// <summary>
/// 网络连接性服务 — 基于 NetworkInterface + NetworkChange 事件的实时多流网络感知
/// <para>
/// 三重 VPN 识别:接口名/描述匹配 + 进程检测 + 环境变量
/// </para>
/// <para>
/// 多流:一台机器可同时有多个活跃接口(以太网+WiFi+VPN隧道),GetActiveInterfaces 返回全部
/// </para>
/// </summary>
[Register(typeof(INetworkConnectivityService), ServiceLifetime.Singleton)]
public sealed partial class NetworkConnectivityService : ServiceEntity, INetworkConnectivityService
{
    private readonly ILogger<NetworkConnectivityService>? _logger;
    private readonly Func<IReadOnlyList<NetworkInterfaceInfo>> _interfaceProvider;
    private readonly Func<bool> _vpnProcessDetector;
    private readonly Func<bool> _proxyEnvDetector;
    private readonly TimeProvider _timeProvider;

    private readonly AsyncLock _stateLock = new("NetworkConnectivityService");
    private NetworkConnectivityState _currentState;
    private bool _subscribed;

    private static readonly FrozenSet<string> VpnKeywords = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "tap", "tun", "vpn", "ppp", "wireguard", "clash", "v2ray", "utun", "tunnel", "openvpn", "singbox", "he");

    private static readonly FrozenSet<string> VpnProcessNames = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "vpn", "openvpn", "wireguard", "clash", "v2ray", "singbox", "tunnelblick", "viscosity", "cloudflare");

    /// <summary>
    /// 构造网络连接性服务 — DI 容器调用时仅传 logger,其余可选参数为 null 时使用平台默认实现
    /// </summary>
    public NetworkConnectivityService(
        ILogger<NetworkConnectivityService>? logger = null,
        TimeProvider? timeProvider = null,
        Func<IReadOnlyList<NetworkInterfaceInfo>>? interfaceProvider = null,
        Func<bool>? vpnProcessDetector = null,
        Func<bool>? proxyEnvDetector = null)
    {
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _interfaceProvider = interfaceProvider ?? DiscoverInterfaces;
        _vpnProcessDetector = vpnProcessDetector ?? DetectVpnProcesses;
        _proxyEnvDetector = proxyEnvDetector ?? DetectProxyEnv;
        _currentState = ComputeState();
        SubscribeNetworkChange();
    }

    /// <inheritdoc/>
    public NetworkConnectivityState CurrentState
    {
        get { using (_stateLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_stateLock.Name}' 等待超时")) return _currentState; }
    }

    /// <inheritdoc/>
    public bool IsNetworkAvailable()
    {
        using (_stateLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_stateLock.Name}' 等待超时")) return _currentState != NetworkConnectivityState.Offline;
    }

    /// <inheritdoc/>
    public bool IsVpnActive()
    {
        using (_stateLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_stateLock.Name}' 等待超时")) return _currentState == NetworkConnectivityState.OnlineWithVpn;
    }

    /// <inheritdoc/>
    public IReadOnlyList<NetworkInterfaceInfo> GetActiveInterfaces() => _interfaceProvider();

    /// <inheritdoc/>
    public NetworkRoute GetCurrentRoute()
    {
        var state = CurrentState;
        return state switch
        {
            NetworkConnectivityState.OnlineWithVpn => new NetworkRoute { Type = NetworkRouteType.Vpn, ViaInterface = FindVpnInterfaceName() },
            NetworkConnectivityState.OnlineWithProxy => new NetworkRoute { Type = NetworkRouteType.Proxy, ProxyUrl = GetProxyUrlFromEnv() },
            _ => new NetworkRoute { Type = NetworkRouteType.Direct },
        };
    }

    /// <inheritdoc/>
    public event EventHandler<NetworkConnectivityChangedEventArgs>? StateChanged;

    /// <summary>
    /// 显式刷新状态 — 重新检测网络并触发事件(供测试和主动刷新使用)
    /// </summary>
    internal void RefreshState() => OnNetworkChanged("manual refresh");

    private string? FindVpnInterfaceName()
    {
        var interfaces = _interfaceProvider();
        return interfaces.FirstOrDefault(i => i.Kind == NetworkInterfaceKind.VpnTunnel)?.Name;
    }

    private static string? GetProxyUrlFromEnv()
    {
        return Environment.GetEnvironmentVariable("HTTPS_PROXY")
            ?? Environment.GetEnvironmentVariable("HTTP_PROXY")
            ?? Environment.GetEnvironmentVariable("https_proxy")
            ?? Environment.GetEnvironmentVariable("http_proxy");
    }

    private NetworkConnectivityState ComputeState()
    {
        var interfaces = _interfaceProvider();
        var hasNonLoopbackUp = interfaces.Any(i => i.IsUp && i.Kind != NetworkInterfaceKind.Loopback);
        if (!hasNonLoopbackUp) return NetworkConnectivityState.Offline;

        var hasVpnInterface = interfaces.Any(i => i.IsUp && i.Kind == NetworkInterfaceKind.VpnTunnel);
        var vpnByProcess = _vpnProcessDetector();
        if (hasVpnInterface || vpnByProcess) return NetworkConnectivityState.OnlineWithVpn;

        if (_proxyEnvDetector()) return NetworkConnectivityState.OnlineWithProxy;

        return NetworkConnectivityState.Online;
    }

    private void OnNetworkChanged(string reason)
    {
        var newState = ComputeState();
        NetworkConnectivityState previous;
        using (_stateLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_stateLock.Name}' 等待超时"))
        {
            if (_currentState == newState) return;
            previous = _currentState;
            _currentState = newState;
        }

        _logger?.LogInformation("网络状态变化: {Previous} → {Current} ({Reason})", previous, newState, reason);

        StateChanged?.Invoke(this, new NetworkConnectivityChangedEventArgs
        {
            PreviousState = previous,
            CurrentState = newState,
            Timestamp = _timeProvider.GetLocalNow(),
            Reason = reason,
        });
    }

    private void SubscribeNetworkChange()
    {
        if (_subscribed) return;
        try
        {
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
            _subscribed = true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "订阅 NetworkChange 事件失败,网络状态将不会自动刷新");
        }
    }

    private void UnsubscribeNetworkChange()
    {
        if (!_subscribed) return;
        try
        {
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "取消订阅 NetworkChange 事件失败");
        }
        _subscribed = false;
    }

    private void OnNetworkAvailabilityChanged(object? sender, EventArgs e) => OnNetworkChanged("network availability changed");
    private void OnNetworkAddressChanged(object? sender, EventArgs e) => OnNetworkChanged("network address changed");

    /// <inheritdoc/>
    protected override void OnDispose()
    {
        UnsubscribeNetworkChange();
        base.OnDispose();
    }

    private static IReadOnlyList<NetworkInterfaceInfo> DiscoverInterfaces()
    {
        try
        {
            var result = new List<NetworkInterfaceInfo>();
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                result.Add(new NetworkInterfaceInfo
                {
                    Name = ni.Name,
                    Description = ni.Description,
                    Kind = ClassifyInterface(ni),
                    IsUp = true,
                    Addresses = ni.GetIPProperties().UnicastAddresses
                        .Select(static a => a.Address.ToString())
                        .ToList(),
                    SpeedBitsPerSecond = ni.Speed,
                    Mtu = 0,
                });
            }
            return result;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static NetworkInterfaceKind ClassifyInterface(NetworkInterface ni)
    {
        return ni.NetworkInterfaceType switch
        {
            NetworkInterfaceType.Ethernet => NetworkInterfaceKind.Ethernet,
            NetworkInterfaceType.Wireless80211 => NetworkInterfaceKind.Wireless,
            NetworkInterfaceType.Loopback => NetworkInterfaceKind.Loopback,
            _ => ClassifyByVpnKeyword(ni),
        };
    }

    private static NetworkInterfaceKind ClassifyByVpnKeyword(NetworkInterface ni)
    {
        if (VpnKeywords.Any(k => ni.Name.Contains(k, StringComparison.OrdinalIgnoreCase))
            || VpnKeywords.Any(k => ni.Description.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
            return NetworkInterfaceKind.VpnTunnel;
        }
        return NetworkInterfaceKind.Unknown;
    }

    private static bool DetectVpnProcesses()
    {
        try
        {
            foreach (var proc in VpnProcessNames)
            {
                if (Process.GetProcessesByName(proc).Length > 0) return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool DetectProxyEnv() => !string.IsNullOrEmpty(GetProxyUrlFromEnv());
}
