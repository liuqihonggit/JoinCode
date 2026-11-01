namespace MockServer.Core.Tests;

/// <summary>
/// 录制回放闭环测试 — 用第二个 MockServer 模拟真实 LLM 上游，
/// 验证 record/start → 透传 → record/stop → replay/load → 回放 全链路。
/// 不需要真实 API Key。
/// </summary>
public sealed class MockServerRecordReplayTests {
    private static async Task<KestrelMockServer> StartMockAsync(string name) {
        var strategy = new OpenAIResponseStrategy(turns: null, "upstream default");
        var cache = new PrefixCacheSimulator(
            TokenEstimator.ExtractConversationPrefix,
            TokenEstimator.EstimateFromMessages);
        var server = new KestrelMockServer(strategy, cache, port: 0, serverName: name);
        await server.StartAsync();
        return server;
    }

    private static async Task<string> PostJsonAsync(HttpClient client, string path, string body) {
        var resp = await client.PostAsync(path, new StringContent(body, Encoding.UTF8, "application/json"));
        return await resp.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Record_ThenReplay_ReturnsRecordedResponse() {
        var upstream = await StartMockAsync("Upstream");
        var main = await StartMockAsync("Main");
        try {
            var upstreamUrl = upstream.Url.TrimEnd('/');
            var mainClient = new HttpClient { BaseAddress = new Uri(main.Url) };

            await PostJsonAsync(mainClient, "/__control__/record/start",
                $$"""{"upstream":"{{upstreamUrl}}","vendor":"openai"}""");

            var llmBody = """{"model":"gpt-4o","messages":[{"role":"user","content":"hello"}]}""";
            var liveResp = await PostJsonAsync(mainClient, "/v1/chat/completions", llmBody);
            liveResp.Should().Contain("upstream default");

            var stopResp = await PostJsonAsync(mainClient, "/__control__/record/stop", "{}");
            stopResp.Should().Contain("interaction_count");

            var stateAfterStop = await mainClient.GetStringAsync("/__control__/state");
            stateAfterStop.Should().Contain("\"mode\":\"Interactive\"");

            await PostJsonAsync(mainClient, "/__control__/clear", "");

            var recordedInteractions = main.Controller.GetRecordedInteractions();
            recordedInteractions.Should().HaveCount(1);
            var recordingJson = JsonSerializer.Serialize(new RecordingFile {
                CreatedAt = DateTime.UtcNow.ToString("O"),
                Upstream = upstreamUrl,
                Vendor = "openai",
                Interactions = recordedInteractions.ToList()
            }, MockServerJsonContext.Default.RecordingFile);
            await PostJsonAsync(mainClient, "/__control__/replay/load", recordingJson);

            var replayResp = await PostJsonAsync(mainClient, "/v1/chat/completions", llmBody);
            replayResp.Should().Contain("upstream default");

            mainClient.Dispose();
        } finally {
            await upstream.StopAsync();
            await upstream.DisposeAsync();
            await main.StopAsync();
            await main.DisposeAsync();
        }
    }

    [Fact]
    public async Task Record_CapturesRequestAndResponse() {
        var upstream = await StartMockAsync("Upstream2");
        var main = await StartMockAsync("Main2");
        try {
            var upstreamUrl = upstream.Url.TrimEnd('/');
            var mainClient = new HttpClient { BaseAddress = new Uri(main.Url) };

            await PostJsonAsync(mainClient, "/__control__/record/start",
                $$"""{"upstream":"{{upstreamUrl}}","vendor":"openai"}""");

            await PostJsonAsync(mainClient, "/v1/chat/completions",
                """{"model":"gpt-4o","messages":[{"role":"user","content":"test capture"}]}""");

            await PostJsonAsync(mainClient, "/__control__/record/stop", "{}");

            var interactions = main.Controller.GetRecordedInteractions();
            interactions.Should().HaveCount(1);
            interactions[0].Path.Should().Be("/v1/chat/completions");
            interactions[0].StatusCode.Should().Be(200);
            interactions[0].ResponseBody.Should().Contain("upstream default");

            mainClient.Dispose();
        } finally {
            await upstream.StopAsync();
            await upstream.DisposeAsync();
            await main.StopAsync();
            await main.DisposeAsync();
        }
    }
}
