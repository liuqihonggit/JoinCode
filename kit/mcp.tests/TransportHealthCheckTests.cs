namespace Mcp.Tests;

public sealed class TransportHealthCheckTests
{
    [Fact]
    public async Task StdioHealthCheck_NoCommand_ReturnsConfigMissing()
    {
        var fs = new InMemoryFileSystem();
        var check = new StdioHealthCheck(null, fs);
        var result = await check.CheckAsync();
        result.IsAvailable.Should().BeFalse();
        result.Category.Should().Be(TransportUnavailabilityCategory.ConfigMissing);
    }

    [Fact]
    public async Task StdioHealthCheck_EmptyCommand_ReturnsConfigMissing()
    {
        var fs = new InMemoryFileSystem();
        var check = new StdioHealthCheck("", fs);
        var result = await check.CheckAsync();
        result.IsAvailable.Should().BeFalse();
        result.Category.Should().Be(TransportUnavailabilityCategory.ConfigMissing);
    }

    [Fact]
    public async Task StdioHealthCheck_NonPathCommand_ReturnsAvailable()
    {
        var fs = new InMemoryFileSystem();
        var check = new StdioHealthCheck("npx", fs);
        var result = await check.CheckAsync();
        result.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task StdioHealthCheck_ExistingPathInMemoryFs_ReturnsAvailable()
    {
        var fs = new InMemoryFileSystem();
        var testPath = "/usr/local/bin/test-cmd";
        await fs.WriteAllTextAsync(testPath, "test");
        var check = new StdioHealthCheck(testPath, fs);
        var result = await check.CheckAsync();
        result.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task StdioHealthCheck_NonExistingPath_ReturnsConfigMissing()
    {
        var fs = new InMemoryFileSystem();
        var check = new StdioHealthCheck("/nonexistent/command.exe", fs);
        var result = await check.CheckAsync();
        result.IsAvailable.Should().BeFalse();
        result.Category.Should().Be(TransportUnavailabilityCategory.ConfigMissing);
    }

    [Fact]
    public async Task TcpPortHealthCheck_UnreachablePort_ReturnsNetworkUnreachable()
    {
        var check = new TcpPortHealthCheck("localhost", 1, "test");
        var result = await check.CheckAsync();
        result.IsAvailable.Should().BeFalse();
        result.Category.Should().Be(TransportUnavailabilityCategory.NetworkUnreachable);
    }

    [Fact]
    public async Task HttpListenerHealthCheck_InvalidPrefix_ReturnsUnavailable()
    {
        var check = new HttpListenerHealthCheck("http://invalid-host-that-does-not-exist:99999/");
        var result = await check.CheckAsync();
        result.IsAvailable.Should().BeFalse();
    }
}
