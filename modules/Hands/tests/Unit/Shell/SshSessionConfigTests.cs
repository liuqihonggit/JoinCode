
namespace Core.Tests.Ssh;

public sealed class SshSessionConfigTests
{
    [Fact]
    public void Constructor_SetsDefaults()
    {
        var config = new SshSessionConfig
        {
            Host = "example.com",
            Username = "user"
        };

        config.Port.Should().Be(22);
        config.AuthMethod.Should().Be(SshAuthMethod.PrivateKey);
        config.KnownHostsPolicy.Should().Be(SshKnownHostsPolicy.AcceptNew);
        config.ConnectionTimeoutMs.Should().Be(30000);
        config.KeepAliveIntervalMs.Should().Be(30000);
        config.MaxReconnectAttempts.Should().Be(10);
        config.ReconnectDelayMs.Should().Be(1000);
        config.MaxReconnectDelayMs.Should().Be(30000);
        config.AutoReconnect.Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithCustomValues_SetsProperties()
    {
        var config = new SshSessionConfig
        {
            Host = "server.example.com",
            Port = 2222,
            Username = "admin",
            AuthMethod = SshAuthMethod.Password,
            Password = "secret",
            KnownHostsPolicy = SshKnownHostsPolicy.Strict,
            ConnectionTimeoutMs = 5000,
            KeepAliveIntervalMs = 15000,
            MaxReconnectAttempts = 5,
            AutoReconnect = false
        };

        config.Host.Should().Be("server.example.com");
        config.Port.Should().Be(2222);
        config.Username.Should().Be("admin");
        config.AuthMethod.Should().Be(SshAuthMethod.Password);
        config.Password.Should().Be("secret");
        config.KnownHostsPolicy.Should().Be(SshKnownHostsPolicy.Strict);
        config.ConnectionTimeoutMs.Should().Be(5000);
        config.KeepAliveIntervalMs.Should().Be(15000);
        config.MaxReconnectAttempts.Should().Be(5);
        config.AutoReconnect.Should().BeFalse();
    }

    [Theory]
    [InlineData(SshAuthMethod.Password)]
    [InlineData(SshAuthMethod.PrivateKey)]
    [InlineData(SshAuthMethod.SshAgent)]
    [InlineData(SshAuthMethod.Certificate)]
    public void AuthMethod_AllValues_AreValid(SshAuthMethod method)
    {
        var config = new SshSessionConfig
        {
            Host = "example.com",
            Username = "user",
            AuthMethod = method
        };

        config.AuthMethod.Should().Be(method);
    }

    [Theory]
    [InlineData(SshKnownHostsPolicy.Strict)]
    [InlineData(SshKnownHostsPolicy.AcceptNew)]
    [InlineData(SshKnownHostsPolicy.Ignore)]
    public void KnownHostsPolicy_AllValues_AreValid(SshKnownHostsPolicy policy)
    {
        var config = new SshSessionConfig
        {
            Host = "example.com",
            Username = "user",
            KnownHostsPolicy = policy
        };

        config.KnownHostsPolicy.Should().Be(policy);
    }
}
