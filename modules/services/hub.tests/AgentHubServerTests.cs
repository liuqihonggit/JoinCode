namespace Hub.Tests;

public class AgentHubServerTests : IAsyncDisposable {
    private AgentHubServer? _server;
    private int _port;
    private int _disposed;

    private async Task<AgentHubServer> StartTestServerAsync() {
        _port = GetAvailablePort();
        var settings = new SettingsJson {
            Vendor = new Dictionary<string, ProfileSettings> {
                ["test-provider"] = new ProfileSettings {
                    Provider = "test-provider",
                    Protocol = ProtocolKindEnumConstants.OpenAiCompatible,
                    Endpoint = "http://localhost:9999/v1",
                    ApiKeyEnvVar = "TEST_API_KEY",
                    Model = "test-model"
                }
            },
            Current = new CurrentSettings { Profile = "test-provider" }
        };

        var registry = ProviderDefinitionRegistry.Create(new ModelConfigLoader(), null, null);
        _server = new AgentHubServer(settings, registry, _port);
        _server.Start();

        var sw = new SpinWait();
        while (!_server.IsRunning) sw.SpinOnce();

        return _server;
    }

    private static int GetAvailablePort() {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public async Task Start_ServerBeginsListening() {
        await StartTestServerAsync();

        if (_server is null) throw new InvalidOperationException("Server not started");
        _server.IsRunning.Should().BeTrue();
        _server.Port.Should().Be(_port);
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsOkStatus() {
        await StartTestServerAsync();

        using var client = new HttpClient();
        using var response = await client.GetAsync($"http://localhost:{_port}/health");

        response.IsSuccessStatusCode.Should().BeTrue();
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("ok");
    }

    [Fact]
    public async Task AgentRegister_RegistersAgent() {
        await StartTestServerAsync();

        using var client = new HttpClient();
        using var response = await client.PostAsync(
            $"http://localhost:{_port}/agent/register",
            new StringContent("""{"agent_id":"test-agent","session_id":"s1"}""", Encoding.UTF8, "application/json"));

        response.IsSuccessStatusCode.Should().BeTrue();
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("ok");
        content.Should().Contain("test-agent");
    }

    [Fact]
    public async Task AgentsEndpoint_ReturnsRegisteredAgents() {
        await StartTestServerAsync();

        using var client = new HttpClient();
        using var regResponse = await client.PostAsync(
            $"http://localhost:{_port}/agent/register",
            new StringContent("""{"agent_id":"test-agent"}""", Encoding.UTF8, "application/json"));

        using var response = await client.GetAsync($"http://localhost:{_port}/agents");

        response.IsSuccessStatusCode.Should().BeTrue();
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("test-agent");
    }

    [Fact]
    public async Task AgentUnregister_RemovesAgent() {
        await StartTestServerAsync();

        using var client = new HttpClient();
        using var regResponse = await client.PostAsync(
            $"http://localhost:{_port}/agent/register",
            new StringContent("""{"agent_id":"test-agent"}""", Encoding.UTF8, "application/json"));

        using var unregResponse = await client.PostAsync(
            $"http://localhost:{_port}/agent/unregister",
            new StringContent("""{"agent_id":"test-agent"}""", Encoding.UTF8, "application/json"));

        using var response = await client.GetAsync($"http://localhost:{_port}/agents");
        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotContain("test-agent");
    }

    [Fact]
    public async Task UnknownPath_Returns404() {
        await StartTestServerAsync();

        using var client = new HttpClient();
        using var response = await client.GetAsync($"http://localhost:{_port}/unknown");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        if (_server != null) {
            await _server.StopAsync();
            await _server.DisposeAsync();
            _server = null;
        }
    }
}
