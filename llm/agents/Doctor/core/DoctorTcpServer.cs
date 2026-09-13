namespace Core.Agents.Doctor;


/// <summary>
/// 医生 TCP 服务器 — TcpListener 监听，管理多个病人连接
/// 替代 DoctorSseServer（HttpListener 在受限环境不可用）
///
/// 路由：
///   GET  /sse?patientId=xxx  ← 病人连接此端点接收医生指令（SSE 推送）
///   POST /events              ← 病人向此端点发送遥测事件
///   GET  /health              ← 健康检查
/// </summary>
public sealed class DoctorTcpServer : IDoctorTransport
{
    private readonly int _port;
    private TcpListener? _listener;
    private readonly Dictionary<string, DoctorTcpPatient> _patients = new();
    private readonly AsyncLock _patientsLock = new();
    private readonly Channel<DiagnosticEvent> _eventChannel;
    private readonly ILogger<DoctorTcpServer>? _logger;
    private CancellationTokenSource? _listenCts;
    private Task? _listenTask;
    private int _isDisposed;

    /// <inheritdoc/>
    public bool IsConnected { get; private set; }

    /// <inheritdoc/>
    public IReadOnlyList<string> ConnectedPatientIds
    {
        get
        {
            using var guard = _patientsLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_patientsLock.Name}' 等待超时");
            return _patients.Keys.ToList();
        }
    }

    /// <inheritdoc/>
    public event EventHandler<DiagnosticEvent>? EventReceived;

    /// <inheritdoc/>
    public event EventHandler<string>? PatientConnected;

    /// <inheritdoc/>
    public event EventHandler<string>? PatientDisconnected;

    /// <summary>
    /// 构造医生 TCP 服务器
    /// </summary>
    /// <param name="port">监听端口</param>
    /// <param name="logger">日志记录器（可选）</param>
    public DoctorTcpServer(int port, ILogger<DoctorTcpServer>? logger = null)
    {
        _port = port;
        _logger = logger;
        _eventChannel = Channel.CreateBounded<DiagnosticEvent>(new BoundedChannelOptions(512)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <inheritdoc/>
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected) return Task.CompletedTask;

        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();

        _listenCts = new CancellationTokenSource();
        _listenTask = Task.Run(() => RunAcceptLoopAsync(_listenCts.Token), CancellationToken.None);

        IsConnected = true;
        DoctorDiag.Write($"[DoctorTCP] 服务器已启动: http://127.0.0.1:{_port}/");

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<DiagnosticEvent?> ReadEventAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return null;

        try
        {
            return await _eventChannel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task SendCommandAsync(string patientId, string command, CancellationToken cancellationToken = default)
    {
        DoctorTcpPatient? patient;
        {
            using var guard = _patientsLock.TryLock(cancellationToken) ?? throw new System.TimeoutException($"锁 '{_patientsLock.Name}' 等待超时");
 _patients.TryGetValue(patientId, out patient); 
        }

        if (patient is null)
        {
            DoctorDiag.WriteError($"[DoctorTCP] 病人 {patientId} 未连接，无法发送指令");
            return;
        }

        var sseData = $"event: command\ndata: {EscapeSseData(command)}\n\n";
        var bytes = Encoding.UTF8.GetBytes(sseData);
        await patient.SendAsync(bytes, cancellationToken).ConfigureAwait(false);
        DoctorDiag.Write($"[DoctorTCP] 已发送指令到病人 {patientId}: {command[..Math.Min(command.Length, 100)]}");
    }

    /// <inheritdoc/>
    public async Task BroadcastCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        List<DoctorTcpPatient> patients;
        {
            using var guard = _patientsLock.TryLock(cancellationToken) ?? throw new System.TimeoutException($"锁 '{_patientsLock.Name}' 等待超时");
 patients = _patients.Values.ToList(); 
        }

        var sseData = $"event: command\ndata: {EscapeSseData(command)}\n\n";
        var bytes = Encoding.UTF8.GetBytes(sseData);

        foreach (var patient in patients)
        {
            try { await patient.SendAsync(bytes, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex)
            {
                DoctorDiag.WriteError($"[DoctorTCP] 广播指令到病人 {patient.PatientId} 失败: {ex.Message}");
            }
        }

        DoctorDiag.Write($"[DoctorTCP] 已广播指令到 {patients.Count} 个病人");
    }

    private async Task RunAcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && IsConnected && _listener is not null)
        {
            try
            {
                var tcpClient = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                DoctorDiag.Write($"[DoctorTCP] 接受新连接: {tcpClient.Client.RemoteEndPoint}");
                _ = Task.Run(async () =>
                {
                    using var c = tcpClient;
                    await HandleClientAsync(c, ct).ConfigureAwait(false);
                }, ct);
            }
            catch (SocketException) when (!IsConnected || ct.IsCancellationRequested) { break; }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                DoctorDiag.WriteError($"[DoctorTCP] 接受连接异常: {ex.Message}");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient tcpClient, CancellationToken ct)
    {
        var stream = tcpClient.GetStream();
        var remoteEndPoint = tcpClient.Client.RemoteEndPoint?.ToString() ?? "unknown";

        try
        {
            var request = await ReadHttpRequestAsync(stream, ct).ConfigureAwait(false);
            if (request is null)
            {
                return;
            }

            var path = request.Path;
            var patientId = request.QueryParams.GetValueOrDefault("patientId") ?? Guid.NewGuid().ToString("N")[..8];

            if (path == "/sse")
            {
                await HandleSseConnectionAsync(tcpClient, stream, patientId, ct).ConfigureAwait(false);
            }
            else if (path == "/events" && request.Method == "POST")
            {
                await HandleEventsPostAsync(stream, request.Body, patientId, ct).ConfigureAwait(false);
            }
            else if (path == "/health")
            {
                await WriteHttpResponseAsync(stream, 200, "application/json", "{\"status\":\"ok\"}"u8.ToArray(), ct).ConfigureAwait(false);
            }
            else
            {
                await WriteHttpResponseAsync(stream, 404, "text/plain", "Not Found"u8.ToArray(), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || !IsConnected) { }
        catch (Exception ex)
        {
            DoctorDiag.WriteError($"[DoctorTCP] 处理请求异常 ({remoteEndPoint}): {ex.Message}");
        }
    }

    private async Task HandleSseConnectionAsync(TcpClient tcpClient, NetworkStream stream, string patientId, CancellationToken ct)
    {
        var responseHeader = "HTTP/1.1 200 OK\r\n" +
                             "Content-Type: text/event-stream\r\n" +
                             "Cache-Control: no-cache\r\n" +
                             "Connection: keep-alive\r\n" +
                             "\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(responseHeader);
        await stream.WriteAsync(headerBytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        var patient = new DoctorTcpPatient(patientId, stream, _logger);
        AddPatient(patientId, patient);

        var endpointMsg = $"event: endpoint\ndata: /events?patientId={patientId}\n\n";
        await patient.SendAsync(Encoding.UTF8.GetBytes(endpointMsg), ct).ConfigureAwait(false);

        DoctorDiag.Write($"[DoctorTCP] 病人 {patientId} 已连接 SSE");
        PatientConnected?.Invoke(this, patientId);

        try
        {
            while (!ct.IsCancellationRequested && tcpClient.Connected)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            RemovePatient(patientId);
            DoctorDiag.Write($"[DoctorTCP] 病人 {patientId} SSE 连接断开");
            PatientDisconnected?.Invoke(this, patientId);
        }
    }

    /// <summary>添加病人到连接表</summary>
    private void AddPatient(string patientId, DoctorTcpPatient patient)
    {
        using var guard = _patientsLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_patientsLock.Name}' 等待超时");
 _patients[patientId] = patient; 
    }

    /// <summary>从连接表移除病人（5秒超时）</summary>
    private void RemovePatient(string patientId)
    {
        using var guard = _patientsLock.TryLock();
        if (guard is null) return;
 _patients.Remove(patientId); 
    }

    private async Task HandleEventsPostAsync(NetworkStream stream, string body, string patientId, CancellationToken ct)
    {
        var evt = ParseEventFromJson(body, patientId);
        if (evt is not null)
        {
            _eventChannel.Writer.TryWrite(evt);
            EventReceived?.Invoke(this, evt);
        }

        await WriteHttpResponseAsync(stream, 202, "text/plain", "Accepted"u8.ToArray(), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 从 JSON 文本解析诊断事件 — 宽容解析，提取 type 字段和全部属性
    /// </summary>
    /// <param name="json">JSON 文本</param>
    /// <param name="patientId">病人标识</param>
    /// <returns>解析出的诊断事件（解析失败则 null）</returns>
    internal static DiagnosticEvent? ParseEventFromJson(string json, string patientId)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var doc = RelaxedJsonSerializer.Deserialize(json, DoctorTcpJsonContext.Default.DictionaryStringJsonElement);
            if (doc is null) return null;

            var eventType = doc.TryGetValue("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String
                ? typeEl.GetString() ?? "unknown"
                : "unknown";

            return new DiagnosticEvent
            {
                EventType = eventType,
                PatientId = patientId,
                RawData = json,
                Timestamp = DateTimeOffset.UtcNow,
                Properties = doc.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.ValueKind switch
                    {
                        JsonValueKind.String => kv.Value.GetString() ?? "",
                        JsonValueKind.Number => kv.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => kv.Value.GetRawText()
                    })
            };
        }
        catch (JsonException) { return null; }
    }

    private static async Task<HttpRequestInfo?> ReadHttpRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var totalRead = 0;

        while (totalRead < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(totalRead), ct).ConfigureAwait(false);
            if (bytesRead == 0) return null;
            totalRead += bytesRead;

            var headerEnd = buffer.AsSpan(0, totalRead).IndexOf("\r\n\r\n"u8);
            if (headerEnd >= 0)
            {
                var headerText = Encoding.ASCII.GetString(buffer, 0, headerEnd);
                var bodyStart = headerEnd + 4;
                var bodyLength = totalRead - bodyStart;

                var request = ParseHttpRequest(headerText, bodyLength > 0 ? Encoding.UTF8.GetString(buffer, bodyStart, bodyLength) : string.Empty);

                if (request is not null && request.ContentLength > bodyLength)
                {
                    var remaining = request.ContentLength - bodyLength;
                    if (totalRead + remaining <= buffer.Length)
                    {
                        while (bodyLength < request.ContentLength)
                        {
                            var extraRead = await stream.ReadAsync(buffer.AsMemory(totalRead), ct).ConfigureAwait(false);
                            if (extraRead == 0) break;
                            totalRead += extraRead;
                            bodyLength = totalRead - bodyStart;
                        }

                        request = ParseHttpRequest(headerText, bodyLength > 0 ? Encoding.UTF8.GetString(buffer, bodyStart, bodyLength) : string.Empty);
                    }
                }

                return request;
            }
        }

        return null;
    }

    /// <summary>
    /// 解析 HTTP 请求头和正文为 HttpRequestInfo — 提取方法、路径、查询参数、Content-Length
    /// </summary>
    /// <param name="headerText">HTTP 请求头文本</param>
    /// <param name="body">HTTP 请求正文</param>
    /// <returns>解析结果（解析失败则 null）</returns>
    internal static HttpRequestInfo? ParseHttpRequest(string headerText, string body)
    {
        var lines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return null;

        var requestLine = lines[0].Split(' ', 3);
        if (requestLine.Length < 2) return null;

        var method = requestLine[0];
        var rawPath = requestLine[1];

        var queryIndex = rawPath.IndexOf('?');
        string path;
        Dictionary<string, string> queryParams;

        if (queryIndex >= 0)
        {
            path = rawPath[..queryIndex];
            var queryString = rawPath[(queryIndex + 1)..];
            queryParams = ParseQueryString(queryString);
        }
        else
        {
            path = rawPath;
            queryParams = new Dictionary<string, string>();
        }

        int contentLength = 0;
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(lines[i].AsSpan(16).Trim(), out contentLength);
            }
        }

        return new HttpRequestInfo(method, path, queryParams, body, contentLength);
    }

    private static Dictionary<string, string> ParseQueryString(string queryString)
    {
        var result = new Dictionary<string, string>();
        foreach (var pair in queryString.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIndex = pair.IndexOf('=');
            if (eqIndex >= 0)
            {
                var key = Uri.UnescapeDataString(pair[..eqIndex]);
                var value = Uri.UnescapeDataString(pair[(eqIndex + 1)..]);
                result[key] = value;
            }
            else
            {
                result[Uri.UnescapeDataString(pair)] = string.Empty;
            }
        }
        return result;
    }

    private static async Task WriteHttpResponseAsync(NetworkStream stream, int statusCode, string contentType, byte[] body, CancellationToken ct)
    {
        var reasonPhrase = statusCode switch
        {
            200 => "OK",
            202 => "Accepted",
            404 => "Not Found",
            500 => "Internal Server Error",
            _ => "Unknown"
        };

        var header = $"HTTP/1.1 {statusCode} {reasonPhrase}\r\n" +
                     $"Content-Type: {contentType}\r\n" +
                     $"Content-Length: {body.Length}\r\n" +
                     "Connection: close\r\n" +
                     "\r\n";

        var headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes, ct).ConfigureAwait(false);
        await stream.WriteAsync(body, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static string EscapeSseData(string data)
    {
        return data.Replace("\n", "\\n").Replace("\r", "");
    }

    /// <summary>
    /// 异步释放资源 — 取消监听、清理所有病人连接、停止 TcpListener
    /// </summary>
    /// <returns>表示异步释放操作的任务</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1) return;

        IsConnected = false;

        if (_listenCts is not null)
        {
            await _listenCts.CancelAsync().ConfigureAwait(false);
            _listenCts.Dispose();
        }

        if (_listenTask is not null)
        {
            try { _listenTask.GetAwaiter().GetResult(); } catch (Exception ex) { _logger?.LogWarning(ex, "[DoctorTCP] 等待监听任务完成失败"); }
        }

        await CleanupPatientsAsync().ConfigureAwait(false);

        try { _listener?.Stop(); } catch (Exception ex) { _logger?.LogWarning(ex, "[DoctorTCP] TcpListener.Stop 失败"); }
        _listener = null;

        _eventChannel.Writer.TryComplete();
        _patientsLock.Dispose();
    }

    /// <summary>清理所有病人连接（在锁保护下执行）</summary>
    private async Task CleanupPatientsAsync()
    {
        using var guard = _patientsLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_patientsLock.Name}' 等待超时");

        var patients = _patients.Values.ToList();
        _patients.Clear();
        await Task.WhenAll(patients.Select(p => p.DisposeAsync().AsTask())).ConfigureAwait(false);
    }
}

