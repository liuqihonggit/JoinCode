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
    public async Task ModelsEndpoint_ReturnsModelList() {
        await StartTestServerAsync();

        using var client = new HttpClient();
        using var response = await client.GetAsync($"http://localhost:{_port}/v1/models");

        response.IsSuccessStatusCode.Should().BeTrue();
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("test-model");
        content.Should().Contain("\"data\"");
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsProviderCount() {
        await StartTestServerAsync();

        using var client = new HttpClient();
        using var response = await client.GetAsync($"http://localhost:{_port}/health");

        response.IsSuccessStatusCode.Should().BeTrue();
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"providers\":1");
    }

    [Fact]
    public async Task AgentRoute_WithMatchingCapability_ReturnsDelivered() {
        await StartTestServerAsync();

        using var client = new HttpClient();
        await client.PostAsync(
            $"http://localhost:{_port}/agent/register",
            new StringContent("""{"agent_id":"worker-1","capabilities":["code-review"]}""", Encoding.UTF8, "application/json"));

        using var response = await client.PostAsync(
            $"http://localhost:{_port}/agent/route",
            new StringContent("""{"capability":"code-review","from_agent_id":"orchestrator","content":"review this"}""", Encoding.UTF8, "application/json"));

        response.IsSuccessStatusCode.Should().BeTrue();
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("delivered");
        content.Should().Contain("worker-1");
    }

    [Fact]
    public async Task AgentRoute_NoMatchingCapability_ReturnsNoAgent() {
        await StartTestServerAsync();

        using var client = new HttpClient();
        await client.PostAsync(
            $"http://localhost:{_port}/agent/register",
            new StringContent("""{"agent_id":"worker-1","capabilities":["code-review"]}""", Encoding.UTF8, "application/json"));

        using var response = await client.PostAsync(
            $"http://localhost:{_port}/agent/route",
            new StringContent("""{"capability":"deployment","from_agent_id":"orchestrator","content":"deploy this"}""", Encoding.UTF8, "application/json"));

        response.IsSuccessStatusCode.Should().BeTrue();
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("no_agent");
    }

    [Fact]
    public async Task AgentMessage_DeliversToTargetAgent() {
        await StartTestServerAsync();

        using var client = new HttpClient();
        await client.PostAsync(
            $"http://localhost:{_port}/agent/register",
            new StringContent("""{"agent_id":"agent-1"}""", Encoding.UTF8, "application/json"));
        await client.PostAsync(
            $"http://localhost:{_port}/agent/register",
            new StringContent("""{"agent_id":"agent-2"}""", Encoding.UTF8, "application/json"));

        using var response = await client.PostAsync(
            $"http://localhost:{_port}/agent/message",
            new StringContent("""{"from_agent_id":"agent-1","to_agent_id":"agent-2","content":"hello"}""", Encoding.UTF8, "application/json"));

        response.IsSuccessStatusCode.Should().BeTrue();
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("delivered");
    }

    [Fact]
    public async Task AgentMessage_BroadcastsToAllExceptSender() {
        await StartTestServerAsync();

        using var client = new HttpClient();
        await client.PostAsync(
            $"http://localhost:{_port}/agent/register",
            new StringContent("""{"agent_id":"agent-1"}""", Encoding.UTF8, "application/json"));
        await client.PostAsync(
            $"http://localhost:{_port}/agent/register",
            new StringContent("""{"agent_id":"agent-2"}""", Encoding.UTF8, "application/json"));
        await client.PostAsync(
            $"http://localhost:{_port}/agent/register",
            new StringContent("""{"agent_id":"agent-3"}""", Encoding.UTF8, "application/json"));

        using var response = await client.PostAsync(
            $"http://localhost:{_port}/agent/message",
            new StringContent("""{"from_agent_id":"agent-1","content":"broadcast msg"}""", Encoding.UTF8, "application/json"));

        response.IsSuccessStatusCode.Should().BeTrue();
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("broadcast");
    }

    [Fact]
    public async Task AgentMessage_ToUnregisteredAgent_ReturnsAgentNotFound() {
        await StartTestServerAsync();

        using var client = new HttpClient();

        using var response = await client.PostAsync(
            $"http://localhost:{_port}/agent/message",
            new StringContent("""{"from_agent_id":"agent-1","to_agent_id":"nonexistent","content":"hello"}""", Encoding.UTF8, "application/json"));

        response.IsSuccessStatusCode.Should().BeTrue();
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("agent_not_found");
    }

    [Fact]
    public async Task AgentRegister_WithCapabilities_ReturnsOk() {
        await StartTestServerAsync();

        using var client = new HttpClient();
        using var response = await client.PostAsync(
            $"http://localhost:{_port}/agent/register",
            new StringContent("""{"agent_id":"worker-1","capabilities":["code-review","testing"]}""", Encoding.UTF8, "application/json"));

        response.IsSuccessStatusCode.Should().BeTrue();
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("ok");
        content.Should().Contain("worker-1");
    }

    [Fact]
    public async Task ModelsEndpoint_ReturnsModelsFromMultipleProviders() {
        _port = GetAvailablePort();
        var settings = new SettingsJson {
            Vendor = new Dictionary<string, ProfileSettings> {
                ["provider-a"] = new ProfileSettings {
                    Provider = "provider-a",
                    Protocol = ProtocolKindEnumConstants.OpenAiCompatible,
                    Endpoint = "http://localhost:9998/v1",
                    ApiKeyEnvVar = "KEY_A",
                    Model = "model-a"
                },
                ["provider-b"] = new ProfileSettings {
                    Provider = "provider-b",
                    Protocol = ProtocolKindEnumConstants.OpenAiCompatible,
                    Endpoint = "http://localhost:9997/v1",
                    ApiKeyEnvVar = "KEY_B",
                    Model = "model-b"
                }
            },
            Current = new CurrentSettings { Profile = "provider-a" }
        };

        var registry = ProviderDefinitionRegistry.Create(new ModelConfigLoader(), null, null);
        _server = new AgentHubServer(settings, registry, _port);
        _server.Start();
        var sw = new SpinWait();
        while (!_server.IsRunning) sw.SpinOnce();

        using var client = new HttpClient();
        using var response = await client.GetAsync($"http://localhost:{_port}/v1/models");

        response.IsSuccessStatusCode.Should().BeTrue();
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("model-a");
        content.Should().Contain("model-b");
        content.Should().Contain("provider-a");
        content.Should().Contain("provider-b");
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
