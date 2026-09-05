namespace Mcp.Tests.Transports;

/// <summary>
/// WebSocketTransport 单元测试 — 验证 ConnectCoreAsync 中 http/https → ws/wss 协议转换逻辑。
/// </summary>
/// <remarks>
/// Bug: ClientWebSocket 不接受 http:// 协议，只接受 ws:// 或 wss://，导致 ArgumentException。
/// 修复: ConnectCoreAsync 将 http:// → ws://，https:// → wss://（OrdinalIgnoreCase 大小写不敏感）。
/// 测试策略:
///   - ws:// 场景: 启动 HttpListener WebSocket 服务器，验证 StartAsync 连接成功 (IsRunning=true)。
///   - wss:// 场景: 连接到空闲端口（无服务器），验证不抛 ArgumentException（证明协议被 ClientWebSocket 接受）。
/// </remarks>
public class WebSocketTransportTest
{
    /// <summary>获取一个空闲 TCP 端口（调用后立即释放，用于绑定或作为无服务连接目标）</summary>
    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>基于 HttpListener 的轻量级 WebSocket 测试服务器，接受升级请求并保持连接</summary>
    private sealed class WebSocketTestServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentBag<WebSocket> _sockets = new();

        public WebSocketTestServer(int port)
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Start();
            _ = AcceptLoopAsync();
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested && _listener.IsListening)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync();
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                if (ctx.Request.IsWebSocketRequest)
                {
                    try
                    {
                        var wsCtx = await ctx.AcceptWebSocketAsync(null);
                        _sockets.Add(wsCtx.WebSocket);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WebSocketTestServer] AcceptWebSocketAsync 失败: {ex.Message}");
                    }
                }
                else
                {
                    ctx.Response.StatusCode = 400;
                    ctx.Response.Close();
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Close();
            foreach (var ws in _sockets)
            {
                try
                {
                    ws.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WebSocketTestServer] 关闭 WebSocket 失败: {ex.Message}");
                }
            }
            _cts.Dispose();
        }
    }

    // ========== ws:// 场景：启动真实服务器验证连接成功 ==========

    /// <summary>http://localhost:port 应转换为 ws://localhost:port 并成功连接</summary>
    [Fact]
    public async Task StartAsync_HttpEndpoint_ConvertsToWsAndConnectsSuccessfully()
    {
        var port = GetFreePort();
        using var server = new WebSocketTestServer(port);
        var config = new McpServerConnectionConfig
        {
            Name = "test-http",
            Endpoint = $"http://localhost:{port}",
            TransportType = McpClientTransportType.WebSocket,
        };
        await using var transport = new WebSocketTransport(config);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await transport.StartAsync(cts.Token);

        transport.IsRunning.Should().BeTrue("http:// 应转换为 ws:// 并成功连接 WebSocket 服务器");
    }

    /// <summary>ws://localhost:port 应保持不变并成功连接</summary>
    [Fact]
    public async Task StartAsync_WsEndpoint_StaysUnchangedAndConnectsSuccessfully()
    {
        var port = GetFreePort();
        using var server = new WebSocketTestServer(port);
        var config = new McpServerConnectionConfig
        {
            Name = "test-ws",
            Endpoint = $"ws://localhost:{port}",
            TransportType = McpClientTransportType.WebSocket,
        };
        await using var transport = new WebSocketTransport(config);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await transport.StartAsync(cts.Token);

        transport.IsRunning.Should().BeTrue("ws:// 应保持不变并成功连接 WebSocket 服务器");
    }

    /// <summary>HTTP://localhost:port 大写应转换为 ws://localhost:port 并成功连接</summary>
    [Fact]
    public async Task StartAsync_UppercaseHttpEndpoint_ConvertsToWsAndConnectsSuccessfully()
    {
        var port = GetFreePort();
        using var server = new WebSocketTestServer(port);
        var config = new McpServerConnectionConfig
        {
            Name = "test-uppercase-http",
            Endpoint = $"HTTP://localhost:{port}",
            TransportType = McpClientTransportType.WebSocket,
        };
        await using var transport = new WebSocketTransport(config);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await transport.StartAsync(cts.Token);

        transport.IsRunning.Should().BeTrue("HTTP:// 大写应转换为 ws:// 并成功连接 WebSocket 服务器");
    }

    // ========== wss:// 场景：连接空闲端口验证协议被 ClientWebSocket 接受 ==========

    /// <summary>https://localhost:port 应转换为 wss://localhost:port，ClientWebSocket 应接受协议（不抛 ArgumentException）</summary>
    [Fact]
    public async Task StartAsync_HttpsEndpoint_ConvertsToWss_ProtocolAcceptedByClientWebSocket()
    {
        // 连接到空闲端口（无服务器）:
        //   - 协议转换正确 (https→wss) → ClientWebSocket 接受协议，TCP 连接失败 → WebSocketException
        //   - 协议未转换 (仍 https)    → ClientWebSocket 拒绝协议 → ArgumentException
        var port = GetFreePort();
        var config = new McpServerConnectionConfig
        {
            Name = "test-https",
            Endpoint = $"https://localhost:{port}",
            TransportType = McpClientTransportType.WebSocket,
        };
        await using var transport = new WebSocketTransport(config);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Exception? caught = null;
        try
        {
            await transport.StartAsync(cts.Token);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        // 若连接成功（端口恰好被占用），说明协议也被接受，测试通过
        // 若抛异常，必须不是 ArgumentException（否则说明协议未被转换，bug 未修复）
        if (caught is not null)
        {
            caught.Should().NotBeAssignableTo<ArgumentException>(
                "https:// 应已转换为 wss://，ClientWebSocket 应接受协议而非抛 ArgumentException");
        }
    }

    /// <summary>wss://localhost:port 应保持不变，ClientWebSocket 应接受协议（不抛 ArgumentException）</summary>
    [Fact]
    public async Task StartAsync_WssEndpoint_StaysUnchanged_ProtocolAcceptedByClientWebSocket()
    {
        var port = GetFreePort();
        var config = new McpServerConnectionConfig
        {
            Name = "test-wss",
            Endpoint = $"wss://localhost:{port}",
            TransportType = McpClientTransportType.WebSocket,
        };
        await using var transport = new WebSocketTransport(config);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Exception? caught = null;
        try
        {
            await transport.StartAsync(cts.Token);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        if (caught is not null)
        {
            caught.Should().NotBeAssignableTo<ArgumentException>(
                "wss:// 应被 ClientWebSocket 接受而非抛 ArgumentException");
        }
    }
}
