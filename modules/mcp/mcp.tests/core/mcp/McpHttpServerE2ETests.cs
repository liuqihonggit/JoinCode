// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace Mcp.Tests;

/// <summary>
/// McpHttpServer E2E 集成测试 — 启动真实 HttpListener + HttpClient 验证无状态/有状态双场景
/// </summary>
[Trait("Category", "Integration")]
public class McpHttpServerE2ETests {
    private static int GetFreePort() {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private const string InitializeBody =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}""";

    /// <summary>构造带 token 鉴权头的 POST 请求（ADR 0138: serve 默认 token 鉴权）</summary>
    private static HttpRequestMessage CreateAuthorizedPost(McpHttpServer server, string url, string body) {
        var request = new HttpRequestMessage(HttpMethod.Post, url) {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {server.AuthToken.Value}");
        return request;
    }

    /// <summary>为已有请求补 token 鉴权头（ADR 0138）</summary>
    private static void AddAuthHeader(HttpRequestMessage request, McpHttpServer server) {
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {server.AuthToken.Value}");
    }

    [Fact]
    public async Task PostInitialize_StatelessMode_NoSessionIdHeader() {
        var server = new McpServer("test");
        var port = GetFreePort();
        await using var httpServer = new McpHttpServer(server, $"http://localhost:{port}/mcp/", statelessMode: true);
        using var cts = new CancellationTokenSource();
        var runTask = Task.Run(() => httpServer.RunAsync(cts.Token));

        try {
            await Task.Delay(300);
            using var client = new HttpClient();
            using var request = CreateAuthorizedPost(httpServer, $"http://localhost:{port}/mcp/", InitializeBody);
            using var response = await client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.Contains("Mcp-Session-Id").Should().BeFalse();
        } finally {
            cts.Cancel();
            httpServer.Stop();
        }
    }

    [Fact]
    public async Task PostInitialize_StatefulMode_ReturnsSessionId_AndDeleteTerminates() {
        var server = new McpServer("test");
        var port = GetFreePort();
        await using var httpServer = new McpHttpServer(server, $"http://localhost:{port}/mcp/", statelessMode: false);
        using var cts = new CancellationTokenSource();
        var runTask = Task.Run(() => httpServer.RunAsync(cts.Token));

        try {
            await Task.Delay(300);
            using var client = new HttpClient();
            using var request = CreateAuthorizedPost(httpServer, $"http://localhost:{port}/mcp/", InitializeBody);
            using var response = await client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.Contains("Mcp-Session-Id").Should().BeTrue();
            var sessionId = response.Headers.GetValues("Mcp-Session-Id").First();
            sessionId.Should().NotBeNullOrEmpty();
            httpServer.ActiveSessionCount.Should().Be(1);

            using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"http://localhost:{port}/mcp/");
            AddAuthHeader(deleteRequest, httpServer);
            deleteRequest.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
            using var deleteResponse = await client.SendAsync(deleteRequest);
            deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
            httpServer.ActiveSessionCount.Should().Be(0);
        } finally {
            cts.Cancel();
            httpServer.Stop();
        }
    }

    [Fact]
    public async Task PostWithInvalidSession_StatefulMode_Returns404() {
        var server = new McpServer("test");
        var port = GetFreePort();
        await using var httpServer = new McpHttpServer(server, $"http://localhost:{port}/mcp/", statelessMode: false);
        using var cts = new CancellationTokenSource();
        var runTask = Task.Run(() => httpServer.RunAsync(cts.Token));

        try {
            await Task.Delay(300);
            using var client = new HttpClient();
            using             var request = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{port}/mcp/") {
                Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"ping"}""", Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", "invalid-session-id");
            AddAuthHeader(request, httpServer);

