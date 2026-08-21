namespace McpProtocol;

/// <summary>
/// MCP Streamable HTTP 服务端 — 对齐 2025-11-25 规范:单端点 POST/GET/DELETE,
/// 支持无状态(不分配 session)和有状态(分配 MCP-Session-Id + session 存储)双模式。
/// 复用 McpServer.ProcessMessageAsync 处理 JSON-RPC 消息。使用 HttpListener(无需 AspNetCore 依赖)。
/// </summary>
public sealed class McpHttpServer : IDisposable
{
    private readonly McpServer _server;
    private readonly HttpListener _listener;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _sessions = new(StringComparer.Ordinal);
    private readonly bool _statelessMode;
    private readonly FrozenSet<string> _allowedOrigins;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// 创建 MCP HTTP 服务端
    /// </summary>
    /// <param name="server">底层 MCP 服务器(提供工具/资源/提示处理)</param>
    /// <param name="prefix">监听前缀,如 http://localhost:8080/mcp/</param>
    /// <param name="statelessMode">无状态模式(默认 true):不分配 MCP-Session-Id,每个请求自包含</param>
    /// <param name="allowedOrigins">允许的 Origin 列表(Origin 校验防 DNS rebinding);null/空则允许所有</param>
    public McpHttpServer(McpServer server, string prefix, bool statelessMode = true, IEnumerable<string>? allowedOrigins = null)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        if (string.IsNullOrWhiteSpace(prefix)) throw new ArgumentException("监听前缀不能为空", nameof(prefix));
        _statelessMode = statelessMode;
        _allowedOrigins = (allowedOrigins ?? []).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        _listener = new HttpListener();
        _listener.Prefixes.Add(prefix);
    }

    /// <summary>运行服务端,直到 cancellationToken 取消</summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener.Start();

        while (!_cts.Token.IsCancellationRequested)
        {
            HttpListenerContext? context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException) when (_cts.Token.IsCancellationRequested)
            {
                break;
            }

            _ = HandleRequestAsync(context, _cts.Token);
        }
    }

    /// <summary>停止服务端</summary>
    public void Stop()
    {
        _cts?.Cancel();
        if (_listener.IsListening) _listener.Stop();
    }

    /// <summary>当前活跃会话数(有状态模式)</summary>
    public int ActiveSessionCount => _sessions.Count;

    /// <summary>是否无状态模式</summary>
    public bool IsStatelessMode => _statelessMode;

    private async Task HandleRequestAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        try
        {
            if (!ValidateOrigin(ctx))
            {
                ctx.Response.StatusCode = 403;
                ctx.Response.Close();
                return;
            }

            var method = ctx.Request.HttpMethod.ToUpperInvariant();
            switch (method)
            {
                case "POST":
                    await HandlePostAsync(ctx, ct).ConfigureAwait(false);
                    break;
                case "GET":
                    HandleGet(ctx);
                    break;
                case "DELETE":
                    HandleDelete(ctx);
                    break;
                default:
                    ctx.Response.StatusCode = 405;
                    ctx.Response.Close();
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            Console.WriteLine("McpHttpServer: 处理请求异常,尝试返回 500");
            try
            {
                ctx.Response.StatusCode = 500;
                ctx.Response.Close();
            }
            catch (Exception)
            {
                Console.WriteLine("McpHttpServer: 响应关闭失败已忽略");
            }
        }
    }

    private async Task HandlePostAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var sessionId = ctx.Request.Headers["Mcp-Session-Id"];

        // 有状态模式:带 session 但不存在 → 404(会话过期)
        if (!_statelessMode && !string.IsNullOrEmpty(sessionId) && !_sessions.ContainsKey(sessionId))
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            return;
        }

        var body = await ReadRequestBodyAsync(ctx.Request, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
        {
            ctx.Response.StatusCode = 400;
            ctx.Response.Close();
            return;
        }

        var response = await _server.ProcessMessageAsync(body, ct).ConfigureAwait(false);

        if (response == null)
        {
            ctx.Response.StatusCode = 202;
            ctx.Response.Close();
            return;
        }

        // 有状态模式:initialize 请求响应分配新 session
        if (!_statelessMode && IsInitializeRequest(body))
        {
            var newSessionId = GenerateSessionId();
            _sessions[newSessionId] = DateTime.UtcNow;
            ctx.Response.Headers["Mcp-Session-Id"] = newSessionId;
        }

        var json = McpJsonSerializer.Serialize(response);
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.ContentType = "application/json";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
        ctx.Response.Close();
    }

    private static void HandleGet(HttpListenerContext ctx)
    {
        // 最小实现:不支持 GET SSE 推送,返回 405
        // 完整实现可在此开 SSE 流推送服务端通知(有状态模式)
        ctx.Response.StatusCode = 405;
        ctx.Response.Close();
    }

    private void HandleDelete(HttpListenerContext ctx)
    {
        var sessionId = ctx.Request.Headers["Mcp-Session-Id"];
        if (!string.IsNullOrEmpty(sessionId))
        {
            _sessions.TryRemove(sessionId, out _);
        }
        ctx.Response.StatusCode = 204;
        ctx.Response.Close();
    }

    private bool ValidateOrigin(HttpListenerContext ctx)
    {
        if (_allowedOrigins.Count == 0) return true;
        var origin = ctx.Request.Headers["Origin"];
        if (string.IsNullOrEmpty(origin)) return true;
        return _allowedOrigins.Contains(origin);
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

    private static async Task<string> ReadRequestBodyAsync(HttpListenerRequest request, CancellationToken ct)
    {
        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
        return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _listener.Close();
    }
}
