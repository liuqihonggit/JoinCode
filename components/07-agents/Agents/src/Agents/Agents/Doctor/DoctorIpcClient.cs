namespace Core.Agents.Doctor;

/// <summary>
/// 医生 IPC 客户端 — 从病人 stdout 读取 NDJSON 遥测事件，解析为 DiagnosticEvent
/// 复用 BridgeSubprocessHandle 的 NDJSON 解析模式
/// </summary>
public sealed class DoctorIpcClient : IDoctorTransport
{
    private readonly PatientProcessManager _patientManager;
    private readonly ILogger? _logger;
    private readonly Channel<DiagnosticEvent> _eventChannel;
    private int _isDisposed;

    /// <summary>是否已连接</summary>
    public bool IsConnected { get; private set; }

    /// <summary>诊断事件接收事件</summary>
    public event EventHandler<DiagnosticEvent>? EventReceived;

    public DoctorIpcClient(PatientProcessManager patientManager, ILogger? logger = null)
    {
        _patientManager = patientManager ?? throw new ArgumentNullException(nameof(patientManager));
        _logger = logger;
        _eventChannel = Channel.CreateBounded<DiagnosticEvent>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });

        _patientManager.OutputLineReceived += OnOutputLineReceived;
    }

    /// <summary>
    /// 连接到病人进程 — 订阅 stdout 事件流
    /// </summary>
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        IsConnected = true;
        _logger?.LogInformation("[Doctor] IPC 客户端已连接，开始监听病人 stdout");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 读取下一条诊断事件
    /// </summary>
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

    /// <summary>
    /// 向病人进程发送指令（通过 stdin）
    /// </summary>
    public async Task SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        var stdin = _patientManager.StandardInput;
        if (stdin is null || !stdin.BaseStream.CanWrite)
        {
            _logger?.LogWarning("[Doctor] 病人 stdin 不可写，无法发送指令");
            return;
        }

        await stdin.WriteAsync(command.AsMemory(), cancellationToken).ConfigureAwait(false);
        await stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("[Doctor] 已发送指令: {Command}", command);
    }

    /// <summary>
    /// 处理病人 stdout 行 — 解析 NDJSON 为 DiagnosticEvent
    /// </summary>
    private void OnOutputLineReceived(object? sender, string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        try
        {
            var evt = ParseDiagnosticEvent(line);
            if (evt is not null)
            {
                _eventChannel.Writer.TryWrite(evt);
                EventReceived?.Invoke(this, evt);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "[Doctor] 解析病人 stdout 行失败: {Line}", line[..Math.Min(line.Length, 200)]);
        }
    }

    /// <summary>
    /// 从 NDJSON 行解析 DiagnosticEvent
    /// 识别 [WIRE] [STEP] [READY] [MAIN] 等诊断标记
    /// </summary>
    internal static DiagnosticEvent? ParseDiagnosticEvent(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var eventType = DetectEventType(line);
        if (eventType is null) return null;

        return new DiagnosticEvent
        {
            EventType = eventType,
            RawData = line,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// 检测事件类型 — 从行内容推断
    /// </summary>
    private static string? DetectEventType(string line)
    {
        if (line.Contains("[WIRE]")) return "wire_trace";
        if (line.Contains("[STEP]")) return "step_trace";
        if (line.Contains("[READY]")) return "ready";
        if (line.Contains("[MAIN]")) return "main_trace";
        if (line.Contains("LoopDetected", StringComparison.OrdinalIgnoreCase)) return "loop_detected";
        if (line.Contains("PermissionDenied", StringComparison.OrdinalIgnoreCase)) return "permission_denied";
        if (line.Contains("ApiError", StringComparison.OrdinalIgnoreCase)) return "api_error";
        if (line.Contains("ApiTimeout", StringComparison.OrdinalIgnoreCase)) return "api_timeout";
        if (line.Contains("ContextOverflow", StringComparison.OrdinalIgnoreCase)) return "context_overflow";

        if (line.StartsWith('{'))
        {
            try
            {
                var json = JsonSerializer.Deserialize(line, DoctorJsonContext.Default.DictionaryStringJsonElement);
                if (json is not null && json.TryGetValue("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
                {
                    return typeEl.GetString();
                }
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[Doctor] NDJSON 解析失败: {ex.Message}"); }
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1) return;

        _patientManager.OutputLineReceived -= OnOutputLineReceived;
        _eventChannel.Writer.TryComplete();
        IsConnected = false;

        await Task.CompletedTask.ConfigureAwait(false);
    }
}

/// <summary>
/// Doctor IPC JSON 序列化上下文 — AOT 兼容
/// </summary>
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class DoctorJsonContext : JsonSerializerContext;
