namespace Core.Agents.Doctor;

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

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
    private readonly SemaphoreSlim _patientsLock = new(1, 1);
    private readonly Channel<DiagnosticEvent> _eventChannel;
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
            _patientsLock.Wait();
            try { return _patients.Keys.ToList(); }
            finally { _patientsLock.Release(); }
        }
    }

    /// <inheritdoc/>
    public event EventHandler<DiagnosticEvent>? EventReceived;

    /// <inheritdoc/>
    public event EventHandler<string>? PatientConnected;

    /// <inheritdoc/>
    public event EventHandler<string>? PatientDisconnected;

    public DoctorTcpServer(int port)
    {
        _port = port;
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
        await _patientsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { _patients.TryGetValue(patientId, out patient); }
        finally { _patientsLock.Release(); }

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
        await _patientsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { patients = _patients.Values.ToList(); }
        finally { _patientsLock.Release(); }

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
                _ = Task.Run(() => HandleClientAsync(tcpClient, ct), ct);
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
                tcpClient.Close();
                return;
            }

            var path = request.Path;
            var patientId = request.QueryParams.GetValueOrDefault("patientId") ?? Guid.NewGuid().ToString("N")[..8];

            if (path == "/sse")
            {
                await HandleSseConnectionAsync(stream, patientId, ct).ConfigureAwait(false);
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
        finally
        {
            try { tcpClient.Close(); }
            catch (Exception closeEx) { System.Diagnostics.Trace.WriteLine($"[DoctorTCP] 关闭连接失败: {closeEx.Message}"); }
        }
    }

    private async Task HandleSseConnectionAsync(NetworkStream stream, string patientId, CancellationToken ct)
    {
        var responseHeader = "HTTP/1.1 200 OK\r\n" +
                             "Content-Type: text/event-stream\r\n" +
                             "Cache-Control: no-cache\r\n" +
                             "Connection: keep-alive\r\n" +
                             "\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(responseHeader);
        await stream.WriteAsync(headerBytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        var patient = new DoctorTcpPatient(patientId, stream);

        await _patientsLock.WaitAsync(ct).ConfigureAwait(false);
        try { _patients[patientId] = patient; }
        finally { _patientsLock.Release(); }

        var endpointMsg = $"event: endpoint\ndata: /events?patientId={patientId}\n\n";
        await patient.SendAsync(Encoding.UTF8.GetBytes(endpointMsg), ct).ConfigureAwait(false);

        DoctorDiag.Write($"[DoctorTCP] 病人 {patientId} 已连接 SSE");
        PatientConnected?.Invoke(this, patientId);

        try
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        finally
        {
            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _patientsLock.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
                try { _patients.Remove(patientId); }
                finally { _patientsLock.Release(); }
            }
            catch (OperationCanceledException) { }

            DoctorDiag.Write($"[DoctorTCP] 病人 {patientId} SSE 连接断开");
            PatientDisconnected?.Invoke(this, patientId);
        }
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

    internal static DiagnosticEvent? ParseEventFromJson(string json, string patientId)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var doc = JsonSerializer.Deserialize(json, DoctorTcpJsonContext.Default.DictionaryStringJsonElement);
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
            try { _listenTask.GetAwaiter().GetResult(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[DoctorTCP] 等待监听任务完成失败: {ex.Message}"); }
        }

        await _patientsLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var patients = _patients.Values.ToList();
            _patients.Clear();
            await Task.WhenAll(patients.Select(p => p.DisposeAsync().AsTask())).ConfigureAwait(false);
        }
        finally { _patientsLock.Release(); }

        try { _listener?.Stop(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[DoctorTCP] TcpListener.Stop 失败: {ex.Message}"); }
        _listener = null;

        _eventChannel.Writer.TryComplete();
        _patientsLock.Dispose();
    }
}

/// <summary>
/// TCP 病人连接 — 封装单个病人的 SSE 输出流
/// </summary>
internal sealed class DoctorTcpPatient : IAsyncDisposable
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    public string PatientId { get; }
    public NetworkStream Stream { get; }

    public DoctorTcpPatient(string patientId, NetworkStream stream)
    {
        PatientId = patientId;
        Stream = stream;
    }

    public async Task SendAsync(byte[] data, CancellationToken cancellationToken)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DoctorTcpPatient));

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            await Stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _writeLock.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _writeLock.Dispose();

        try { await Stream.DisposeAsync().ConfigureAwait(false); }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[DoctorTCP] 释放病人输出流失败: {ex.Message}"); }
    }
}

/// <summary>
/// HTTP 请求解析结果
/// </summary>
internal sealed class HttpRequestInfo
{
    public string Method { get; }
    public string Path { get; }
    public Dictionary<string, string> QueryParams { get; }
    public string Body { get; }
    public int ContentLength { get; }

    public HttpRequestInfo(string method, string path, Dictionary<string, string> queryParams, string body, int contentLength)
    {
        Method = method;
        Path = path;
        QueryParams = queryParams;
        Body = body;
        ContentLength = contentLength;
    }
}

[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class DoctorTcpJsonContext : JsonSerializerContext;
