namespace JoinCode.Gui.WebHost;

/// <summary>
/// DSH 前端宿主（ADR 0143）：在本机回环端口托管 dsh-web-frontend 资产，并实现其 HTTP/WS 协议。
/// 路由：
/// <list type="bullet">
/// <item><c>GET /</c> → 宿主注入后的 index.html</item>
/// <item><c>GET /plugins/??a,b&amp;rev=x</c> → combo 拼接（DSH 的按包脚本合并约定）</item>
/// <item><c>POST /api/{method}</c> → 一元 RPC 信封（见 <see cref="DshRpcDispatcher"/>)</item>
/// <item><c>WS /api/remote.mux</c> → 流式端点（$events / workspace/follow / session/control）</item>
/// <item>其余 → 静态文件（assets/favicon/manifest）</item>
/// </list>
/// 页面与宿主同源，故不需要 token/cookie 交换（DSH 设计用于外部浏览器，这里是内嵌私有页面）。
/// </summary>
internal sealed class DshWebHost : IAsyncDisposable {
    private const string StreamPath = "/api/remote.mux";
    private const string ApiPrefix = "/api/";
    private const string ComboPrefix = "/plugins/??";

    private static readonly FrozenDictionary<string, string> ContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            [".js"] = "text/javascript; charset=utf-8",
            [".css"] = "text/css; charset=utf-8",
            [".html"] = "text/html; charset=utf-8",
            [".json"] = "application/json; charset=utf-8",
            [".webmanifest"] = "application/manifest+json; charset=utf-8",
            [".svg"] = "image/svg+xml",
            [".png"] = "image/png",
            [".woff"] = "font/woff",
            [".woff2"] = "font/woff2",
            [".ttf"] = "font/ttf",
            [".txt"] = "text/plain; charset=utf-8",
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private readonly HttpListener _listener = new();
    private readonly string _root;
    private readonly DshRpcDispatcher _dispatcher;
    private readonly DshHostServices _services;
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _acceptLoop;
    private int _disposed;

    private DshWebHost(string root, int port, DshHostServices services) {
        _root = root;
        _services = services;
        _dispatcher = new DshRpcDispatcher(services);
        Port = port;
        BaseUrl = $"http://127.0.0.1:{port}/";
    }

    /// <summary>监听端口</summary>
    internal int Port { get; }

    /// <summary>页面根地址（含结尾斜杠）</summary>
    internal string BaseUrl { get; }

    /// <summary>解包资产 + 选空闲端口 + 启动监听</summary>
    internal static DshWebHost Start(DshHostServices? services = null) {
        var root = DshWebAssets.EnsureExtracted();
        var host = new DshWebHost(root, FindFreePort(), services ?? new DshHostServices(new PhysicalFileSystem()));
        host._listener.Prefixes.Add(host.BaseUrl);
        host._listener.Start();
        host._acceptLoop = host.AcceptLoopAsync();
        App.LogDiag($"[DSH-WEB] listening {host.BaseUrl} root={root}");
        return host;
    }

    /// <summary>请求受理循环（每个请求独立任务，不阻塞后续连接）</summary>
    private async Task AcceptLoopAsync() {
        while (!_lifetime.IsCancellationRequested) {
            HttpListenerContext context;
            try {
                context = await _listener.GetContextAsync();
            } catch (Exception) when (_lifetime.IsCancellationRequested) {
                return;
            }
            _ = HandleAsync(context);
        }
    }

    /// <summary>单请求处理（异常只记录，不打断监听）</summary>
    private async Task HandleAsync(HttpListenerContext context) {
        try {
            await RouteAsync(context);
        } catch (Exception ex) {
            App.LogDiag($"[DSH-WEB] request failed {context.Request.RawUrl}: {ex.Message}");
            TryClose(context.Response, 500);
        }
    }

    /// <summary>路由分发</summary>
    private async Task RouteAsync(HttpListenerContext context) {
        var request = context.Request;
        var raw = request.RawUrl ?? "/";
        if (request.IsWebSocketRequest && raw.StartsWith(StreamPath, StringComparison.Ordinal)) {
            await HandleStreamAsync(context);
            return;
        }
        if (raw.StartsWith(ComboPrefix, StringComparison.Ordinal)) {
            await ServeComboAsync(context, raw);
            return;
        }
        var path = request.Url?.AbsolutePath ?? "/";
        if (path.StartsWith(ApiPrefix, StringComparison.Ordinal) || path == "/api") {
            await HandleRpcAsync(context);
            return;
        }
        if (path is "/" or "") {
            await ServeFileAsync(context, "index.html");
            return;
        }
        await ServeFileAsync(context, path.TrimStart('/'));
    }

