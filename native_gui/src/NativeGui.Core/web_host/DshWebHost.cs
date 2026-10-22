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
    private const string BinaryUploadPath = "/api/session/uploadFileBinary";

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
        var fileSystem = new PhysicalFileSystem();
        var root = DshWebAssets.EnsureExtracted(fileSystem);
        var host = new DshWebHost(root, FindFreePort(), services ?? new DshHostServices(fileSystem));
        host._listener.Prefixes.Add(host.BaseUrl);
        host._listener.Start();
        host._acceptLoop = host.AcceptLoopAsync();
        GuiDiag.Log($"[DSH-WEB] listening {host.BaseUrl} root={root}");
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
            Console.Error.WriteLine($"[DSH-WEB] request failed {context.Request.RawUrl}: {ex}");
            GuiDiag.Log($"[DSH-WEB] request failed {context.Request.RawUrl}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
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
            // 附件直传（TASK058 P2-B2）：POST /api/session/uploadFileBinary?sessionId=&name=，body 为 octet-stream
            if (string.Equals(path, BinaryUploadPath, StringComparison.Ordinal)) {
                await HandleBinaryUploadAsync(context);
                return;
            }
            await HandleRpcAsync(context);
            return;
        }
        if (path is "/" or "") {
            await ServeFileAsync(context, "index.html");
            return;
        }
        await ServeFileAsync(context, path.TrimStart('/'));
    }

    /// <summary>
    /// 附件二进制直传：body 原样字节（Blob/流式大文件免 base64 膨胀），魔数校验与落盘复用
    /// <see cref="DshAttachmentStore"/>；响应与 RPC 同信封（前端 parseFileUploadResult 统一解析）。
    /// </summary>
    private async Task HandleBinaryUploadAsync(HttpListenerContext context) {
        var response = context.Response;
        try {
            var query = context.Request.Url?.Query ?? "";
            var sessionId = QueryValue(query, "sessionId");
            var name = QueryValue(query, "name");
            DshRpcResult result;
            if (string.IsNullOrWhiteSpace(sessionId)) {
                result = DshRpcResults.Fail("fileUploads/rejected", "uploadFileBinary 需要 sessionId 与 name 查询参数");
            } else {
                using var buffer = new MemoryStream();
                await context.Request.InputStream.CopyToAsync(buffer);
                result = await _services.Attachments.UploadAsync(sessionId, Convert.ToBase64String(buffer.ToArray()), name, _lifetime.Token);
            }
            var reply = new DshRpcResponse { RpcId = "upload", Result = result };
            var payload = JsonSerializer.SerializeToUtf8Bytes(reply, DshJsonContext.Default.DshRpcResponse);
            response.StatusCode = 200;
            response.ContentType = "application/json; charset=utf-8";
            response.Headers["Cache-Control"] = "no-store";
            response.ContentLength64 = payload.Length;
            await response.OutputStream.WriteAsync(payload);
        } catch (Exception ex) {
            GuiDiag.Log($"[DSH-WEB] binary upload failed: {ex.Message}");
            TryClose(response, 500);
        } finally {
            TryClose(response, 200);
        }
    }

    /// <summary>解析查询串单值（无解码需求：sessionId/name 均为简单 ASCII）</summary>
    private static string? QueryValue(string query, string key) {
        foreach (var pair in query.TrimStart('?').Split('&')) {
            var equals = pair.IndexOf('=');
            if (equals > 0 && string.Equals(pair[..equals], key, StringComparison.OrdinalIgnoreCase)) {
                return pair[(equals + 1)..];
            }
        }
        return null;
    }

    /// <summary>一元 RPC：解析信封 → 分发 → 回写信封（失败也回 200，错误在 result.error）</summary>
    private async Task HandleRpcAsync(HttpListenerContext context) {
        var response = context.Response;
        DshRpcRequest? envelope;
        try {
            envelope = await JsonSerializer.DeserializeAsync(context.Request.InputStream, DshJsonContext.Default.DshRpcRequest)
                ;
        } catch (JsonException ex) {
            GuiDiag.Log($"[DSH-WEB] rpc parse failed {context.Request.Url}: {ex.Message}");
            TryClose(response, 400);
            return;
        }
        if (envelope is null || envelope.Type != "client-request" || string.IsNullOrEmpty(envelope.RpcId)) {
            GuiDiag.Log($"[DSH-WEB] rpc invalid envelope {context.Request.Url}");
            TryClose(response, 400);
            return;
        }
        var args = envelope.Payload?.Args;
        var result = await _dispatcher.HandleAsync(envelope.Method, args ?? default, _lifetime.Token);
        if (!result.Ok) {
            GuiDiag.Log($"[DSH-WEB] rpc {envelope.Method} -> {result.Error?.Code}: {result.Error?.Message}");
        }
        try {
            if (result.Ok && result.Binary is { } binary) {
                await WriteMultipartReplyAsync(response, envelope.RpcId, result, binary);
                return;
            }
            var reply = new DshRpcResponse { RpcId = envelope.RpcId, Result = result };
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

    /// <summary>
    /// 二进制一元响应：按 DSH gateway 的 multipart 契约回写 —— metadata 字段为 JSON 信封
    /// （value 内保留 null 占位 + attachments 清单），bytes-0 字段为原始字节 Blob。
    /// 前端 parseBinaryResponse 仅在 Content-Type 基类型为 multipart/form-data 时启用。
    /// </summary>
    private static async Task WriteMultipartReplyAsync(HttpListenerResponse response, string rpcId, DshRpcResult result, DshRpcBinaryAttachment binary) {
        const string partName = "bytes-0";
        var boundary = $"dsh-{Guid.NewGuid():N}";
        var metadata = new DshRpcMultipartMetadata {
            RpcId = rpcId,
            Result = result,
            Attachments = [new DshRpcAttachmentRef { Path = binary.Path, Part = partName }],
        };
        var payload = BuildMultipartPayload(boundary, partName, metadata, binary.Bytes);
        response.StatusCode = 200;
        response.ContentType = $"multipart/form-data; boundary={boundary}";
        response.Headers["Cache-Control"] = "no-store";
        response.ContentLength64 = payload.Length;
        await response.OutputStream.WriteAsync(payload);
    }

    /// <summary>
    /// 组装 multipart 响应体（metadata JSON + 原始字节 Blob）。
    /// 一次成型返回完整数组，避免「流写完后指针在末尾再 CopyTo 复制 0 字节」导致 Content-Length 不符、
    /// 客户端无限等待（真机已踩）。
    /// </summary>
    internal static byte[] BuildMultipartPayload(string boundary, string partName, DshRpcMultipartMetadata metadata, byte[] bytes) {
        using var payload = new MemoryStream();
        WriteAscii(payload, $"--{boundary}\r\nContent-Disposition: form-data; name=\"metadata\"\r\n\r\n");
        payload.Write(JsonSerializer.SerializeToUtf8Bytes(metadata, DshJsonContext.Default.DshRpcMultipartMetadata));
        WriteAscii(payload, $"\r\n--{boundary}\r\nContent-Disposition: form-data; name=\"{partName}\"; filename=\"bytes.bin\"\r\nContent-Type: application/octet-stream\r\n\r\n");
        payload.Write(bytes);
        WriteAscii(payload, $"\r\n--{boundary}--\r\n");
        return payload.ToArray();
    }

    /// <summary>写入 multipart 分隔符（ASCII，无 BOM）</summary>
    private static void WriteAscii(Stream stream, string text) {
        var bytes = Encoding.ASCII.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
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
                    GuiDiag.Log($"[DSH-WEB] stream parse failed: {ex.Message}");
                    continue;
                }
                if (open is null || open.Type != "open" || string.IsNullOrEmpty(open.StreamId)) {
                    continue;
                }
                var send = (Func<DshStreamItem, Task>)(item => SendItemAsync(socket, sendGate, item));
                var (opening, lease, ready) = await OpenStreamAsync(open, send, _lifetime.Token);
                if (lease is not null) {
                    leases.Add(lease);
                }
                foreach (var item in opening) {
                    await send(item);
                }
                if (ready is not null) {
                    await ready();
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
    private async Task<(IReadOnlyList<DshStreamItem> Opening, IDisposable? Lease, Func<Task>? Ready)> OpenStreamAsync(
        DshStreamOpen open, Func<DshStreamItem, Task> send, CancellationToken cancellationToken) {
        GuiDiag.Log($"[DSH-WEB] stream open endpoint={open.Endpoint} id={open.StreamId}");
        switch (open.Endpoint) {
            case "$events": {
                // ready 首帧 + 挂接权限确认桥（后续 waterfall 帧走同一条流；连接关闭时 fail-closed 清挂起请求）
                var clientId = Guid.NewGuid().ToString();
                Func<JsonElement, Task> sendFrame = frame => send(Item(open.StreamId, frame));
                _services.Approvals.Attach(sendFrame, clientId);
                // goal/activation-changed 事件桥（TASK058 P2-B5）：activation 翻转即推 waterfall 帧
                void OnGoalChanged(string sessionId, DshGoalDocument goal) {
                    _ = sendFrame(JsonSerializer.SerializeToElement(new DshWaterfallFrame {
                        Event = "goal/activation-changed",
                        EventId = Guid.NewGuid().ToString("N"),
                        AgentId = sessionId,
                        Request = JsonSerializer.SerializeToElement(new DshGoalActivationChanged {
                            SessionId = sessionId,
                            Goal = new DshGoalActivation { Id = goal.Id, Revision = goal.Revision, Activation = goal.Activation },
                        }, DshJsonContext.Default.DshGoalActivationChanged),
                    }, DshJsonContext.Default.DshWaterfallFrame)).ContinueWith(
                        static t => GuiDiag.Log($"[DSH-GOALS] activation frame send failed: {t.Exception?.Message}"),
                        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                }
                _services.Goals.GoalChanged += OnGoalChanged;
                return ([Item(open.StreamId, JsonSerializer.SerializeToElement(new DshEventsReady {
                    ClientId = clientId,
                    Host = new DshHostInfo { Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) },
                }, DshJsonContext.Default.DshEventsReady))], new DetachLease(() => {
                    _services.Goals.GoalChanged -= OnGoalChanged;
                    _services.Approvals.Detach();
                }), null);
            }
            case "workspace/follow": {
                var baseline = await _services.Workspaces.ReadBaselineAsync(cancellationToken);
                return ([Item(open.StreamId, JsonSerializer.SerializeToElement(
                    new DshWorkspaceBaseline { Value = baseline }, DshJsonContext.Default.DshWorkspaceBaseline))], null, null);
            }
            case "session/control": {
                var projections = await _services.Sessions.ProjectionsMapAsync(DshSessionStore.MaxSessions, cancellationToken);
                return ([Item(open.StreamId, JsonSerializer.SerializeToElement(
                    new DshSessionControlBaseline { Value = new DshSessionControlValue { Projections = projections } },
                    DshJsonContext.Default.DshSessionControlBaseline))], null, null);
            }
            case "session/follow":
                return await OpenSessionAsync(open, send, cancellationToken);
            case "workspaceFiles/changes":
                return await OpenWorkspaceFilesChangesAsync(open, send, cancellationToken);
            case "job/list":
                return await OpenJobListAsync(open, send, cancellationToken);
            case "job/follow":
                return await OpenJobFollowAsync(open, send, cancellationToken);
            case "terminal/follow":
                return OpenTerminalFollow(open, send, cancellationToken);
            case "terminal/retain":
                return OpenTerminalRetain(open);
            default:
                GuiDiag.Log($"[DSH-WEB] stream endpoint unimplemented: {open.Endpoint}");
                return ([], null, null);
        }
    }

    /// <summary>
    /// 打开文件变更流（TASK058 P2-B4）：ready 首帧 + 后台泵把 watcher 变更帧推到连接；
    /// 关闭时取消泵并释放 watcher 租约（幂等）。会话/路径非法时发可执行引导错误帧。
    /// </summary>
    private async Task<(IReadOnlyList<DshStreamItem> Opening, IDisposable? Lease, Func<Task>? Ready)> OpenWorkspaceFilesChangesAsync(
        DshStreamOpen open, Func<DshStreamItem, Task> send, CancellationToken cancellationToken) {
        var request = open.Payload?.Args.Deserialize(DshJsonContext.Default.DshWorkspaceFilesArgs);
        if (request is null || string.IsNullOrWhiteSpace(request.WorkspaceFileScopeId)) {
            var raw = open.Payload?.Args.ToString() ?? "<null>";
            return ([Item(open.StreamId, StreamError("workspaceFiles/rejected",
                $"workspaceFiles/changes 需要 {{workspaceFileScopeId, path}}；收到 args={raw[..Math.Min(120, raw.Length)]}"))], null, null);
        }
        var feed = await _services.WorkspaceFiles.CreateChangeFeedAsync(request.WorkspaceFileScopeId, request.Path, cancellationToken);
        if (feed.Error is { } error) {
            return ([Item(open.StreamId, StreamError(error.Error?.Code ?? "workspaceFiles/rejected", error.Error?.Message ?? "变更流打开失败"))], null, null);
        }
        var frames = feed.Frames ?? throw new InvalidOperationException("变更流产物缺帧通道（内部不变量破坏）");
        var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pump = Task.Run(async () => {
            try {
                await foreach (var frame in frames.Reader.ReadAllAsync(pumpCts.Token).ConfigureAwait(false)) {
                    await send(Item(open.StreamId, frame)).ConfigureAwait(false);
                }
            } catch (OperationCanceledException) {
                // 连接关闭/宿主停机 — 泵静默收尾
            } catch (Exception ex) {
                GuiDiag.Log($"[DSH-WEB] changes pump failed: {ex.Message}");
            }
        }, pumpCts.Token);
        return (
            [Item(open.StreamId, JsonSerializer.SerializeToElement(new DshChangeReady(), DshJsonContext.Default.DshChangeReady))],
            new DetachLease(() => {
                pumpCts.Cancel();
                feed.Lease?.Dispose();
                pumpCts.Dispose();
            }),
            null);
    }

    /// <summary>
    /// 打开后台任务花名册流（TASK066 C2）：先推一帧 rows，之后轮询引擎后台代理，
    /// 花名册变化时补推（DSH 侧只在任务注册/状态变化时收到新帧）。
    /// </summary>
    private async Task<(IReadOnlyList<DshStreamItem> Opening, IDisposable? Lease, Func<Task>? Ready)> OpenJobListAsync(
        DshStreamOpen open, Func<DshStreamItem, Task> send, CancellationToken cancellationToken) {
        var request = open.Payload?.Args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshJobListArgs)?.Request;
        if (request is null || string.IsNullOrWhiteSpace(request.SessionId)) {
            return ([Item(open.StreamId, StreamError("job/rejected",
                "job/list 需要 {request:{sessionId}}；请先选择一个会话再打开任务面板"))], null, null);
        }
        var sessionId = request.SessionId;
        var initial = await _services.Jobs.ListAsync(sessionId, cancellationToken);
        var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pump = Task.Run(() => PumpJobRowsAsync(open.StreamId, sessionId, initial, send, pumpCts.Token), pumpCts.Token);
        return (
            [JobRowsItem(open.StreamId, initial)],
            new DetachLease(() => {
                pumpCts.Cancel();
                pumpCts.Dispose();
            }),
            null);
    }

    /// <summary>花名册轮询泵：只在投影变化时推帧（会话切换/引擎未就绪时静默）</summary>
    private async Task PumpJobRowsAsync(
        string streamId, string sessionId, IReadOnlyList<DshJobView> initial,
        Func<DshStreamItem, Task> send, CancellationToken cancellationToken) {
        var previous = JsonSerializer.Serialize(
            new DshJobRowsFrame { Jobs = [.. initial] }, DshJsonContext.Default.DshJobRowsFrame);
        while (!cancellationToken.IsCancellationRequested) {
            try {
                await Task.Delay(JobPollInterval, cancellationToken).ConfigureAwait(false);
                var jobs = await _services.Jobs.ListAsync(sessionId, cancellationToken).ConfigureAwait(false);
                var current = JsonSerializer.Serialize(
                    new DshJobRowsFrame { Jobs = [.. jobs] }, DshJsonContext.Default.DshJobRowsFrame);
                if (!string.Equals(previous, current, StringComparison.Ordinal)) {
                    previous = current;
                    await send(JobRowsItem(streamId, jobs)).ConfigureAwait(false);
                }
            } catch (OperationCanceledException) {
                return;
            } catch (Exception ex) {
                GuiDiag.Log($"[DSH-JOBS] rows pump failed: {ex.Message}");
                return;
            }
        }
    }

    /// <summary>
    /// 打开单个任务的输出跟随流（TASK066 C2）：opened 锚点 → output 增量 → 终态 status 后正常关闭。
    /// 输出由代理活动文本合成，字节游标保证重连从 `from` 续读。
    /// </summary>
    private async Task<(IReadOnlyList<DshStreamItem> Opening, IDisposable? Lease, Func<Task>? Ready)> OpenJobFollowAsync(
        DshStreamOpen open, Func<DshStreamItem, Task> send, CancellationToken cancellationToken) {
        var request = open.Payload?.Args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshJobFollowArgs)?.Request;
        if (request is null || string.IsNullOrWhiteSpace(request.JobId)) {
            return ([Item(open.StreamId, StreamError("job/rejected",
                "job/follow 需要 {request:{jobId, sessionId?, from?}}；请先在任务列表里选择一个任务"))], null, null);
        }
        var sessionId = request.SessionId ?? "";
        var snapshot = await _services.Jobs.SnapshotAsync(sessionId, request.JobId, cancellationToken);
        if (snapshot is null) {
            return ([Item(open.StreamId, StreamError("job/not-found",
                $"找不到后台任务 {request.JobId}；任务可能已被清理，请刷新任务列表"))], null, null);
        }
        var total = Encoding.UTF8.GetByteCount(snapshot.OutputText);
        var cursor = Math.Clamp(request.From ?? 0, 0, total);
        var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pump = Task.Run(() => PumpJobOutputAsync(
            open.StreamId, sessionId, request.JobId, cursor, snapshot, send, pumpCts.Token), pumpCts.Token);
        return (
            [Item(open.StreamId, JsonSerializer.SerializeToElement(
                new DshJobOpenedFrame { Job = snapshot.View, From = cursor },
                DshJsonContext.Default.DshJobOpenedFrame))],
            new DetachLease(() => {
                pumpCts.Cancel();
                pumpCts.Dispose();
            }),
            null);
    }

    /// <summary>输出跟随泵：按字节游标增量推 output，排空且终态后推 status 并退出</summary>
    private async Task PumpJobOutputAsync(
        string streamId, string sessionId, string jobId, long cursor, DshJobSnapshot initial,
        Func<DshStreamItem, Task> send, CancellationToken cancellationToken) {
        var snapshot = initial;
        while (!cancellationToken.IsCancellationRequested) {
            try {
                var bytes = Encoding.UTF8.GetBytes(snapshot.OutputText);
                if (cursor < bytes.LongLength) {
                    var offset = (int)cursor;
                    var length = bytes.Length - offset;
                    var text = Encoding.UTF8.GetString(bytes, offset, length);
                    await send(Item(streamId, JsonSerializer.SerializeToElement(new DshJobOutputFrame {
                        Chunks = [new DshJobChunk { At = cursor, Text = text, Channel = "log" }],
                        Next = bytes.LongLength,
                    }, DshJsonContext.Default.DshJobOutputFrame))).ConfigureAwait(false);
                    cursor = bytes.LongLength;
                }
                if (snapshot.Terminal) {
                    await send(Item(streamId, JsonSerializer.SerializeToElement(
                        new DshJobStatusFrame { Job = snapshot.View },
                        DshJsonContext.Default.DshJobStatusFrame))).ConfigureAwait(false);
                    return;
                }
                await Task.Delay(JobPollInterval, cancellationToken).ConfigureAwait(false);
                snapshot = await _services.Jobs.SnapshotAsync(sessionId, jobId, cancellationToken).ConfigureAwait(false)
                    ?? snapshot;
            } catch (OperationCanceledException) {
                return;
            } catch (Exception ex) {
                GuiDiag.Log($"[DSH-JOBS] follow pump failed: {ex.Message}");
                return;
            }
        }
    }

    /// <summary>任务花名册帧项</summary>
    private static DshStreamItem JobRowsItem(string streamId, IReadOnlyList<DshJobView> jobs) =>
        Item(streamId, JsonSerializer.SerializeToElement(
            new DshJobRowsFrame { Jobs = [.. jobs] }, DshJsonContext.Default.DshJobRowsFrame));

    /// <summary>后台任务轮询间隔（引擎是拉取式快照；500ms 足够覆盖任务面板刷新）</summary>
    private static readonly TimeSpan JobPollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// 打开终端跟随流（TASK066 C3）：snapshot（屏幕基线 + 元数据）→ output/state 增量。
    /// 断开只释放跟随者，PTY 进程继续运行；重新 follow 从最新基线恢复。
    /// </summary>
    private (IReadOnlyList<DshStreamItem> Opening, IDisposable? Lease, Func<Task>? Ready) OpenTerminalFollow(
        DshStreamOpen open, Func<DshStreamItem, Task> send, CancellationToken cancellationToken) {
        var request = open.Payload?.Args.Deserialize(DshJsonContext.Default.DshTerminalFollowArgs);
        if (request is null
            || string.IsNullOrWhiteSpace(request.AgentId)
            || string.IsNullOrWhiteSpace(request.Id)
            || string.IsNullOrWhiteSpace(request.AttachmentId)) {
            return ([Item(open.StreamId, StreamError("terminal/rejected",
                "terminal/follow 需要 {agentId, id, attachmentId}；先用 terminal/create 建立终端并保存 attachmentId"))], null, null);
        }
        DshTerminalSnapshotFrame snapshot;
        DshTerminalFollower follower;
        try {
            (snapshot, follower) = _services.Terminals.Follow(request.AgentId, request.Id, request.AttachmentId);
        } catch (DshTerminalException ex) {
            return ([Item(open.StreamId, StreamError(ex.Code, ex.Message))], null, null);
        }
        var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pump = Task.Run(() => PumpTerminalFramesAsync(open.StreamId, follower, send, pumpCts.Token), pumpCts.Token);
        return (
            [Item(open.StreamId, JsonSerializer.SerializeToElement(snapshot, DshJsonContext.Default.DshTerminalSnapshotFrame))],
            new DetachLease(() => {
                pumpCts.Cancel();
                follower.Dispose();
                pumpCts.Dispose();
            }),
            null);
    }

    /// <summary>终端跟随泵：把输出/状态帧转成 DSH 流项；跟随者结束（进程退出）后自然收尾</summary>
    private static async Task PumpTerminalFramesAsync(
        string streamId, DshTerminalFollower follower, Func<DshStreamItem, Task> send, CancellationToken cancellationToken) {
        try {
            await foreach (var frame in follower.Frames.ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
                var value = frame switch {
                    DshTerminalOutputFrame output => JsonSerializer.SerializeToElement(output, DshJsonContext.Default.DshTerminalOutputFrame),
                    DshTerminalStateFrame state => JsonSerializer.SerializeToElement(state, DshJsonContext.Default.DshTerminalStateFrame),
                    _ => throw new InvalidOperationException($"未知终端帧类型 {frame.GetType().Name}"),
                };
                await send(Item(streamId, value)).ConfigureAwait(false);
            }
        } catch (OperationCanceledException) {
            // 连接关闭/宿主停机 — 泵静默收尾
        } catch (Exception ex) {
            GuiDiag.Log($"[DSH-TERMINAL] follow pump failed: {ex.Message}");
        }
    }

    /// <summary>终端保留声明（TASK066 C3）：终端存在即回 retained；进程生命周期与流无关</summary>
    private (IReadOnlyList<DshStreamItem> Opening, IDisposable? Lease, Func<Task>? Ready) OpenTerminalRetain(DshStreamOpen open) {
        var request = open.Payload?.Args.Deserialize(DshJsonContext.Default.DshTerminalRetainArgs);
        if (request is null || string.IsNullOrWhiteSpace(request.SessionId) || string.IsNullOrWhiteSpace(request.Id)) {
            return ([Item(open.StreamId, StreamError("terminal/rejected",
                "terminal/retain 需要 {sessionId, id}；请从 terminal/list 取得实际终端 ID"))], null, null);
        }
        try {
            _services.Terminals.Retain(request.SessionId, request.Id);
        } catch (DshTerminalException ex) {
            return ([Item(open.StreamId, StreamError(ex.Code, ex.Message))], null, null);
        }
        return ([Item(open.StreamId, JsonSerializer.SerializeToElement(
            new DshTerminalRetainedFrame(), DshJsonContext.Default.DshTerminalRetainedFrame))], null, null);
    }

    /// <summary>打开会话事件流的首帧：宿主事件日志（或转录投影）→ snapshot + 订阅后续事件；否则给出可执行的引导错误</summary>
    private async Task<(IReadOnlyList<DshStreamItem> Opening, IDisposable? Lease, Func<Task>? Ready)> OpenSessionAsync(
        DshStreamOpen open, Func<DshStreamItem, Task> send, CancellationToken cancellationToken) {
        var sessionId = FollowSessionId(open);
        if (string.IsNullOrWhiteSpace(sessionId)) {
            return ([Item(open.StreamId, StreamError("session/rejected", "打开会话需要 {address:{kind:'session',sessionId:'<会话 ID>'}}；子代理地址暂不支持，请先在侧栏选择普通会话"))], null, null);
        }
        if (!_services.Sessions.IsReady) {
            return ([Item(open.StreamId, StreamError("session/not-ready", "会话存储尚未就绪（引擎仍在组装中），稍后重试即可"))], null, null);
        }
        var snapshot = await _services.Sessions.SnapshotAsync(sessionId, cancellationToken, _services.Streams);
        if (snapshot is null) {
            return ([Item(open.StreamId, StreamError("session/unknown", $"会话 {sessionId} 不在磁盘上（~/.jcc/sessions/{sessionId}/transcript.json 不存在）；用 session/create 建一个新会话，或从侧栏的会话列表里选一个"))], null, null);
        }
        _services.Streams.Observe(sessionId, snapshot.Cursor, MaxTurn(snapshot.Records));
        var follow = FollowRequest(open);
        // 基线必须与订阅在同一把锁里取（ADR 0147）：否则快照与首帧之间可能漏帧 → 前端断流
        var (baseline, lease) = _services.Streams.SubscribeSession(
            sessionId,
            open.StreamId,
            sessionEvent => send(Item(
                open.StreamId,
                JsonSerializer.SerializeToElement(new DshSessionRecord { Event = sessionEvent }, DshJsonContext.Default.DshSessionRecord))),
            follow?.AssistantStream == true
                ? frame => SendAssistantFrameAsync(open.StreamId, send, frame)
                : null,
            snapshot.Cursor);
        snapshot.AssistantStream = baseline;
        return (
            [Item(open.StreamId, JsonSerializer.SerializeToElement(snapshot, DshJsonContext.Default.DshSessionFollowSnapshot))],
            lease,
            async () => {
                // 开场项（快照）已发出：先补发持久事件，再放行缓存的瞬态帧
                await _services.Streams.FlushAsync(open.StreamId);
                await _services.Streams.ActivateAsync(open.StreamId);
            });
    }

    /// <summary>包装一帧 assistant-stream（瞬态通道信封，前端按 type 分流）</summary>
    /// <param name="streamId">前端流身份</param>
    /// <param name="send">底层发送回调</param>
    /// <param name="frame">帧内容</param>
    private static Task SendAssistantFrameAsync(string streamId, Func<DshStreamItem, Task> send, DshAssistantStreamFrame frame) =>
        send(Item(streamId, JsonSerializer.SerializeToElement(new DshAssistantStreamEnvelope {
            Frame = frame,
        }, DshJsonContext.Default.DshAssistantStreamEnvelope)));

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
        var address = FollowRequest(open)?.Address;
        return address is null || address.Kind != "session" ? null : address.SessionId;
    }

    /// <summary>解出 session/follow 的请求体（args 是 {request:{...}} 信封）</summary>
    /// <param name="open">流打开帧</param>
    private static DshSessionFollowArgs? FollowRequest(DshStreamOpen open) {
        if (open.Payload?.Args is not { ValueKind: JsonValueKind.Object } args) {
            return null;
        }
        return args.Deserialize(DshJsonContext.Default.DshRequestEnvelopeDshSessionFollowArgs)?.Request;
    }

    /// <summary>构造流式端点失败帧（引导式错误：说明期望参数与下一步可执行动作）</summary>
    private static JsonElement StreamError(string code, string message) =>
        JsonSerializer.SerializeToElement(
            new DshStreamError {
                Error = new DshStreamErrorBody {
                    Code = code,
                    Message = message,
                    Details = JsonSerializer.SerializeToElement(new DshRpcErrorDetails(), DshJsonContext.Default.DshRpcErrorDetails),
                },
            },
            DshJsonContext.Default.DshStreamError);

    private static DshStreamItem Item(string streamId, JsonElement value) => new() { StreamId = streamId, Value = value };

    /// <summary>流连接关闭时的清理动作句柄（$events 摘除权限桥通道等）</summary>
    private sealed record DetachLease(Action Detach) : IDisposable {
        /// <summary>执行清理（幂等由被调方保证）</summary>
        public void Dispose() => Detach();
    }

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
                GuiDiag.Log($"[DSH-WEB] combo item missing: {item}");
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
            GuiDiag.Log($"[DSH-WEB] 404 {relative}");
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
            GuiDiag.Log($"[DSH-WEB] close {statusCode} skipped: {ex.GetType().Name}");
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
            GuiDiag.Log($"[DSH-WEB] listener stop skipped: {ex.GetType().Name}");
        }
        if (_acceptLoop is not null) {
            await _acceptLoop;
        }
        _lifetime.Dispose();
        GuiDiag.Log($"[DSH-WEB] stopped {BaseUrl}");
    }
}
