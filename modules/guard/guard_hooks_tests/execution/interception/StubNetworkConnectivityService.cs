namespace Guard.Tests.Hooks.Execution.Interception;

/// <summary>
/// 可控的网络连通性假实现 — 让 VPN 相关断言与运行机器的代理/VPN 状态解耦。
/// <para>
/// 守卫的 fallback 检测会读进程名与 HTTP_PROXY/HTTPS_PROXY，开发机上有代理进程时
/// 「无 VPN 时应返回 false」这条断言恒失败 —— 那是机器状态，不是代码缺陷。
/// 注入本类后断言只依赖显式给定的状态。
/// </para>
/// </summary>
internal sealed class StubNetworkConnectivityService(bool vpnActive) : INetworkConnectivityService {
    /// <inheritdoc />
    public NetworkConnectivityState CurrentState =>
        vpnActive ? NetworkConnectivityState.OnlineWithVpn : NetworkConnectivityState.Online;

    /// <inheritdoc />
    public bool IsNetworkAvailable() => true;

    /// <inheritdoc />
    public bool IsVpnActive() => vpnActive;

    /// <inheritdoc />
    public IReadOnlyList<NetworkInterfaceInfo> GetActiveInterfaces() => [];

    /// <inheritdoc />
    public NetworkRoute GetCurrentRoute() => new() {
        Type = vpnActive ? NetworkRouteType.Vpn : NetworkRouteType.Direct,
    };

    /// <inheritdoc />
    public event EventHandler<NetworkConnectivityChangedEventArgs>? StateChanged {
        add { }
        remove { }
    }
}
