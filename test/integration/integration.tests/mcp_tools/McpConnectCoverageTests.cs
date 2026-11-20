namespace Integration.Tests.McpTools;

/// <summary>
/// mcp_connect 巡检覆盖（TASK072 E 项，清单 :147）— 注入 FakeMcpClientFactory（本地替身服务器），
/// 让原本被跳过的 mcp_connect 走完整 handler 链路（参数校验/连接/注册表/持久化）而不连真实服务器。
/// </summary>
public sealed class McpConnectCoverageTests {
    [Fact]
    public async Task McpConnect_ExecutesThroughHandler_WithLocalFakeServer() {
        await using var host = new IsolatedTestHost(
            withTestPipelines: true, injectFakeMcpClientFactory: true, disposeProvider: false);
        await host.InitializeMcpAsync().ConfigureAwait(true);
        var gateway = host.GetRequiredService<IToolExecutionGateway>();

        var args = new Dictionary<string, JsonElement> {
            ["connection_name"] = JsonSerializer.SerializeToElement("patrol-fake-server"),
            ["endpoint"] = JsonSerializer.SerializeToElement("http://localhost:19999/mcp"),
            ["transport_type"] = JsonSerializer.SerializeToElement("http"),
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var result = await gateway.ExecuteAsync("mcp_connect", args, cts.Token).ConfigureAwait(true);

        if (result is null) throw new InvalidOperationException("mcp_connect 不应返回 null");
        result.IsError.Should().BeFalse(
            $"本地 fake 服务器应连接成功: {result.Content.FirstOrDefault()?.Text}");
        var stub = host.McpClientStub;
        if (stub is null) throw new InvalidOperationException("injectFakeMcpClientFactory=true 时 McpClientStub 必须已注入");
        stub.ConnectCount.Should().Be(1, "mcp_connect 应真调客户端 ConnectAsync（非跳过）");
        stub.IsConnected.Should().BeTrue();
    }
}