    /// <summary>一元 RPC：解析信封 → 分发 → 回写信封（失败也回 200，错误在 result.error）</summary>
    private async Task HandleRpcAsync(HttpListenerContext context) {
        var response = context.Response;
        DshRpcRequest? envelope;
        try {
            envelope = await JsonSerializer.DeserializeAsync(context.Request.InputStream, DshJsonContext.Default.DshRpcRequest)
                ;
        } catch (JsonException ex) {
            App.LogDiag($"[DSH-WEB] rpc parse failed {context.Request.Url}: {ex.Message}");
            TryClose(response, 400);
            return;
        }
        if (envelope is null || envelope.Type != "client-request" || string.IsNullOrEmpty(envelope.RpcId)) {
            App.LogDiag($"[DSH-WEB] rpc invalid envelope {context.Request.Url}");
            TryClose(response, 400);
            return;
        }
        var args = envelope.Payload?.Args;
        var result = await _dispatcher.HandleAsync(envelope.Method, args ?? default, _lifetime.Token);
        if (!result.Ok) {
            App.LogDiag($"[DSH-WEB] rpc {envelope.Method} -> {result.Error?.Code}: {result.Error?.Message}");
        }
        var reply = new DshRpcResponse { RpcId = envelope.RpcId, Result = result };
        try {
            var payload = JsonSerializer.SerializeToUtf8Bytes(reply, DshJsonContext.Default.DshRpcResponse);
            response.StatusCode = 200;
            response.ContentType = "application/json; charset=utf-8";
            response.Headers["Cache-Control"] = "no-store";
            response.ContentLength64 = payload.Length;
            await response.OutputStream.WriteAsync(payload);
        } finally {
            TryClose(response, 200);
        }
    }

    /// <summary>WebSocket 流：回发打开帧产生的项，并登记需要持续推送的端点（session/follow）</summary>
    private async Task HandleStreamAsync(HttpListenerContext context) {
        var accepted = await context.AcceptWebSocketAsync(null);
        using var socket = accepted.WebSocket;
        using var sendGate = new SemaphoreSlim(1, 1);
        var leases = new List<IDisposable>();
        try {
            var buffer = new byte[64 * 1024];
            while (socket.State == WebSocketState.Open && !_lifetime.IsCancellationRequested) {
                var received = await ReceiveTextAsync(socket, buffer);
                if (received is null) {
                    return;
                }
                DshStreamOpen? open;
                try {
                    open = JsonSerializer.Deserialize(received, DshJsonContext.Default.DshStreamOpen);
                } catch (JsonException ex) {
                    App.LogDiag($"[DSH-WEB] stream parse failed: {ex.Message}");
                    continue;
                }
                if (open is null || open.Type != "open" || string.IsNullOrEmpty(open.StreamId)) {
                    continue;
                }
                var send = (Func<DshStreamItem, Task>)(item => SendItemAsync(socket, sendGate, item));
                var (opening, lease) = await OpenStreamAsync(open, send, _lifetime.Token);
                if (lease is not null) {
                    leases.Add(lease);
                }
                foreach (var item in opening) {
                    await send(item);
                }
            }
        } finally {
            foreach (var lease in leases) {
                lease.Dispose();
            }
        }
    }

    /// <summary>发送一条流式项（同一连接串行，避免并发写 WebSocket）</summary>
    private static async Task SendItemAsync(WebSocket socket, SemaphoreSlim gate, DshStreamItem item) {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(item, DshJsonContext.Default.DshStreamItem);
        await gate.WaitAsync();
        try {
            if (socket.State == WebSocketState.Open) {
                await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
            }
        } finally {
            gate.Release();
        }
    }

