namespace McpProtocol;

/// <summary>
/// MCP TCP 服务端 — 用 TcpListener 替代 HttpListener,手动解析 HTTP 请求。
/// 纵深防御: HttpListener 依赖 HTTP.sys 驱动,在沙箱/非管理员环境不可用;
/// TcpListener 是纯托管代码,无系统依赖,可作为降级方案。
/// 对齐 2025-11-25 规范: POST 处理 JSON-RPC, DELETE 删除会话, GET SSE 推送(有状态模式)。
/// </summary>
public sealed class McpTcpServer : ServiceEntity
{
    private readonly McpServer _server;
    private readonly TcpListener _listener;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _sessions = new(StringComparer.Ordinal);
    private readonly bool _statelessMode;
    private readonly FrozenSet<string> _allowedOrigins;
    private CancellationTokenSource? _cts;
    private readonly DateTime _startTime = DateTime.UtcNow;

    // 统计信息 — 用于结构化退出报告
    private int _totalRequests;
    private int _totalErrors;
    private int _activeConnections;

    /// <summary>总请求数</summary>
    public int TotalRequests => Volatile.Read(ref _totalRequests);
    /// <summary>错误请求数</summary>
    public int TotalErrors => Volatile.Read(ref _totalErrors);
    /// <summary>当前活跃连接数</summary>
    public int ActiveConnections => Volatile.Read(ref _activeConnections);
    /// <summary>服务运行时长</summary>
    public TimeSpan Uptime => DateTime.UtcNow - _startTime;

