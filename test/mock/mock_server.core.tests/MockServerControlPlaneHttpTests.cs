namespace MockServer.Core.Tests;

/// <summary>
/// MockServer 控制面端点集成测试 — 启动真实 KestrelMockServer，通过 HTTP 验证控制端点。
/// 每个测试独立启动/关闭服务器，避免 IAsyncLifetime 的潜在问题。
/// </summary>
public sealed class MockServerControlPlaneHttpTests {
    private static async Task<(KestrelMockServer Server, HttpClient Client)> StartServerAsync() {
        var strategy = new OpenAIResponseStrategy(turns: null, "default mock response");
        var cache = new PrefixCacheSimulator(
            TokenEstimator.ExtractConversationPrefix,
            TokenEstimator.EstimateFromMessages);
        var server = new KestrelMockServer(strategy, cache, port: 0, serverName: "ControlHttp");
        await server.StartAsync();
        var client = new HttpClient { BaseAddress = new Uri(server.Url) };
        return (server, client);
    }

    [Fact]
    public async Task State_ReturnsCorrectInitialState() {
        var (server, client) = await StartServerAsync();
        try {
            var json = await client.GetStringAsync("/__control__/state");
            using var doc = JsonDocument.Parse(json);

            doc.RootElement.GetProperty("total_requests").GetInt32().Should().Be(0);
            doc.RootElement.GetProperty("inject_queue_length").GetInt32().Should().Be(0);
        } finally {
            client.Dispose();
            await server.StopAsync();
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task Inject_ThenLlmRequest_ReturnsInjectedBody() {
        var (server, client) = await StartServerAsync();
        try {
            var injectPayload = """{"status_code":200,"content_type":"application/json","body":"injected hello"}""";
            await client.PostAsync("/__control__/inject", new StringContent(injectPayload, Encoding.UTF8, "application/json"));

            var llmResp = await client.PostAsync("/v1/chat/completions",
                new StringContent("""{"model":"gpt-4o","messages":[{"role":"user","content":"hi"}]}""", Encoding.UTF8, "application/json"));

            llmResp.IsSuccessStatusCode.Should().BeTrue();
            (await llmResp.Content.ReadAsStringAsync()).Should().Contain("injected hello");
        } finally {
            client.Dispose();
            await server.StopAsync();
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task Interactive_RequestWaitsThenInject_ReturnsInjected() {
        var (server, client) = await StartServerAsync();
        try {
            var requestTask = client.PostAsync("/v1/chat/completions",
                new StringContent("""{"model":"gpt-4o","messages":[{"role":"user","content":"hi"}]}""", Encoding.UTF8, "application/json"));

            await Task.Delay(300);
            requestTask.IsCompleted.Should().BeFalse("Interactive 模式无注入时请求应挂起");

            var injectPayload = """{"status_code":200,"content_type":"application/json","body":"responder reply"}""";
            await client.PostAsync("/__control__/inject", new StringContent(injectPayload, Encoding.UTF8, "application/json"));

            var llmResp = await requestTask;
            llmResp.IsSuccessStatusCode.Should().BeTrue();
            (await llmResp.Content.ReadAsStringAsync()).Should().Contain("responder reply");
        } finally {
            client.Dispose();
            await server.StopAsync();
            await server.DisposeAsync();
        }
    }
}
