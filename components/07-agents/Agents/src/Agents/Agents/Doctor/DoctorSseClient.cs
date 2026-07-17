namespace Core.Agents.Doctor;

using System.Net.Http;
using System.Text;

/// <summary>
/// 医生 SSE 客户端 — 病人端使用，连接医生的 SSE 服务器
/// 启动时 GET /sse 接收医生指令，运行时 POST /events 发送遥测数据
///
/// 用法：jcc.exe --doctor-endpoint http://localhost:9902
/// </summary>
public sealed class DoctorSseClient : IAsyncDisposable
{
    private readonly string _endpoint;
    private readonly string _patientId;
    private readonly ILogger? _logger;
    private readonly HttpClient _httpClient;
    private readonly CancellationTokenSource _cts;
    private Task? _sseListenTask;
    private int _isDisposed;

    /// <summary>病人 ID</summary>
    public string PatientId => _patientId;

    /// <summary>是否已连接</summary>
    public bool IsConnected { get; private set; }

    /// <summary>收到医生指令事件</summary>
    public event EventHandler<string>? CommandReceived;

    public DoctorSseClient(string endpoint, string? patientId = null, ILogger? logger = null)
    {
        _endpoint = endpoint.TrimEnd('/');
        _patientId = patientId ?? Guid.NewGuid().ToString("N")[..8];
        _logger = logger;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _cts = new CancellationTokenSource();
    }

    /// <summary>
    /// 连接到医生 SSE 服务器 — 启动 SSE 监听循环
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected) return;

        _logger?.LogInformation("[DoctorSSE-Client] 连接医生: {Endpoint}, 病人ID: {PatientId}", _endpoint, _patientId);

        _sseListenTask = ListenSseAsync(_cts.Token);

        IsConnected = true;
    }

    /// <summary>
    /// 发送遥测事件到医生
    /// </summary>
    public async Task SendEventAsync(DiagnosticEvent evt, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return;

        var sb = new StringBuilder();
        sb.Append("{\"type\":\"").Append(JsonEscape(evt.EventType)).Append("\"");
        sb.Append(",\"patientId\":\"").Append(JsonEscape(_patientId)).Append("\"");
        sb.Append(",\"timestamp\":\"").Append(evt.Timestamp.ToString("o")).Append("\"");
        if (evt.SessionId is not null)
            sb.Append(",\"sessionId\":\"").Append(JsonEscape(evt.SessionId)).Append("\"");
        if (evt.Properties.Count > 0)
        {
            sb.Append(",\"properties\":{");
            var first = true;
            foreach (var kv in evt.Properties)
            {
                if (!first) sb.Append(',');
                sb.Append('"').Append(JsonEscape(kv.Key)).Append("\":\"").Append(JsonEscape(kv.Value)).Append('"');
                first = false;
            }
            sb.Append('}');
        }
        sb.Append('}');

        var content = new StringContent(sb.ToString(), Encoding.UTF8, "application/json");
        var url = $"{_endpoint}/events?patientId={_patientId}";

        try
        {
            var response = await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("[DoctorSSE-Client] 发送事件失败: {StatusCode}", response.StatusCode);
            }
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogDebug(ex, "[DoctorSSE-Client] 发送事件网络异常");
        }
    }

    /// <summary>
    /// 发送简单文本事件到医生
    /// </summary>
    public async Task SendTextEventAsync(string eventType, string? data = null, CancellationToken cancellationToken = default)
    {
        var evt = new DiagnosticEvent
        {
            EventType = eventType,
            PatientId = _patientId,
            RawData = data,
            Timestamp = DateTimeOffset.UtcNow
        };

        await SendEventAsync(evt, cancellationToken).ConfigureAwait(false);
    }

    private async Task ListenSseAsync(CancellationToken ct)
    {
        var retryDelay = TimeSpan.FromSeconds(1);
        var maxRetryDelay = TimeSpan.FromSeconds(30);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var url = $"{_endpoint}/sse?patientId={_patientId}";
                using var stream = await _httpClient.GetStreamAsync(url, ct).ConfigureAwait(false);

                _logger?.LogInformation("[DoctorSSE-Client] SSE 连接已建立: {Url}", url);

                await foreach (var sseEvent in ParseSseStreamAsync(stream, ct).ConfigureAwait(false))
                {
                    if (sseEvent.EventType == "command")
                    {
                        _logger?.LogDebug("[DoctorSSE-Client] 收到医生指令: {Data}", sseEvent.Data);
                        CommandReceived?.Invoke(this, sseEvent.Data);
                    }
                    else if (sseEvent.EventType == "endpoint")
                    {
                        _logger?.LogDebug("[DoctorSSE-Client] 收到端点信息: {Data}", sseEvent.Data);
                    }
                }

                _logger?.LogWarning("[DoctorSSE-Client] SSE 流结束，将重连");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (HttpRequestException ex)
            {
                _logger?.LogDebug(ex, "[DoctorSSE-Client] SSE 连接失败，{Delay}ms 后重连", retryDelay.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "[DoctorSSE-Client] SSE 监听异常，{Delay}ms 后重连", retryDelay.TotalMilliseconds);
            }

            try { await Task.Delay(retryDelay, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }

            retryDelay = TimeSpan.FromMilliseconds(Math.Min(retryDelay.TotalMilliseconds * 2, maxRetryDelay.TotalMilliseconds));
        }
    }

    /// <summary>
    /// 内联 SSE 事件流解析 — 不依赖 Transport.Impl.SseStreamParser
    /// 对齐 SSE 规范: event:/data:/id:/空行分隔
    /// </summary>
    private static async IAsyncEnumerable<(string EventType, string Data)> ParseSseStreamAsync(
        Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

        var eventType = string.Empty;
        var dataBuilder = new StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break;

            if (string.IsNullOrEmpty(line))
            {
                if (dataBuilder.Length > 0)
                {
                    yield return (eventType, dataBuilder.ToString());
                    dataBuilder.Clear();
                    eventType = string.Empty;
                }
                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventType = line.AsSpan(6).Trim().ToString();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (dataBuilder.Length > 0) dataBuilder.AppendLine();
                dataBuilder.Append(line.AsSpan(5).Trim());
            }
        }

        if (dataBuilder.Length > 0)
        {
            yield return (eventType, dataBuilder.ToString());
        }
    }

    private static string JsonEscape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1) return;

        IsConnected = false;
        await _cts.CancelAsync().ConfigureAwait(false);

        if (_sseListenTask is not null)
        {
            try { _sseListenTask.GetAwaiter().GetResult(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[DoctorSSE-Client] 等待SSE监听任务完成失败: {ex.Message}"); }
        }

        _cts.Dispose();
        _httpClient.Dispose();
    }
}