    /// <summary>
    /// 创建 MCP TCP 服务端
    /// </summary>
    /// <param name="server">底层 MCP 服务器</param>
    /// <param name="host">监听地址(如 localhost 或 127.0.0.1)</param>
    /// <param name="port">监听端口</param>
    /// <param name="statelessMode">无状态模式(默认 true):不分配 MCP-Session-Id</param>
    /// <param name="allowedOrigins">允许的 Origin 列表;null/空则允许所有</param>
    public McpTcpServer(McpServer server, string host, int port, bool statelessMode = true, IEnumerable<string>? allowedOrigins = null)
        : base(nameof(McpTcpServer))
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("监听地址不能为空", nameof(host));
        if (port <= 0 || port > 65535) throw new ArgumentException("端口范围 1-65535", nameof(port));
        _statelessMode = statelessMode;
        _allowedOrigins = (allowedOrigins ?? []).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        var address = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ? IPAddress.Loopback : IPAddress.Parse(host);
        _listener = new TcpListener(address, port);
    }

    /// <summary>运行服务端,直到 cancellationToken 取消</summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener.Start();

        // 取消时 Stop listener,中断 AcceptTcpClientAsync 阻塞
        using var registration = _cts.Token.Register(static state =>
        {
            var self = (McpTcpServer)state!;
            try { self._listener.Stop(); } catch (Exception ex) { Console.WriteLine($"McpTcpServer: Stop 失败: {ex.Message}"); }
        }, this);

        while (!_cts.Token.IsCancellationRequested)
        {
            TcpClient? client;
            try
            {
                client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
            }
            catch (SocketException) when (_cts.Token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (_cts.Token.IsCancellationRequested)
            {
                Console.WriteLine($"McpTcpServer: Accept 异常(取消): {ex.Message}");
                break;
            }

            Interlocked.Increment(ref _activeConnections);
            _ = HandleClientAsync(client, _cts.Token);
        }
    }

    /// <summary>停止服务端</summary>
    public void Stop()
    {
        _cts?.Cancel();
        _listener.Stop();
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                while (!ct.IsCancellationRequested && client.Connected)
                {
                    HttpRequestInfo? request;
                    try
                    {
                        request = await ReadHttpRequestAsync(stream, ct).ConfigureAwait(false);
                    }
                    catch (EndOfStreamException)
                    {
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (request is null) break;

                    Interlocked.Increment(ref _totalRequests);
                    await DispatchRequestAsync(stream, request, ct).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Console.WriteLine($"McpTcpServer: 连接异常: {ex.Message}");
        }
        finally
        {
            Interlocked.Decrement(ref _activeConnections);
        }
    }

    private async Task DispatchRequestAsync(Stream stream, HttpRequestInfo request, CancellationToken ct)
    {
        var method = request.Method.ToUpperInvariant();
        try
        {
            switch (method)
            {
                case "POST":
                    await HandlePostAsync(stream, request, ct).ConfigureAwait(false);
                    break;
                case "DELETE":
                    HandleDelete(stream, request);
                    break;
                case "GET":
                    // 无状态模式不支持 GET SSE
                    if (_statelessMode || string.IsNullOrEmpty(request.Headers.GetValueOrDefault("Mcp-Session-Id")))
                    {
                        await WriteResponseAsync(stream, 405, "Method Not Allowed", null, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        // SSE 推送暂不支持,返回 200 空流
                        await WriteResponseAsync(stream, 200, "OK", null, ct).ConfigureAwait(false);
                    }
                    break;
                default:
                    await WriteResponseAsync(stream, 405, "Method Not Allowed", null, ct).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Interlocked.Increment(ref _totalErrors);
            Console.WriteLine($"McpTcpServer: 处理请求异常: {ex.Message}");
            try
            {
                await WriteResponseAsync(stream, 500, "Internal Server Error", null, ct).ConfigureAwait(false);
            }
            catch (Exception writeEx) when (!ct.IsCancellationRequested)
            {
                Console.WriteLine($"McpTcpServer: 写 500 响应失败: {writeEx.Message}");
            }
        }
    }

    private async Task HandlePostAsync(Stream stream, HttpRequestInfo request, CancellationToken ct)
    {
        var sessionId = request.Headers.GetValueOrDefault("Mcp-Session-Id");

        // 有状态模式:带 session 但不存在 → 404
        if (!_statelessMode && !string.IsNullOrEmpty(sessionId) && !_sessions.ContainsKey(sessionId))
        {
            await WriteResponseAsync(stream, 404, "Not Found", null, ct).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            await WriteResponseAsync(stream, 400, "Bad Request", null, ct).ConfigureAwait(false);
            return;
        }

        var response = await _server.ProcessMessageAsync(request.Body, ct).ConfigureAwait(false);

        if (response is null)
        {
            await WriteResponseAsync(stream, 202, "Accepted", null, ct).ConfigureAwait(false);
            return;
        }

        // 有状态模式: initialize 请求分配新 session
        Dictionary<string, string>? extraHeaders = null;
        if (!_statelessMode && IsInitializeRequest(request.Body))
        {
            var newSessionId = GenerateSessionId();
            _sessions[newSessionId] = DateTime.UtcNow;
            extraHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Mcp-Session-Id"] = newSessionId
            };
        }

        var json = McpJsonSerializer.Serialize(response);
        await WriteResponseAsync(stream, 200, "OK", json, ct, extraHeaders).ConfigureAwait(false);
    }

    private void HandleDelete(Stream stream, HttpRequestInfo request)
    {
        var sessionId = request.Headers.GetValueOrDefault("Mcp-Session-Id");
        if (!string.IsNullOrEmpty(sessionId))
        {
            _sessions.TryRemove(sessionId, out _);
        }
        WriteResponseAsync(stream, 204, "No Content", null, default).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 读取 HTTP 请求 — 逐字节读取头部(可靠),按 Content-Length 批量读取 body。
    /// </summary>
    private static async Task<HttpRequestInfo?> ReadHttpRequestAsync(Stream stream, CancellationToken ct)
    {
        // 读取头部直到 \r\n\r\n
        var headerSb = new StringBuilder(512);
        var oneByte = new byte[1];
        while (true)
        {
            var n = await stream.ReadAsync(oneByte.AsMemory(0, 1), ct).ConfigureAwait(false);
            if (n == 0)
            {
                return headerSb.Length == 0 ? null : throw new EndOfStreamException("连接关闭");
            }
            var b = oneByte[0];
            headerSb.Append((char)b);
            if (headerSb.Length >= 4 &&
                headerSb[^4] == '\r' && headerSb[^3] == '\n' &&
                headerSb[^2] == '\r' && headerSb[^1] == '\n')
            {
                break;
            }
            if (headerSb.Length > 65536)
            {
                throw new InvalidOperationException("HTTP 头部过大(>64KB)");
            }
        }

        // 解析头部
        var headerText = headerSb.ToString(0, headerSb.Length - 4);
        var lines = headerText.Split("\r\n");
        if (lines.Length == 0 || lines[0].Length == 0) return null;

        var requestLine = lines[0].Split(' ', 3);
        if (requestLine.Length < 2) return null;
        var method = requestLine[0];
        var path = requestLine[1];

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var colon = lines[i].IndexOf(':');
            if (colon > 0)
            {
                var key = lines[i][..colon].Trim();
                var value = lines[i][(colon + 1)..].Trim();
                headers[key] = value;
            }
        }

        // 读取 body
        string body = string.Empty;
        if (headers.TryGetValue("Content-Length", out var lenStr) && int.TryParse(lenStr, out var contentLength) && contentLength > 0)
        {
            if (contentLength > 10 * 1024 * 1024)
            {
                throw new InvalidOperationException("HTTP body 过大(>10MB)");
            }
            var bodyBuffer = new byte[contentLength];
            var read = 0;
            while (read < contentLength)
            {
                var n = await stream.ReadAsync(bodyBuffer.AsMemory(read, contentLength - read), ct).ConfigureAwait(false);
                if (n == 0) throw new EndOfStreamException("body 读取不完整");
                read += n;
            }
            body = Encoding.UTF8.GetString(bodyBuffer, 0, read);
        }

        return new HttpRequestInfo(method, path, headers, body);
    }

    private static async Task WriteResponseAsync(Stream stream, int statusCode, string statusText, string? body, CancellationToken ct, Dictionary<string, string>? extraHeaders = null)
    {
        var bodyBytes = body is null ? [] : Encoding.UTF8.GetBytes(body);
        var sb = new StringBuilder(256);
        sb.Append("HTTP/1.1 ").Append(statusCode).Append(' ').Append(statusText).Append("\r\n");
        sb.Append("Content-Type: application/json\r\n");
        sb.Append("Content-Length: ").Append(bodyBytes.Length).Append("\r\n");
        if (extraHeaders is not null)
        {
            foreach (var kv in extraHeaders)
            {
                sb.Append(kv.Key).Append(": ").Append(kv.Value).Append("\r\n");
            }
        }
        sb.Append("\r\n");

        var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
        await stream.WriteAsync(headerBytes, ct).ConfigureAwait(false);
        if (bodyBytes.Length > 0)
        {
            await stream.WriteAsync(bodyBytes, ct).ConfigureAwait(false);
        }
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static bool IsInitializeRequest(string body)
    {
        return body.Contains("\"method\"", StringComparison.Ordinal)
            && body.Contains("\"initialize\"", StringComparison.Ordinal);
    }

    private static string GenerateSessionId()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }

    protected override void OnDispose()
    {
        Stop();
        _cts?.Dispose();
    }
}

/// <summary>HTTP 请求信息(内部解析用)</summary>
internal sealed record HttpRequestInfo(string Method, string Path, Dictionary<string, string> Headers, string Body);
