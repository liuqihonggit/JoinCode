namespace Host.Tests.ChatCommands;

/// <summary>
/// JccMcpServer 权限管道接入回归测试 — tools/call 必须统一走 IToolExecutionGateway（13 层权限管道），
/// 禁止直连 IMcpToolRegistry.ExecuteToolAsync 绕过权限层（ADR 0138 P0 修复回归守卫）。
/// 集成方式: 真实 HttpListener + token 鉴权 + HTTP tools/call 全链路。
/// </summary>
[Trait("Category", "Integration")]
public sealed class JccMcpServerGatewayTests {
    private static int GetFreePort() {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>tools/call JSON-RPC 请求体</summary>
    private const string CallBody =
        """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"sample_tool","arguments":{"path":"evil.txt"}}}""";

    [Fact]
    public async Task HandleCallTool_RoutesThroughGateway_NotRegistry() {
        var registry = new Mock<IMcpToolRegistry>();
        registry.Setup(r => r.ContainsToolAsync("sample_tool", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var gateway = new Mock<IToolExecutionGateway>();
        gateway.Setup(g => g.ExecuteAsync("sample_tool", It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>(), null))
            .ReturnsAsync(CreateResult("gateway executed"));
        var server = new JccMcpServer(registry.Object, gateway.Object, "test");
        var port = GetFreePort();
        await using var http = new McpHttpServer(server, $"http://localhost:{port}/mcp/");
        using var cts = new CancellationTokenSource();
        var runTask = Task.Run(() => http.RunAsync(cts.Token));

        try {
            await Task.Delay(300);
            using var client = new HttpClient();
            using var request = CreateAuthorizedCall(http, port);
            using var response = await client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            // 执行必须走权限网关
            gateway.Verify(g => g.ExecuteAsync("sample_tool", It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>(), null), Times.Once,
                "tools/call 必须统一走 IToolExecutionGateway 权限管道（ADR 0138）");
            // 直连注册表执行必须为 0 次（回归守卫）
            registry.Verify(r => r.ExecuteToolAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>(), null), Times.Never,
                "禁止直连 IMcpToolRegistry.ExecuteToolAsync 绕过权限层");
        } finally {
            cts.Cancel();
            http.Stop();
        }
    }

    [Fact]
    public async Task HandleCallTool_GatewayResult_ConvertedToCallTool_Content() {
        var registry = new Mock<IMcpToolRegistry>();
        registry.Setup(r => r.ContainsToolAsync("sample_tool", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var gateway = new Mock<IToolExecutionGateway>();
        gateway.Setup(g => g.ExecuteAsync("sample_tool", It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>(), null))
            .ReturnsAsync(CreateResult("vault activated"));
        var server = new JccMcpServer(registry.Object, gateway.Object, "test");
        var port = GetFreePort();
        await using var http = new McpHttpServer(server, $"http://localhost:{port}/mcp/");
        using var cts = new CancellationTokenSource();
        var runTask = Task.Run(() => http.RunAsync(cts.Token));

        try {
            await Task.Delay(300);
            using var client = new HttpClient();
            using var request = CreateAuthorizedCall(http, port);
            using var response = await client.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            json.Should().Contain("vault activated", "gateway 结果内容必须透传到 MCP 协议响应");
        } finally {
            cts.Cancel();
            http.Stop();
        }
    }

    [Fact]
    public async Task HandleCallTool_GatewayError_IsErrorTrue() {
        var registry = new Mock<IMcpToolRegistry>();
        registry.Setup(r => r.ContainsToolAsync("sample_tool", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var gateway = new Mock<IToolExecutionGateway>();
        gateway.Setup(g => g.ExecuteAsync("sample_tool", It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>(), null))
            .ReturnsAsync(CreateResult("路径越界: C:\\evil.txt 不在信任工作目录内", isError: true));
        var server = new JccMcpServer(registry.Object, gateway.Object, "test");
        var port = GetFreePort();
        await using var http = new McpHttpServer(server, $"http://localhost:{port}/mcp/");
        using var cts = new CancellationTokenSource();
        var runTask = Task.Run(() => http.RunAsync(cts.Token));

        try {
            await Task.Delay(300);
            using var client = new HttpClient();
            using var request = CreateAuthorizedCall(http, port);
            using var response = await client.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            json.Should().Contain("evil.txt").And.Contain("\"isError\":true", "错误结果必须带 isError 标记（MCP 序列化将非 ASCII 转义为 \\uXXXX，故断言 ASCII 子串）");
        } finally {
            cts.Cancel();
            http.Stop();
        }
    }

    [Fact]
    public async Task HandleCallTool_UnknownTool_ShortCircuits_GatewayNotCalled() {
        var registry = new Mock<IMcpToolRegistry>();
        registry.Setup(r => r.ContainsToolAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var gateway = new Mock<IToolExecutionGateway>();
        var server = new JccMcpServer(registry.Object, gateway.Object, "test");
        var port = GetFreePort();
        await using var http = new McpHttpServer(server, $"http://localhost:{port}/mcp/");
        using var cts = new CancellationTokenSource();
        var runTask = Task.Run(() => http.RunAsync(cts.Token));

        try {
            await Task.Delay(300);
            using var client = new HttpClient();
            using var request = CreateAuthorizedCall(http, port);
            using var response = await client.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            gateway.Verify(g => g.ExecuteAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>(), null), Times.Never);
            json.Should().Contain("Tool not found");
        } finally {
            cts.Cancel();
            http.Stop();
        }
    }

    /// <summary>构造带 token 的 tools/call 请求</summary>
    private static HttpRequestMessage CreateAuthorizedCall(McpHttpServer http, int port) {
        var request = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{port}/mcp/") {
            Content = new StringContent(CallBody, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {http.AuthToken.Value}");
        return request;
    }

    private static ToolResult CreateResult(string text, bool isError = false) {
        return new ToolResult {
            Content = [new ToolContent { Type = ToolContentType.Text, Text = text }],
            IsError = isError
        };
    }
}
