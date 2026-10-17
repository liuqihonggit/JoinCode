namespace Services.Web.Tests;

public sealed class PrivateNetworkGuardTests {
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.2")]
    [InlineData("127.255.255.255")]
    public void IsPrivateAddress_LoopbackV4_ReturnsTrue(string ip) {
        Assert.True(PrivateNetworkGuard.IsPrivateAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("10.0.0.0")]
    public void IsPrivateAddress_Private10_ReturnsTrue(string ip) {
        Assert.True(PrivateNetworkGuard.IsPrivateAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("172.16.0.0")]
    public void IsPrivateAddress_Private172_ReturnsTrue(string ip) {
        Assert.True(PrivateNetworkGuard.IsPrivateAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.1")]
    public void IsPrivateAddress_Public172_ReturnsFalse(string ip) {
        Assert.False(PrivateNetworkGuard.IsPrivateAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("192.168.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("192.168.255.255")]
    public void IsPrivateAddress_Private192_ReturnsTrue(string ip) {
        Assert.True(PrivateNetworkGuard.IsPrivateAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("169.254.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("169.254.255.255")]
    public void IsPrivateAddress_LinkLocal_ReturnsTrue(string ip) {
        Assert.True(PrivateNetworkGuard.IsPrivateAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("0.0.0.0")]
    public void IsPrivateAddress_ZeroNetwork_ReturnsTrue(string ip) {
        Assert.True(PrivateNetworkGuard.IsPrivateAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("203.0.113.1")]
    [InlineData("172.15.0.1")]
    public void IsPrivateAddress_PublicAddresses_ReturnsFalse(string ip) {
        Assert.False(PrivateNetworkGuard.IsPrivateAddress(IPAddress.Parse(ip)));
    }

    [Fact]
    public void IsPrivateAddress_IPv6Loopback_ReturnsTrue() {
        Assert.True(PrivateNetworkGuard.IsPrivateAddress(IPAddress.Parse("::1")));
    }

    [Fact]
    public void IsPrivateAddress_IPv6UniqueLocal_ReturnsTrue() {
        Assert.True(PrivateNetworkGuard.IsPrivateAddress(IPAddress.Parse("fc00::1")));
        Assert.True(PrivateNetworkGuard.IsPrivateAddress(IPAddress.Parse("fd00::1")));
    }

    [Fact]
    public void IsPrivateAddress_IPv6LinkLocal_ReturnsTrue() {
        Assert.True(PrivateNetworkGuard.IsPrivateAddress(IPAddress.Parse("fe80::1")));
    }

    [Fact]
    public void IsPrivateAddress_IPv6Public_ReturnsFalse() {
        Assert.False(PrivateNetworkGuard.IsPrivateAddress(IPAddress.Parse("2001:db8::1")));
    }

    [Fact]
    public void IsPrivateAddress_IPv4MappedToIPv6_Loopback_ReturnsTrue() {
        var mapped = IPAddress.Parse("::ffff:127.0.0.1");
        Assert.True(PrivateNetworkGuard.IsPrivateAddress(mapped));
    }

    [Fact]
    public void IsPrivateAddress_IPv4MappedToIPv6_Public_ReturnsFalse() {
        var mapped = IPAddress.Parse("::ffff:8.8.8.8");
        Assert.False(PrivateNetworkGuard.IsPrivateAddress(mapped));
    }

    // === ValidateHost 统一入口（B2: fail-closed + 重定向复检数据源, ADR 0140）===

    [Fact]
    public void ValidateHost_DirectPrivateIpLiteral_NotAllowed() {
        var decision = PrivateNetworkGuard.ValidateHost("10.9.8.7");
        Assert.False(decision.Allowed);
        Assert.NotNull(decision.Reason);
    }

    [Fact]
    public void ValidateHost_DirectPublicIpLiteral_Allowed() {
        var decision = PrivateNetworkGuard.ValidateHost("203.0.113.1");
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void ValidateHost_HostnameResolvesToPrivateIp_NotAllowed() {
        var decision = PrivateNetworkGuard.ValidateHost("rebind.evil.example", _ => [IPAddress.Parse("127.0.0.1")]);
        Assert.False(decision.Allowed);
        Assert.NotNull(decision.Reason);
    }

    [Fact]
    public void ValidateHost_HostnameResolvesToPublicIp_Allowed() {
        var decision = PrivateNetworkGuard.ValidateHost("safe.example.com", _ => [IPAddress.Parse("8.8.8.8")]);
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void ValidateHost_MultipleAddressesOnePrivate_NotAllowed() {
        // 多解析结果中任一私网 IP 即拒绝(攻击者常用多 A 记录混入内网地址)
        var decision = PrivateNetworkGuard.ValidateHost("multi.evil.example",
            _ => [IPAddress.Parse("8.8.8.8"), IPAddress.Parse("10.0.0.99")]);
        Assert.False(decision.Allowed);
    }

    [Fact]
    public void ValidateHost_DnsResolveFails_NotAllowed() {
        // fail-closed: DNS 解析失败必须拒绝,禁止放行(B2 缺陷: SocketException 曾被 catch 后放行)
        var decision = PrivateNetworkGuard.ValidateHost("nonexistent.invalid",
            _ => throw new SocketException((int)SocketError.HostNotFound));
        Assert.False(decision.Allowed);
        Assert.NotNull(decision.Reason);
    }

    [Fact]
    public void ValidateHost_IpLiteralSkipsDns_NotAllowed() {
        // IP 字面量不得触发 DNS(防御 DNS 返回伪造结果覆盖字面量判断)
        var called = false;
        var decision = PrivateNetworkGuard.ValidateHost("192.168.1.1", _ => {
            called = true;
            return [];
        });
        Assert.False(called);
        Assert.False(decision.Allowed);
    }
}