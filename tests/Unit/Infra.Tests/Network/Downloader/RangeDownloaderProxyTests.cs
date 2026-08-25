namespace Infra.Services.Tests.Network.Downloader;

/// <summary>
/// RangeDownloader 代理解析单元测试 — 验证优先级:options > 环境变量 > VPN/代理路由
/// </summary>
public sealed class RangeDownloaderProxyTests
{
    // === options 显式指定(最高优先级)===

    [Fact]
    public void ResolveProxyUrl_OptionsProxy_ReturnsOptionsProxy()
    {
        var result = RangeDownloader.ResolveProxyUrl("http://options:8080", networkService: null);
        result.Should().Be("http://options:8080");
    }

    [Fact]
    public void ResolveProxyUrl_OptionsProxy_OverridesEnvironment()
    {
        using var env = new EnvScope("HTTPS_PROXY", "http://env:8080");
        var result = RangeDownloader.ResolveProxyUrl("http://options:8080", networkService: null);
        result.Should().Be("http://options:8080");
    }

    // === 环境变量回退 ===

    [Fact]
    public void ResolveProxyUrl_NoOptions_UsesHttpsProxyEnv()
    {
        using var env = new EnvScope("HTTPS_PROXY", "http://env-https:8080");
        var result = RangeDownloader.ResolveProxyUrl(null, networkService: null);
        result.Should().Be("http://env-https:8080");
    }

    [Fact]
    public void ResolveProxyUrl_NoOptions_UsesHttpProxyEnv()
    {
        using var env1 = new EnvScope("HTTPS_PROXY", null);
        using var env2 = new EnvScope("HTTP_PROXY", "http://env-http:8080");
        var result = RangeDownloader.ResolveProxyUrl(null, networkService: null);
        result.Should().Be("http://env-http:8080");
    }

    // === VPN/代理路由识别 ===

    [Fact]
    public void ResolveProxyUrl_NoOptionsNoEnv_UsesVpnRouteProxy()
    {
        using var env1 = new EnvScope("HTTPS_PROXY", null);
        using var env2 = new EnvScope("HTTP_PROXY", null);
        var route = new NetworkRoute { Type = NetworkRouteType.Proxy, ProxyUrl = "http://vpn-proxy:8080" };
        var mockNetwork = new Mock<INetworkConnectivityService>();
        mockNetwork.Setup(n => n.GetCurrentRoute()).Returns(route);

        var result = RangeDownloader.ResolveProxyUrl(null, mockNetwork.Object);

        result.Should().Be("http://vpn-proxy:8080");
    }

    [Fact]
    public void ResolveProxyUrl_VpnRouteNoProxy_ReturnsNull()
    {
        var route = new NetworkRoute { Type = NetworkRouteType.Vpn };
        var mockNetwork = new Mock<INetworkConnectivityService>();
        mockNetwork.Setup(n => n.GetCurrentRoute()).Returns(route);

        var result = RangeDownloader.ResolveProxyUrl(null, mockNetwork.Object);

        result.Should().BeNull();
    }

    // === 全空 → null ===

    [Fact]
    public void ResolveProxyUrl_AllNull_ReturnsNull()
    {
        using var env1 = new EnvScope("HTTPS_PROXY", null);
        using var env2 = new EnvScope("HTTP_PROXY", null);
        var result = RangeDownloader.ResolveProxyUrl(null, networkService: null);
        result.Should().BeNull();
    }

    // === 环境变量优先于 VPN 路由 ===

    [Fact]
    public void ResolveProxyUrl_EnvOverridesVpnRoute()
    {
        using var env = new EnvScope("HTTPS_PROXY", "http://env:8080");
        var route = new NetworkRoute { Type = NetworkRouteType.Proxy, ProxyUrl = "http://vpn:8080" };
        var mockNetwork = new Mock<INetworkConnectivityService>();
        mockNetwork.Setup(n => n.GetCurrentRoute()).Returns(route);

        var result = RangeDownloader.ResolveProxyUrl(null, mockNetwork.Object);

        result.Should().Be("http://env:8080");
    }

    /// <summary>
    /// 环境变量作用域 — Dispose 时恢复原值
    /// </summary>
    private sealed class EnvScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _original;

        internal EnvScope(string name, string? value)
        {
            _name = name;
            _original = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _original);
    }
}