/// <summary>
/// TCP 病人连接 — 封装单个病人的 SSE 输出流
/// </summary>
internal sealed class DoctorTcpPatient : IAsyncDisposable
{
    private readonly AsyncLock _writeLock = new();
    private readonly ILogger? _logger;
    private bool _disposed;

    /// <summary>病人标识</summary>
    public string PatientId { get; }

    /// <summary>病人 SSE 输出网络流</summary>
    public NetworkStream Stream { get; }

    /// <summary>
    /// 构造 TCP 病人连接
    /// </summary>
    /// <param name="patientId">病人标识</param>
    /// <param name="stream">网络流</param>
    /// <param name="logger">日志记录器（可选）</param>
    public DoctorTcpPatient(string patientId, NetworkStream stream, ILogger? logger = null)
    {
        PatientId = patientId;
        Stream = stream;
        _logger = logger;
    }

    /// <summary>
    /// 异步发送字节数据到病人 — 加锁保证写入串行化
    /// </summary>
    /// <param name="data">待发送字节数组</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步发送操作的任务</returns>
    public async Task SendAsync(byte[] data, CancellationToken cancellationToken)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DoctorTcpPatient));

        using var guard = _writeLock.TryLock(cancellationToken) ?? throw new System.TimeoutException($"锁 '{_writeLock.Name}' 等待超时");

        await Stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        await Stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    
    }

    /// <summary>
    /// 异步释放资源 — 释放写入锁和网络流
    /// </summary>
    /// <returns>表示异步释放操作的任务</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _writeLock.Dispose();

        try { await Stream.DisposeAsync().ConfigureAwait(false); }
        catch (Exception ex) { _logger?.LogWarning(ex, "[DoctorTCP] 释放病人输出流失败"); }
    }
}

