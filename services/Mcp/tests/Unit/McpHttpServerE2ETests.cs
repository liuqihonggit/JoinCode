namespace Mcp.Tests;

/// <summary>
/// McpHttpServer E2E 集成测试 — 启动真实 HttpListener + HttpClient 验证无状态/有状态双场景
/// </summary>
[Trait("Category", "Integration")]
public class McpHttpServerE2ETests
{
    private static int GetFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private const string InitializeBody =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}""";

    [Fact]
    public async Task PostInitialize_StatelessMode_NoSessionIdHeader()
    {
        var server = new McpServer("test");
        var port = GetFreePort();
        using var httpServer = new McpHttpServer(server, $"http://localhost:{port}/mcp/", statelessMode: true);
        var cts = new CancellationTokenSource();
        var runTask = Task.Run(() => httpServer.RunAsync(cts.Token));

        try
        {
            await Task.Delay(300);
            using var client = new HttpClient();
            var response = await client.PostAsync(
                $"http://localhost:{port}/mcp/",
                new StringContent(InitializeBody, Encoding.UTF8, "application/json"));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.Contains("Mcp-Session-Id").Should().BeFalse();
        }
        finally
        {
            cts.Cancel();
            httpServer.Stop();
        }
    }

    [Fact]
    public async Task PostInitialize_StatefulMode_ReturnsSessionId_AndDeleteTerminates()
    {
        var server = new McpServer("test");
        var port = GetFreePort();
        using var httpServer = new McpHttpServer(server, $"http://localhost:{port}/mcp/", statelessMode: false);
        var cts = new CancellationTokenSource();
        var runTask = Task.Run(() => httpServer.RunAsync(cts.Token));

        try
        {
            await Task.Delay(300);
            using var client = new HttpClient();

            var response = await client.PostAsync(
                $"http://localhost:{port}/mcp/",
                new StringContent(InitializeBody, Encoding.UTF8, "application/json"));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.Contains("Mcp-Session-Id").Should().BeTrue();
            var sessionId = response.Headers.GetValues("Mcp-Session-Id").First();
            sessionId.Should().NotBeNullOrEmpty();
            httpServer.ActiveSessionCount.Should().Be(1);

            var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"http://localhost:{port}/mcp/");
            deleteRequest.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
            var deleteResponse = await client.SendAsync(deleteRequest);
            deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
            httpServer.ActiveSessionCount.Should().Be(0);
        }
        finally
        {
            cts.Cancel();
            httpServer.Stop();
        }
    }

    [Fact]
    public async Task PostWithInvalidSession_StatefulMode_Returns404()
    {
        var server = new McpServer("test");
        var port = GetFreePort();
        using var httpServer = new McpHttpServer(server, $"http://localhost:{port}/mcp/", statelessMode: false);
        var cts = new CancellationTokenSource();
        var runTask = Task.Run(() => httpServer.RunAsync(cts.Token));

        try
        {
            await Task.Delay(300);
            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{port}/mcp/")
            {
                Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"ping"}""", Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", "invalid-session-id");

            var response = await client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            cts.Cancel();
            httpServer.Stop();
        }
    }

    [Fact]
    public async Task Get_StatelessMode_Returns405()
    {
        var server = new McpServer("test");
        var port = GetFreePort();
        using var httpServer = new McpHttpServer(server, $"http://localhost:{port}/mcp/", statelessMode: true);
        var cts = new CancellationTokenSource();
        var runTask = Task.Run(() => httpServer.RunAsync(cts.Token));

        try
        {
            await Task.Delay(300);
            using var client = new HttpClient();
            var response = await client.GetAsync($"http://localhost:{port}/mcp/");
            response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        }
        finally
        {
            cts.Cancel();
            httpServer.Stop();
        }
    }

    [Fact]
    public async Task Get_StatefulMode_SseStream_PushesNotifications()
    {
        var server = new McpServer("test");
        var port = GetFreePort();
        using var httpServer = new McpHttpServer(server, $"http://localhost:{port}/mcp/", statelessMode: false);
        var cts = new CancellationTokenSource();
        var runTask = Task.Run(() => httpServer.RunAsync(cts.Token));

        try
        {
            await Task.Delay(300);
            using var client = new HttpClient();

            var initResponse = await client.PostAsync(
                $"http://localhost:{port}/mcp/",
                new StringContent(InitializeBody, Encoding.UTF8, "application/json"));
            initResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var sessionId = initResponse.Headers.GetValues("Mcp-Session-Id").First();

            var getRequest = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}/mcp/");
            getRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            getRequest.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
            var getResponse = await client.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead);
            getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            getResponse.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

            var notificationBody = """{"jsonrpc":"2.0","method":"notifications/initialized"}""";
            var notificationRequest = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{port}/mcp/")
            {
                Content = new StringContent(notificationBody, Encoding.UTF8, "application/json")
            };
            notificationRequest.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
            var notificationResponse = await client.SendAsync(notificationRequest);
            notificationResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

            using var stream = await getResponse.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var receivedData = string.Empty;
            while (!timeoutCts.Token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(timeoutCts.Token);
                if (line is not null && line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    receivedData = line.Substring(6);
                    break;
                }
            }
            receivedData.Should().Contain("notifications/initialized");
        }
        finally
        {
            cts.Cancel();
            httpServer.Stop();
        }
    }

    [Fact]
    public async Task Get_SseStream_EventsHaveId_ForLastEventIdReconnect()
    {
        var server = new McpServer("test");
        var port = GetFreePort();
        using var httpServer = new McpHttpServer(server, $"http://localhost:{port}/mcp/", statelessMode: false);
        var cts = new CancellationTokenSource();
        var runTask = Task.Run(() => httpServer.RunAsync(cts.Token));

        try
        {
            await Task.Delay(300);
            using var client = new HttpClient();

            var initResponse = await client.PostAsync(
                $"http://localhost:{port}/mcp/",
                new StringContent(InitializeBody, Encoding.UTF8, "application/json"));
            var sessionId = initResponse.Headers.GetValues("Mcp-Session-Id").First();

            var getRequest = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}/mcp/");
            getRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            getRequest.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
            var getResponse = await client.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead);

            var notificationBody = """{"jsonrpc":"2.0","method":"notifications/initialized"}""";
            var notificationRequest = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{port}/mcp/")
            {
                Content = new StringContent(notificationBody, Encoding.UTF8, "application/json")
            };
            notificationRequest.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
            await client.SendAsync(notificationRequest);

            using var stream = await getResponse.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var receivedId = string.Empty;
            while (!timeoutCts.Token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(timeoutCts.Token);
                if (line is not null && line.StartsWith("id: ", StringComparison.Ordinal))
                {
                    receivedId = line.Substring(4);
                    break;
                }
            }
            receivedId.Should().NotBeNullOrEmpty("SSE 事件必须包含 id 行以支持 Last-Event-ID 重连");
        }
        finally
        {
            cts.Cancel();
            httpServer.Stop();
        }
    }
}
