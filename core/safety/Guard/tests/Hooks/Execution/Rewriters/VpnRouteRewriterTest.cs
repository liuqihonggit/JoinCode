namespace Guard.Tests.Hooks.Execution.Rewriters;

/// <summary>
/// VpnRouteRewriter 单元测试 — 验证实时 VPN 检测、命令匹配、代理注入
/// </summary>
public sealed class VpnRouteRewriterTest
{
    private static INetworkConnectivityService CreateNetworkService(bool vpnActive)
    {
        var mock = new Mock<INetworkConnectivityService>();
        mock.Setup(x => x.IsVpnActive()).Returns(vpnActive);
        return mock.Object;
    }

    [Fact]
    public void Name_ReturnsExpectedName()
    {
        var sut = new VpnRouteRewriter();
        sut.Name.Should().Be("VpnRouteRewriter");
    }

    [Fact]
    public void Priority_Returns30()
    {
        var sut = new VpnRouteRewriter();
        sut.Priority.Should().Be(30);
    }

    [Theory]
    [InlineData("git push")]
    [InlineData("git clone https://example.com")]
    [InlineData("curl http://example.com")]
    [InlineData("wget http://example.com")]
    [InlineData("gh pr list")]
    public void CanRewrite_WhenVpnActive_AndNetworkCommand_ReturnsTrue(string command)
    {
        var sut = new VpnRouteRewriter(networkService: CreateNetworkService(vpnActive: true));
        sut.CanRewrite(command).Should().BeTrue();
    }

    [Theory]
    [InlineData("echo hello")]
    [InlineData("dotnet build")]
    [InlineData("npm install")]
    public void CanRewrite_WhenVpnActive_AndNonNetworkCommand_ReturnsFalse(string command)
    {
        var sut = new VpnRouteRewriter(networkService: CreateNetworkService(vpnActive: true));
        sut.CanRewrite(command).Should().BeFalse();
    }

    [Fact]
    public void CanRewrite_WhenVpnInactive_ReturnsFalse()
    {
        var sut = new VpnRouteRewriter(networkService: CreateNetworkService(vpnActive: false));
        sut.CanRewrite("git push").Should().BeFalse();
    }

    [Fact]
    public void Rewrite_GitCommand_AddsProxyConfig()
    {
        var sut = new VpnRouteRewriter();
        var context = new Dictionary<string, object> { ["proxy_url"] = "http://proxy:8080" };

        var result = sut.Rewrite("git push origin main", context);

        result.Should().Contain("http.proxy=http://proxy:8080");
        result.Should().Contain("https.proxy=http://proxy:8080");
        result.Should().StartWith("git -c");
    }

    [Fact]
    public void Rewrite_CurlCommand_AddsProxyFlag()
    {
        var sut = new VpnRouteRewriter();
        var context = new Dictionary<string, object> { ["proxy_url"] = "http://proxy:8080" };

        var result = sut.Rewrite("curl http://example.com", context);

        result.Should().Contain("--proxy http://proxy:8080");
    }

    [Fact]
    public void Rewrite_NoProxyUrlInContext_ReturnsUnchanged()
    {
        var sut = new VpnRouteRewriter();
        var context = new Dictionary<string, object>();

        var result = sut.Rewrite("git push", context);

        result.Should().Be("git push");
    }

    [Fact]
    public void Rewrite_GhCommand_ReturnsUnchanged()
    {
        var sut = new VpnRouteRewriter();
        var context = new Dictionary<string, object> { ["proxy_url"] = "http://proxy:8080" };

        var result = sut.Rewrite("gh pr list", context);

        result.Should().Be("gh pr list");
    }
}
