namespace Core.Agents.Doctor;

using System.Net;
using System.Text;
using System.Text.Json;

/// <summary>
/// 医生 SSE 服务器 — HttpListener 监听，管理多个病人 SSE 连接
/// 复用 McpClient.Transports.SseTransport 的多客户端广播模式
///
/// 路由：
///   GET  /sse?patientId=xxx  ← 病人连接此端点接收医生指令（SSE 推送）
///   POST /events              ← 病人向此端点发送遥测事件
///   GET  /health              ← 健康检查
/// </summary>
public sealed class DoctorSseServer : IDoctorTransport
{
    private readonly int _port;
    private readonly string _host;
    private readonly ILogger? _logger;
    private HttpListener? _listener;
    private readonly Dictionary<string, DoctorSsePatient> _patients = new();
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

    public DoctorSseServer(int port, string host = "localhost", ILogger? logger = null)
    {
        _port = port;
        _host = host;
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

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://{_host}:{_port}/");
        _listener.Start();

        _listenCts = new CancellationTokenSource();
        _listenTask = RunAcceptLoopAsync(_listenCts.Token);

        IsConnected = true;
        _logger?.LogInformation("[DoctorSSE] 服务器已启动: http://{Host}:{Port}/", _host, _port);

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
        DoctorSsePatient? patient;
        await _patientsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { _patients.TryGetValue(patientId, out patient); }
        finally { _patientsLock.Release(); }

        if (patient is null)
        {
            _logger?.LogWarning("[DoctorSSE] 病人 {PatientId} 未连接，无法发送指令", patientId);
            return;
        }

        var sseData = $"event: command\ndata: {EscapeSseData(command)}\n\n";
        var bytes = Encoding.UTF8.GetBytes(sseData);
        await patient.SendAsync(bytes, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("[DoctorSSE] 已发送指令到病人 {PatientId}: {Command}", patientId, command[..Math.Min(command.Length, 100)]);
    }

    /// <inheritdoc/>
    public async Task BroadcastCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        List<DoctorSsePatient> patients;
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
                _logger?.LogDebug(ex, "[DoctorSSE] 广播指令到病人 {PatientId} 失败", patient.PatientId);
            }
        }

        _logger?.LogDebug("[DoctorSSE] 已广播指令到 {Count} 个病人", patients.Count);
    }

    private async Task RunAcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && IsConnected && _listener is not null)
        {
            try
            {
                var context = await _listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleRequestAsync(context, ct), ct);
            }
            catch (HttpListenerException) when (!IsConnected || ct.IsCancellationRequested) { break; }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[DoctorSSE] 接受连接异常");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
    {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        var patientId = context.Request.QueryString["patientId"] ?? Guid.NewGuid().ToString("N")[..8];

        try
        {
            if (path == "/sse")
            {
                await HandleSseConnectionAsync(context, patientId, ct).ConfigureAwait(false);
            }
            else if (path == "/events" && context.Request.HttpMethod == "POST")
            {
                await HandleEventsPostAsync(context, patientId, ct).ConfigureAwait(false);
            }
            else if (path == "/health")
            {
                context.Response.StatusCode = 200;
                var healthBytes = Encoding.UTF8.GetBytes("{\"status\":\"ok\"}");
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(healthBytes, ct).ConfigureAwait(false);
                context.Response.Close();
            }
            else
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[DoctorSSE] 处理请求异常: {Path}", path);
            try { context.Response.StatusCode = 500; context.Response.Close(); }
            catch (Exception closeEx) { System.Diagnostics.Trace.WriteLine($"[DoctorSSE] 关闭响应失败: {closeEx.Message}"); }
        }
    }

    private async Task HandleSseConnectionAsync(HttpListenerContext context, string patientId, CancellationToken ct)
    {
        var response = context.Response;
        response.ContentType = "text/event-stream";
        response.Headers.Add("Cache-Control", "no-cache");
        response.Headers.Add("Connection", "keep-alive");
        response.StatusCode = 200;

        var patient = new DoctorSsePatient(patientId, response.OutputStream);

        await _patientsLock.WaitAsync(ct).ConfigureAwait(false);
        try { _patients[patientId] = patient; }
        finally { _patientsLock.Release(); }

        var endpointMsg = $"event: endpoint\ndata: /events?patientId={patientId}\n\n";
        await patient.SendAsync(Encoding.UTF8.GetBytes(endpointMsg), ct).ConfigureAwait(false);

        _logger?.LogInformation("[DoctorSSE] 病人 {PatientId} 已连接 SSE", patientId);
        PatientConnected?.Invoke(this, patientId);

        try
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await _patientsLock.WaitAsync(ct).ConfigureAwait(false);
            try { _patients.Remove(patientId); }
            finally { _patientsLock.Release(); }

            _logger?.LogInformation("[DoctorSSE] 病人 {PatientId} SSE 连接断开", patientId);
            PatientDisconnected?.Invoke(this, patientId);

            await patient.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task HandleEventsPostAsync(HttpListenerContext context, string patientId, CancellationToken ct)
    {
        using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
        var json = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

        var evt = ParseEventFromJson(json, patientId);
        if (evt is not null)
        {
            _eventChannel.Writer.TryWrite(evt);
            EventReceived?.Invoke(this, evt);
        }

        context.Response.StatusCode = 202;
        context.Response.Close();
    }

    internal static DiagnosticEvent? ParseEventFromJson(string json, string patientId)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var doc = JsonSerializer.Deserialize(json, DoctorSseJsonContext.Default.DictionaryStringJsonElement);
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
            try { _listenTask.GetAwaiter().GetResult(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[DoctorSSE] 等待监听任务完成失败: {ex.Message}"); }
        }

        await _patientsLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var patients = _patients.Values.ToList();
            _patients.Clear();
            await Task.WhenAll(patients.Select(p => p.DisposeAsync().AsTask())).ConfigureAwait(false);
        }
        finally { _patientsLock.Release(); }

        _listener?.Stop();
        _listener?.Close();
        _listener = null;

        _eventChannel.Writer.TryComplete();
        _patientsLock.Dispose();
    }
}

/// <summary>
/// SSE 病人连接 — 封装单个病人的 SSE 输出流
/// </summary>
internal sealed class DoctorSsePatient : IAsyncDisposable
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    public string PatientId { get; }
    public Stream OutputStream { get; }

    public DoctorSsePatient(string patientId, Stream outputStream)
    {
        PatientId = patientId;
        OutputStream = outputStream;
    }

    public async Task SendAsync(byte[] data, CancellationToken cancellationToken)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DoctorSsePatient));

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await OutputStream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            await OutputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _writeLock.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _writeLock.Dispose();

        try { await OutputStream.DisposeAsync().ConfigureAwait(false); }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[DoctorSSE] 释放病人输出流失败: {ex.Message}"); }
    }
}

[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class DoctorSseJsonContext : JsonSerializerContext;
