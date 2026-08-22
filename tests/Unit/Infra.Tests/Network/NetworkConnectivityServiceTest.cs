namespace Infra.Services.Tests.Network;

/// <summary>
/// NetworkConnectivityService 单元测试 — 验证状态计算、VPN识别、多流接口、路由判断、事件触发
/// </summary>
public sealed class NetworkConnectivityServiceTest
{
    private static NetworkConnectivityService CreateSut(
        IReadOnlyList<NetworkInterfaceInfo> interfaces,
        bool vpnProcess = false,
        bool proxyEnv = false) =>
        new(
            interfaceProvider: () => interfaces,
            vpnProcessDetector: () => vpnProcess,
            proxyEnvDetector: () => proxyEnv);

    private static NetworkInterfaceInfo Iface(string name, NetworkInterfaceKind kind, bool up = true) =>
        new() { Name = name, Kind = kind, IsUp = up };

    [Fact]
    public void Constructor_NoInterfaces_OfflineState()
    {
        var sut = CreateSut([]);
        sut.CurrentState.Should().Be(NetworkConnectivityState.Offline);
        sut.IsNetworkAvailable().Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithEthernetInterface_OnlineState()
    {
        var sut = CreateSut([Iface("eth0", NetworkInterfaceKind.Ethernet)]);
        sut.CurrentState.Should().Be(NetworkConnectivityState.Online);
        sut.IsNetworkAvailable().Should().BeTrue();
        sut.IsVpnActive().Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithVpnTunnelInterface_OnlineWithVpnState()
    {
        var sut = CreateSut([
            Iface("eth0", NetworkInterfaceKind.Ethernet),
            Iface("tun0", NetworkInterfaceKind.VpnTunnel),
        ]);
        sut.CurrentState.Should().Be(NetworkConnectivityState.OnlineWithVpn);
        sut.IsVpnActive().Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithVpnProcess_OnlineWithVpnState()
    {
        var sut = CreateSut(
            [Iface("eth0", NetworkInterfaceKind.Ethernet)],
            vpnProcess: true);
        sut.CurrentState.Should().Be(NetworkConnectivityState.OnlineWithVpn);
        sut.IsVpnActive().Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithProxyEnv_OnlineWithProxyState()
    {
        var sut = CreateSut(
            [Iface("eth0", NetworkInterfaceKind.Ethernet)],
            proxyEnv: true);
        sut.CurrentState.Should().Be(NetworkConnectivityState.OnlineWithProxy);
    }

    [Fact]
    public void Constructor_VpnTakesPrecedenceOverProxy()
    {
        var sut = CreateSut(
            [Iface("tun0", NetworkInterfaceKind.VpnTunnel)],
            vpnProcess: true,
            proxyEnv: true);
        sut.CurrentState.Should().Be(NetworkConnectivityState.OnlineWithVpn);
    }

    [Fact]
    public void GetActiveInterfaces_ReturnsProvidedInterfaces()
    {
        var interfaces = new List<NetworkInterfaceInfo>
        {
            Iface("eth0", NetworkInterfaceKind.Ethernet),
            Iface("wlan0", NetworkInterfaceKind.Wireless),
            Iface("tun0", NetworkInterfaceKind.VpnTunnel),
        };
        var sut = CreateSut(interfaces);
        sut.GetActiveInterfaces().Should().HaveCount(3);
    }

    [Fact]
    public void GetCurrentRoute_WhenVpnActive_ReturnsVpnRoute()
    {
        var sut = CreateSut(
            [Iface("tun0", NetworkInterfaceKind.VpnTunnel)],
            vpnProcess: true);
        sut.GetCurrentRoute().Type.Should().Be(NetworkRouteType.Vpn);
    }

    [Fact]
    public void GetCurrentRoute_WhenProxyConfigured_ReturnsProxyRoute()
    {
        var sut = CreateSut(
            [Iface("eth0", NetworkInterfaceKind.Ethernet)],
            proxyEnv: true);
        sut.GetCurrentRoute().Type.Should().Be(NetworkRouteType.Proxy);
    }

    [Fact]
    public void GetCurrentRoute_WhenDirect_ReturnsDirectRoute()
    {
        var sut = CreateSut([Iface("eth0", NetworkInterfaceKind.Ethernet)]);
        sut.GetCurrentRoute().Type.Should().Be(NetworkRouteType.Direct);
    }

    [Fact]
    public void GetCurrentRoute_WhenOffline_ReturnsDirectRoute()
    {
        var sut = CreateSut([]);
        sut.GetCurrentRoute().Type.Should().Be(NetworkRouteType.Direct);
    }

    [Fact]
    public void RefreshState_WhenStateChanges_RaisesStateChangedEvent()
    {
        var interfaces = new List<NetworkInterfaceInfo>();
        var sut = new NetworkConnectivityService(
            interfaceProvider: () => interfaces,
            vpnProcessDetector: () => false,
            proxyEnvDetector: () => false);

        NetworkConnectivityChangedEventArgs? received = null;
        sut.StateChanged += (_, e) => received = e;

        interfaces.Add(Iface("eth0", NetworkInterfaceKind.Ethernet));
        sut.RefreshState();

        received.Should().NotBeNull();
        received!.PreviousState.Should().Be(NetworkConnectivityState.Offline);
        received.CurrentState.Should().Be(NetworkConnectivityState.Online);
    }

    [Fact]
    public void RefreshState_WhenStateUnchanged_DoesNotRaiseEvent()
    {
        var sut = CreateSut([Iface("eth0", NetworkInterfaceKind.Ethernet)]);
        var raised = false;
        sut.StateChanged += (_, _) => raised = true;

        sut.RefreshState();

        raised.Should().BeFalse();
    }

    [Fact]
    public void Constructor_OnlyLoopback_OfflineState()
    {
        var sut = CreateSut([Iface("lo", NetworkInterfaceKind.Loopback)]);
        sut.CurrentState.Should().Be(NetworkConnectivityState.Offline);
        sut.IsNetworkAvailable().Should().BeFalse();
    }

    [Fact]
    public void GetCurrentRoute_WhenVpnViaInterface_ReturnsViaInterfaceName()
    {
        var sut = CreateSut([Iface("tun0", NetworkInterfaceKind.VpnTunnel)], vpnProcess: true);
        var route = sut.GetCurrentRoute();
        route.Type.Should().Be(NetworkRouteType.Vpn);
        route.ViaInterface.Should().Be("tun0");
    }
}