            using var response = await client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        } finally {
            cts.Cancel();
            httpServer.Stop();
        }
    }

    [Fact]
    public async Task Get_StatelessMode_Returns405() {
        var server = new McpServer("test");
        var port = GetFreePort();
        await using var httpServer = new McpHttpServer(server, $"http://localhost:{port}/mcp/", statelessMode: true);
        using var cts = new CancellationTokenSource();
        var runTask = Task.Run(() => httpServer.RunAsync(cts.Token));

        try {
            await Task.Delay(300);
            using var client = new HttpClient();
            using var getHttpRequest = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}/mcp/");
            AddAuthHeader(getHttpRequest, httpServer);
            using var getResponse = await client.SendAsync(getHttpRequest);
            getResponse.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        } finally {
            cts.Cancel();
            httpServer.Stop();
        }
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task Get_StatefulMode_SseStream_PushesNotifications() {
        var server = new McpServer("test");
        var port = GetFreePort();
        await using var httpServer = new McpHttpServer(server, $"http://localhost:{port}/mcp/", statelessMode: false);
        using var cts = new CancellationTokenSource();
        var runTask = Task.Run(() => httpServer.RunAsync(cts.Token));

        try {
            await Task.Delay(300);
            using var client = new HttpClient();

            using var initResponse = await client.SendAsync(CreateAuthorizedPost(httpServer, $"http://localhost:{port}/mcp/", InitializeBody));
            initResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var sessionId = initResponse.Headers.GetValues("Mcp-Session-Id").First();

            using var getRequest = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}/mcp/");
            getRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            AddAuthHeader(getRequest, httpServer);
            getRequest.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
            using var getResponse = await client.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead);
            getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            getResponse.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

            var notificationBody = """{"jsonrpc":"2.0","method":"notifications/initialized"}""";
            var notificationRequest = CreateAuthorizedPost(httpServer, $"http://localhost:{port}/mcp/", notificationBody);
            notificationRequest.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
            using (notificationRequest) {
                using var notificationResponse = await client.SendAsync(notificationRequest);
            notificationResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
            await using var stream = await getResponse.Content.ReadAsStreamAsync();
            using var reader = stream.AsUtf8Reader();
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var receivedData = string.Empty;
            while (!timeoutCts.Token.IsCancellationRequested) {
                var line = await reader.ReadLineAsync(timeoutCts.Token);
                if (line is not null && line.StartsWith("data: ", StringComparison.Ordinal)) {
                    receivedData = line.Substring(6);
                    break;
                }
            }
            receivedData.Should().Contain("notifications/initialized");
            }
        } finally {
            cts.Cancel();
            httpServer.Stop();
        }
    }

    [Fact]
    public async Task Get_SseStream_EventsHaveId_ForLastEventIdReconnect() {
        var server = new McpServer("test");
        var port = GetFreePort();
        await using var httpServer = new McpHttpServer(server, $"http://localhost:{port}/mcp/", statelessMode: false);
        using var cts = new CancellationTokenSource();
        var runTask = Task.Run(() => httpServer.RunAsync(cts.Token));

        try {
            await Task.Delay(300);
            using var client = new HttpClient();

            using var initResponse = await client.SendAsync(CreateAuthorizedPost(httpServer, $"http://localhost:{port}/mcp/", InitializeBody));
            var sessionId = initResponse.Headers.GetValues("Mcp-Session-Id").First();

            using var getRequest = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}/mcp/");
            getRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            AddAuthHeader(getRequest, httpServer);
            getRequest.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
            using var getResponse = await client.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead);

            var notificationBody = """{"jsonrpc":"2.0","method":"notifications/initialized"}""";
            using var notificationRequest = CreateAuthorizedPost(httpServer, $"http://localhost:{port}/mcp/", notificationBody);
            notificationRequest.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
            using var notificationResponse = await client.SendAsync(notificationRequest);
            await using var stream = await getResponse.Content.ReadAsStreamAsync();
            using var reader = stream.AsUtf8Reader();
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var receivedId = string.Empty;
            while (!timeoutCts.Token.IsCancellationRequested) {
                var line = await reader.ReadLineAsync(timeoutCts.Token);
                if (line is not null && line.StartsWith("id: ", StringComparison.Ordinal)) {
                    receivedId = line.Substring(4);
                    break;
                }
            }
            receivedId.Should().NotBeNullOrEmpty("SSE 事件必须包含 id 行以支持 Last-Event-ID 重连");
        } finally {
            cts.Cancel();
            httpServer.Stop();
        }
    }

    [Fact]
    public async Task Post_OversizedBody_Returns413() {
        var server = new McpServer("test");
        var port = GetFreePort();
        await using var httpServer = new McpHttpServer(server, $"http://localhost:{port}/mcp/", statelessMode: true);
        httpServer.MaxRequestBodyBytes = 100;
        using var cts = new CancellationTokenSource();
        var runTask = Task.Run(() => httpServer.RunAsync(cts.Token));

        try {
            await Task.Delay(300);
            using var client = new HttpClient();
            var oversizedBody = new string('x', 200);
            using var request = CreateAuthorizedPost(httpServer, $"http://localhost:{port}/mcp/", oversizedBody);
            using var response = await client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        } finally {
            cts.Cancel();
            httpServer.Stop();
        }
    }

    [Fact]
    public async Task Post_NormalBody_UnderLimit_Returns200() {
        var server = new McpServer("test");
        var port = GetFreePort();
        await using var httpServer = new McpHttpServer(server, $"http://localhost:{port}/mcp/", statelessMode: true);
        httpServer.MaxRequestBodyBytes = 10 * 1024 * 1024;
        using var cts = new CancellationTokenSource();
        var runTask = Task.Run(() => httpServer.RunAsync(cts.Token));

        try {
            await Task.Delay(300);
            using var client = new HttpClient();
            using var request = CreateAuthorizedPost(httpServer, $"http://localhost:{port}/mcp/", InitializeBody);
            using var response = await client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        } finally {
            cts.Cancel();
            httpServer.Stop();
        }
    }
}