/// <summary>
/// HTTP 请求解析结果
/// </summary>
internal sealed class HttpRequestInfo
{
    /// <summary>HTTP 方法（GET/POST 等）</summary>
    public string Method { get; }

    /// <summary>请求路径（不含查询字符串）</summary>
    public string Path { get; }

    /// <summary>查询参数字典</summary>
    public Dictionary<string, string> QueryParams { get; }

    /// <summary>请求正文</summary>
    public string Body { get; }

    /// <summary>Content-Length 头部值</summary>
    public int ContentLength { get; }

    /// <summary>
    /// 构造 HTTP 请求信息
    /// </summary>
    /// <param name="method">HTTP 方法</param>
    /// <param name="path">请求路径</param>
    /// <param name="queryParams">查询参数字典</param>
    /// <param name="body">请求正文</param>
    /// <param name="contentLength">Content-Length 值</param>
    public HttpRequestInfo(string method, string path, Dictionary<string, string> queryParams, string body, int contentLength)
    {
        Method = method;
        Path = path;
        QueryParams = queryParams;
        Body = body;
        ContentLength = contentLength;
    }
}

/// <summary>
/// DoctorTcpServer 专用 JSON 序列化上下文 — AOT 源码生成
/// </summary>
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
internal sealed partial class DoctorTcpJsonContext : JsonSerializerContext;
