namespace Mcp.Tests;

/// <summary>
/// McpTcpServer token 鉴权集成测试 — 裸 TcpClient 手写 HTTP（ADR 0138）
/// </summary>
[Trait("Category", "Integration")]
public class McpTcpServerAuthTests {
    private static int GetFreePort() {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private const string InitializeBody =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}""";

    /// <summary>发送裸 HTTP 请求并读取首行状态码</summary>
    private static async Task<(int StatusCode, string FirstLine)> SendRawAsync(int port, string? authLine) {
        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, port);
        await using var stream = client.GetStream();
        var sb = new StringBuilder();
        sb.Append("POST /mcp/ HTTP/1.1\r\n");
        if (authLine is not null)
            sb.Append(authLine).Append("\r\n");
        var body = new System.Text.UTF8Encoding(false).GetBytes(InitializeBody);
        sb.Append("Host: localhost:").Append(port).Append("\r\n");
        sb.Append("Content-Type: application/json\r\n");
        sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
        sb.Append("Connection: close\r\n\r\n");
        var headerBytes = System.Text.Encoding.ASCII.GetBytes(sb.ToString());
        await stream.WriteAsync(headerBytes, CancellationToken.None);
        await stream.WriteAsync(body, CancellationToken.None);
        await stream.FlushAsync(CancellationToken.None);

        var buffer = new byte[1024];
        var read = await stream.ReadAsync(buffer, CancellationToken.None);
        var text = System.Text.Encoding.ASCII.GetString(buffer, 0, read);
        var line = text.AsSpan().Slice(0, text.IndexOf('\r'));
        var statusCode = int.Parse(line.ToString().Split(' ')[1]);
        return (statusCode, text[..text.IndexOf('\r')]);
    }

    [Fact]
    public async Task PostInitialize_WithoutToken_Returns401() {
        var server = new McpServer("test");
        var port = GetFreePort();
        await using var tcpServer = new McpTcpServer(server, "localhost", port);
        using var cts = new CancellationTokenSource();
        _ = Task.Run(() => tcpServer.RunAsync(cts.Token));
        try {
            await Task.Delay(300);
            var (statusCode, _) = await SendRawAsync(port, null);
            statusCode.Should().Be(401);
        } finally {
            cts.Cancel();
            tcpServer.Stop();
        }
    }

    [Fact]
    public async Task PostInitialize_WithBearerToken_Returns200() {
        var server = new McpServer("test");
        var port = GetFreePort();
        await using var tcpServer = new McpTcpServer(server, "localhost", port);
        using var cts = new CancellationTokenSource();
        _ = Task.Run(() => tcpServer.RunAsync(cts.Token));
        try {
            await Task.Delay(300);
            var (statusCode, _) = await SendRawAsync(port, $"Authorization: Bearer {tcpServer.AuthToken.Value}");
            statusCode.Should().Be(200);
        } finally {
            cts.Cancel();
            tcpServer.Stop();
        }
    }

    [Fact]
    public async Task PostInitialize_WithMcpTokenHeader_Returns200() {
        var server = new McpServer("test");
        var port = GetFreePort();
        await using var tcpServer = new McpTcpServer(server, "localhost", port);
        using var cts = new CancellationTokenSource();
        _ = Task.Run(() => tcpServer.RunAsync(cts.Token));
        try {
            await Task.Delay(300);
            var (statusCode, _) = await SendRawAsync(port, $"Mcp-Token: {tcpServer.AuthToken.Value}");
            statusCode.Should().Be(200);
        } finally {
            cts.Cancel();
            tcpServer.Stop();
        }
    }

    [Fact]
    public void Constructor_EmptyToken_Throws() {
        var server = new McpServer("test");
        var act = () => new McpTcpServer(server, "localhost", 9911, authToken: "");
        act.Should().Throw<ArgumentException>();
    }
}