    /// <summary>
    /// 按端点产生初始数据项（$events 就绪、workspace/follow 读真实注册表、session/control 读会话投影）；
    /// <c>session/follow</c> 额外返回一个订阅句柄，宿主把后续事件持续推到该连接。
    /// </summary>
    private async Task<(IReadOnlyList<DshStreamItem> Opening, IDisposable? Lease)> OpenStreamAsync(
        DshStreamOpen open, Func<DshStreamItem, Task> send, CancellationToken cancellationToken) {
        App.LogDiag($"[DSH-WEB] stream open endpoint={open.Endpoint} id={open.StreamId}");
        switch (open.Endpoint) {
            case "$events":
                return ([Item(open.StreamId, JsonSerializer.SerializeToElement(new DshEventsReady {
                    ClientId = Guid.NewGuid().ToString(),
                    Host = new DshHostInfo { Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) },
                }, DshJsonContext.Default.DshEventsReady))], null);
            case "workspace/follow": {
                var baseline = await _services.Workspaces.ReadBaselineAsync(cancellationToken);
                return ([Item(open.StreamId, JsonSerializer.SerializeToElement(
                    new DshWorkspaceBaseline { Value = baseline }, DshJsonContext.Default.DshWorkspaceBaseline))], null);
            }
            case "session/control": {
                var projections = await _services.Sessions.ProjectionsMapAsync(DshSessionStore.MaxSessions, cancellationToken);
                return ([Item(open.StreamId, JsonSerializer.SerializeToElement(
                    new DshSessionControlBaseline { Value = new DshSessionControlValue { Projections = projections } },
                    DshJsonContext.Default.DshSessionControlBaseline))], null);
            }
            case "session/follow":
                return await OpenSessionAsync(open, send, cancellationToken);
            default:
                App.LogDiag($"[DSH-WEB] stream endpoint unimplemented: {open.Endpoint}");
                return ([], null);
        }
    }

    /// <summary>打开会话事件流的首帧：磁盘会话 → snapshot + 订阅后续事件；否则给出可执行的引导错误</summary>
    private async Task<(IReadOnlyList<DshStreamItem> Opening, IDisposable? Lease)> OpenSessionAsync(
        DshStreamOpen open, Func<DshStreamItem, Task> send, CancellationToken cancellationToken) {
        var sessionId = FollowSessionId(open);
        if (string.IsNullOrWhiteSpace(sessionId)) {
            return ([Item(open.StreamId, StreamError("session/rejected", "打开会话需要 {address:{kind:'session',sessionId:'<会话 ID>'}}；子代理地址暂不支持，请先在侧栏选择普通会话"))], null);
        }
        if (!_services.Sessions.IsReady) {
            return ([Item(open.StreamId, StreamError("session/not-ready", "会话存储尚未就绪（引擎仍在组装中），稍后重试即可"))], null);
        }
        var snapshot = await _services.Sessions.SnapshotAsync(sessionId, cancellationToken);
        if (snapshot is null) {
            return ([Item(open.StreamId, StreamError("session/unknown", $"会话 {sessionId} 不在磁盘上（~/.jcc/sessions/{sessionId}/transcript.json 不存在）；用 session/create 建一个新会话，或从侧栏的会话列表里选一个"))], null);
        }
        _services.Streams.Observe(sessionId, snapshot.Cursor, MaxTurn(snapshot.Records));
        var lease = _services.Streams.Subscribe(sessionId, open.StreamId, sessionEvent => send(Item(
            open.StreamId,
            JsonSerializer.SerializeToElement(new DshSessionRecord { Event = sessionEvent }, DshJsonContext.Default.DshSessionRecord))));
        return ([Item(open.StreamId, JsonSerializer.SerializeToElement(snapshot, DshJsonContext.Default.DshSessionFollowSnapshot))], lease);
    }

    /// <summary>快照事件页里的最大轮次（无事件为 0）</summary>
    private static int MaxTurn(IReadOnlyList<DshSessionRecord> records) =>
        records
            .Where(record => record.Event.Type == "turn/start")
            .Select(record => record.Event.Data.TryGetProperty("turn", out var turn) && turn.TryGetInt32(out var value) ? value : 0)
            .DefaultIfEmpty(0)
            .Max();

    /// <summary>取出 session/follow 请求里的目标会话 ID（非普通会话地址返回 null）</summary>
    /// <param name="open">流打开帧</param>
    /// <returns>会话 ID；地址缺失或非 kind=session 时为 null</returns>
    internal static string? FollowSessionId(DshStreamOpen open) {
        if (open.Payload?.Args is not { ValueKind: JsonValueKind.Object } args) {
            return null;
        }
        var envelope = args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshSessionFollowArgs);
        var address = envelope?.Request?.Address;
        return address is null || address.Kind != "session" ? null : address.SessionId;
    }

    /// <summary>构造流式端点失败帧（引导式错误：说明期望参数与下一步可执行动作）</summary>
    private static JsonElement StreamError(string code, string message) =>
        JsonSerializer.SerializeToElement(
            new DshStreamError { Error = new DshStreamErrorBody { Code = code, Message = message } },
            DshJsonContext.Default.DshStreamError);

    private static DshStreamItem Item(string streamId, JsonElement value) => new() { StreamId = streamId, Value = value };

    /// <summary>读取一条完整文本消息（分片聚合），连接关闭/空消息返回 null</summary>
    private static async Task<string?> ReceiveTextAsync(WebSocket socket, byte[] buffer) {
        var builder = new StringBuilder();
        while (true) {
            var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close) {
                return null;
            }
            builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (result.EndOfMessage) {
                return builder.ToString();
            }
        }
    }

    /// <summary>combo 路由：<c>/plugins/??a,b&amp;rev=x</c> 按序拼接（保留 DSH 原始 URL 形态）</summary>
    private async Task ServeComboAsync(HttpListenerContext context, string raw) {
        var list = raw[ComboPrefix.Length..];
        var end = list.IndexOf('&');
        if (end >= 0) {
            list = list[..end];
        }
        var response = context.Response;
        response.StatusCode = 200;
        response.ContentType = "text/javascript; charset=utf-8";
        response.Headers["Cache-Control"] = "no-store";
        foreach (var item in list.Split(',', StringSplitOptions.RemoveEmptyEntries)) {
            var file = ResolveUnderRoot(Path.Combine("plugins", item));
            if (file is null || !File.Exists(file)) {
                App.LogDiag($"[DSH-WEB] combo item missing: {item}");
                continue;
            }
            await using var stream = File.OpenRead(file);
            await stream.CopyToAsync(response.OutputStream);
            await response.OutputStream.WriteAsync("\n;"u8.ToArray());
        }
        TryClose(response, 200);
    }

    /// <summary>静态文件（index.html 不缓存；资产短缓存）</summary>
    private async Task ServeFileAsync(HttpListenerContext context, string relative) {
        var response = context.Response;
        var file = ResolveUnderRoot(relative);
        if (file is null || !File.Exists(file)) {
            App.LogDiag($"[DSH-WEB] 404 {relative}");
            TryClose(response, 404);
            return;
        }
        var extension = Path.GetExtension(file);
        response.StatusCode = 200;
        response.ContentType = ContentTypes.TryGetValue(extension, out var contentType) ? contentType : "application/octet-stream";
        response.Headers["Cache-Control"] = "no-store";
        var content = await File.ReadAllBytesAsync(file);
        response.ContentLength64 = content.Length;
        await response.OutputStream.WriteAsync(content);
        TryClose(response, 200);
    }

    /// <summary>拼接并把路径限制在资产根内（防目录穿越）</summary>
    private string? ResolveUnderRoot(string relative) {
        var root = Path.GetFullPath(_root) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    /// <summary>尽力关闭响应（连接已被对端断开时忽略）</summary>
    private static void TryClose(HttpListenerResponse response, int statusCode) {
        try {
            response.StatusCode = statusCode;
            response.Close();
        } catch (Exception ex) {
            App.LogDiag($"[DSH-WEB] close {statusCode} skipped: {ex.GetType().Name}");
        }
    }

    /// <summary>让操作系统分配一个空闲回环端口</summary>
    private static int FindFreePort() {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) {
            return;
        }
        _lifetime.Cancel();
        try {
            _listener.Stop();
            _listener.Close();
        } catch (Exception ex) {
            App.LogDiag($"[DSH-WEB] listener stop skipped: {ex.GetType().Name}");
        }
        if (_acceptLoop is not null) {
            await _acceptLoop;
        }
        _lifetime.Dispose();
        App.LogDiag($"[DSH-WEB] stopped {BaseUrl}");
    }
}
