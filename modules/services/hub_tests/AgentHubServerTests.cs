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

    [Fact]
    public async Task AgentHeartbeat_UpdatesHeartbeatForRegisteredAgent() {
        await StartTestServerAsync();

        using var client = new HttpClient();
        await client.PostAsync(
            $"http://localhost:{_port}/agent/register",
            new StringContent("""{"agent_id":"agent-1"}""", Encoding.UTF8, "application/json"));

        using var hbResponse = await client.PostAsync(
            $"http://localhost:{_port}/agent/heartbeat",
            new StringContent("""{"agent_id":"agent-1"}""", Encoding.UTF8, "application/json"));

        hbResponse.IsSuccessStatusCode.Should().BeTrue();
        var content = await hbResponse.Content.ReadAsStringAsync();
        content.Should().Contain("ok");
    }

    [Fact]
    public async Task AgentHeartbeat_UnregisteredAgent_Returns404() {
        await StartTestServerAsync();

        using var client = new HttpClient();
        using var response = await client.PostAsync(
            $"http://localhost:{_port}/agent/heartbeat",
            new StringContent("""{"agent_id":"nonexistent"}""", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AgentHeartbeat_EmptyAgentId_Returns400() {
        await StartTestServerAsync();

        using var client = new HttpClient();
        using var response = await client.PostAsync(
            $"http://localhost:{_port}/agent/heartbeat",
            new StringContent("""{"agent_id":""}""", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ApplyReload_AddsNewProviderToModelsEndpoint() {
        await StartTestServerAsync();

        using var client = new HttpClient();

        // 初始状态：只有 test-model
        using var response1 = await client.GetAsync($"http://localhost:{_port}/v1/models");
        var content1 = await response1.Content.ReadAsStringAsync();
        content1.Should().Contain("test-model");
        content1.Should().NotContain("new-model");

        // 热重载：添加新 provider
        var newSettings = new SettingsJson {
            Vendor = new Dictionary<string, ProfileSettings> {
                ["test-provider"] = new ProfileSettings {
                    Provider = "test-provider",
                    Protocol = ProtocolKindEnumConstants.OpenAiCompatible,
                    Endpoint = "http://localhost:9999/v1",
                    ApiKeyEnvVar = "TEST_API_KEY",
                    Model = "test-model"
                },
                ["new-provider"] = new ProfileSettings {
                    Provider = "new-provider",
                    Protocol = ProtocolKindEnumConstants.OpenAiCompatible,
                    Endpoint = "http://localhost:9996/v1",
                    ApiKeyEnvVar = "NEW_API_KEY",
                    Model = "new-model"
                }
            },
            Current = new CurrentSettings { Profile = "test-provider" }
        };
        var newRegistry = ProviderDefinitionRegistry.Create(new ModelConfigLoader(), null, null);
        if (_server is null) throw new InvalidOperationException("Server not started");
        _server.ApplyReload(newSettings, newRegistry);

        // 热重载后：应该包含 new-model
        using var response2 = await client.GetAsync($"http://localhost:{_port}/v1/models");
        var content2 = await response2.Content.ReadAsStringAsync();
        content2.Should().Contain("test-model");
        content2.Should().Contain("new-model");
        content2.Should().Contain("new-provider");
    }

    [Fact]
    public async Task ApplyReload_RemovesDeletedProviderFromModelsEndpoint() {
        await StartTestServerAsync();

        using var client = new HttpClient();

        // 初始状态：有 test-model
        using var response1 = await client.GetAsync($"http://localhost:{_port}/v1/models");
        var content1 = await response1.Content.ReadAsStringAsync();
        content1.Should().Contain("test-model");

        // 热重载：删除 test-provider，替换为另一个 provider
        var newSettings = new SettingsJson {
            Vendor = new Dictionary<string, ProfileSettings> {
                ["replacement-provider"] = new ProfileSettings {
                    Provider = "replacement-provider",
                    Protocol = ProtocolKindEnumConstants.OpenAiCompatible,
                    Endpoint = "http://localhost:9995/v1",
                    ApiKeyEnvVar = "REPL_API_KEY",
                    Model = "replacement-model"
                }
            },
            Current = new CurrentSettings { Profile = "replacement-provider" }
        };
        var newRegistry = ProviderDefinitionRegistry.Create(new ModelConfigLoader(), null, null);
        if (_server is null) throw new InvalidOperationException("Server not started");
        _server.ApplyReload(newSettings, newRegistry);

        // 热重载后：test-model 应该消失，replacement-model 应该出现
        using var response2 = await client.GetAsync($"http://localhost:{_port}/v1/models");
        var content2 = await response2.Content.ReadAsStringAsync();
        content2.Should().NotContain("test-model");
        content2.Should().Contain("replacement-model");
    }

    [Fact]
    public async Task ApplyReload_UpdatesProviderCountInHealthEndpoint() {
        await StartTestServerAsync();

        using var client = new HttpClient();

        // 初始状态：1 个 provider
        using var response1 = await client.GetAsync($"http://localhost:{_port}/health");
        var content1 = await response1.Content.ReadAsStringAsync();
        content1.Should().Contain("\"providers\":1");

        // 热重载：添加第二个 provider
        var newSettings = new SettingsJson {
            Vendor = new Dictionary<string, ProfileSettings> {
                ["test-provider"] = new ProfileSettings {
                    Provider = "test-provider",
                    Protocol = ProtocolKindEnumConstants.OpenAiCompatible,
                    Endpoint = "http://localhost:9999/v1",
                    ApiKeyEnvVar = "TEST_API_KEY",
                    Model = "test-model"
                },
                ["extra-provider"] = new ProfileSettings {
                    Provider = "extra-provider",
                    Protocol = ProtocolKindEnumConstants.OpenAiCompatible,
                    Endpoint = "http://localhost:9994/v1",
                    ApiKeyEnvVar = "EXTRA_API_KEY",
                    Model = "extra-model"
                }
            },
            Current = new CurrentSettings { Profile = "test-provider" }
        };
        var newRegistry = ProviderDefinitionRegistry.Create(new ModelConfigLoader(), null, null);
        if (_server is null) throw new InvalidOperationException("Server not started");
        _server.ApplyReload(newSettings, newRegistry);

        // 热重载后：2 个 provider
        using var response2 = await client.GetAsync($"http://localhost:{_port}/health");
        var content2 = await response2.Content.ReadAsStringAsync();
        content2.Should().Contain("\"providers\":2");
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
