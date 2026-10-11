namespace Infra.IO.Tests;

public sealed class GitHubApiClientPortCheckTests {
    [Theory]
    [InlineData("https://api.github.com/", "api.github.com", 443)]
    [InlineData("https://api.github.com", "api.github.com", 443)]
    [InlineData("http://api.github.com/", "api.github.com", 80)]
    [InlineData("https://api.github.com:8080/", "api.github.com", 8080)]
    [InlineData("https://ssh.github.com:443/", "ssh.github.com", 443)]
    public void ExtractHostPort_VariousUrls_ReturnsCorrectHostPort(string url, string expectedHost, int expectedPort) {
        var uri = new Uri(url);
        var (host, port) = GitHubApiClient.ExtractHostPort(uri);
        host.Should().Be(expectedHost);
        port.Should().Be(expectedPort);
    }

    [Fact]
    public async Task IsPortReachableAsync_UnreachablePort_ReturnsFalse() {
        var result = await GitHubApiClient.IsPortReachableAsync("127.0.0.1", 1, CancellationToken.None);
        result.Should().BeFalse();
    }

    [SkippableFact]
    public async Task IsPortReachableAsync_ReachablePort_ReturnsTrue() {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        try {
            var result = await GitHubApiClient.IsPortReachableAsync("127.0.0.1", port, CancellationToken.None);
            result.Should().BeTrue();
        } finally {
            listener.Stop();
        }
    }

    [Fact]
    public async Task BuildNetworkErrorMessageAsync_WithNullBaseAddress_ReturnsRawMessage() {
        var handler = new HttpClientHandler();
        using var client = new HttpClient(handler) { BaseAddress = null };
        await using var apiClient = new GitHubApiClient(client, new InMemoryFileSystem());
        var ex = new Exception("test error");
        var msg = await apiClient.BuildNetworkErrorMessageAsync(ex, CancellationToken.None);
        msg.Should().Contain("test error");
    }

    [Fact]
    public async Task BuildNetworkErrorMessageAsync_UnreachablePort_ContainsPortInfo() {
        using var client = new HttpClient { BaseAddress = new Uri("https://127.0.0.1:1/") };
        await using var apiClient = new GitHubApiClient(client, new InMemoryFileSystem());
        var ex = new Exception("connection refused");
        var msg = await apiClient.BuildNetworkErrorMessageAsync(ex, CancellationToken.None);
        msg.Should().Contain("端口不可达");
        msg.Should().Contain("127.0.0.1:1");
    }
}
