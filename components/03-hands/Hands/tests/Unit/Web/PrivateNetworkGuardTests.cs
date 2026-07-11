namespace Services.Web.Tests;

public sealed class PrivateNetworkGuardTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.2")]
    [InlineData("127.255.255.255")]
    public void IsPrivateAddress_LoopbackV4_ReturnsTrue(string ip)
    {
        Assert.True(PrivateNetworkGuard.IsPrivateAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("10.0.0.0")]
    public void IsPrivateAddress_Private10_ReturnsTrue(string ip)
    {
        Assert.True(PrivateNetworkGuard.IsPrivateAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("172.16.0.0")]
    public void IsPrivateAddress_Private172_ReturnsTrue(string ip)
    {
        Assert.True(PrivateNetworkGuard.IsPrivateAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.1")]
    public void IsPrivateAddress_Public172_ReturnsFalse(string ip)
    {
        Assert.False(PrivateNetworkGuard.IsPrivateAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("192.168.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("192.168.255.255")]
    public void IsPrivateAddress_Private192_ReturnsTrue(string ip)
    {
        Assert.True(PrivateNetworkGuard.IsPrivateAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("169.254.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("169.254.255.255")]
    public void IsPrivateAddress_LinkLocal_ReturnsTrue(string ip)
    {
        Assert.True(PrivateNetworkGuard.IsPrivateAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("0.0.0.0")]
    public void IsPrivateAddress_ZeroNetwork_ReturnsTrue(string ip)
    {
        Assert.True(PrivateNetworkGuard.IsPrivateAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("203.0.113.1")]
    [InlineData("172.15.0.1")]
    public void IsPrivateAddress_PublicAddresses_ReturnsFalse(string ip)
    {
        Assert.False(PrivateNetworkGuard.IsPrivateAddress(IPAddress.Parse(ip)));
    }

    [Fact]
    public void IsPrivateAddress_IPv6Loopback_ReturnsTrue()
    {
        Assert.True(PrivateNetworkGuard.IsPrivateAddress(IPAddress.Parse("::1")));
    }

    [Fact]
    public void IsPrivateAddress_IPv6UniqueLocal_ReturnsTrue()
    {
        Assert.True(PrivateNetworkGuard.IsPrivateAddress(IPAddress.Parse("fc00::1")));
        Assert.True(PrivateNetworkGuard.IsPrivateAddress(IPAddress.Parse("fd00::1")));
    }

    [Fact]
    public void IsPrivateAddress_IPv6LinkLocal_ReturnsTrue()
    {
        Assert.True(PrivateNetworkGuard.IsPrivateAddress(IPAddress.Parse("fe80::1")));
    }

    [Fact]
    public void IsPrivateAddress_IPv6Public_ReturnsFalse()
    {
        Assert.False(PrivateNetworkGuard.IsPrivateAddress(IPAddress.Parse("2001:db8::1")));
    }

    [Fact]
    public void IsPrivateAddress_IPv4MappedToIPv6_Loopback_ReturnsTrue()
    {
        var mapped = IPAddress.Parse("::ffff:127.0.0.1");
        Assert.True(PrivateNetworkGuard.IsPrivateAddress(mapped));
    }

    [Fact]
    public void IsPrivateAddress_IPv4MappedToIPv6_Public_ReturnsFalse()
    {
        var mapped = IPAddress.Parse("::ffff:8.8.8.8");
        Assert.False(PrivateNetworkGuard.IsPrivateAddress(mapped));
    }
}
