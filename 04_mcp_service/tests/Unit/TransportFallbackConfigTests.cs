namespace Mcp.Tests;

public sealed class TransportFallbackConfigTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var config = new TransportFallbackConfig();
        config.Enabled.Should().BeTrue();
        config.HealthCheckEnabled.Should().BeTrue();
        config.CircuitBreakerEnabled.Should().BeTrue();
        config.ConnectTimeoutMs.Should().Be(5000);
        config.ChainTimeoutMs.Should().Be(30000);
        config.HealthCheckTimeoutMs.Should().Be(2000);
        config.CircuitBreakerFailureThreshold.Should().Be(3);
        config.CircuitBreakerCoolDownMs.Should().Be(30000);
    }

    [Fact]
    public void FromEnvironment_Disabled_SetsEnabledFalse()
    {
        var prev = Environment.GetEnvironmentVariable("JCC_TRANSPORT_FALLBACK");
        try
        {
            Environment.SetEnvironmentVariable("JCC_TRANSPORT_FALLBACK", "0");
            var config = TransportFallbackConfig.FromEnvironment();
            config.Enabled.Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("JCC_TRANSPORT_FALLBACK", prev);
        }
    }

    [Fact]
    public void FromEnvironment_CustomTimeout_ParsesCorrectly()
    {
        var prev = Environment.GetEnvironmentVariable("JCC_TRANSPORT_CONNECT_TIMEOUT_MS");
        try
        {
            Environment.SetEnvironmentVariable("JCC_TRANSPORT_CONNECT_TIMEOUT_MS", "10000");
            var config = TransportFallbackConfig.FromEnvironment();
            config.ConnectTimeoutMs.Should().Be(10000);
        }
        finally
        {
            Environment.SetEnvironmentVariable("JCC_TRANSPORT_CONNECT_TIMEOUT_MS", prev);
        }
    }

    [Fact]
    public void FromEnvironment_InvalidTimeout_UsesDefault()
    {
        var prev = Environment.GetEnvironmentVariable("JCC_TRANSPORT_CONNECT_TIMEOUT_MS");
        try
        {
            Environment.SetEnvironmentVariable("JCC_TRANSPORT_CONNECT_TIMEOUT_MS", "invalid");
            var config = TransportFallbackConfig.FromEnvironment();
            config.ConnectTimeoutMs.Should().Be(5000);
        }
        finally
        {
            Environment.SetEnvironmentVariable("JCC_TRANSPORT_CONNECT_TIMEOUT_MS", prev);
        }
    }
}
