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
}
