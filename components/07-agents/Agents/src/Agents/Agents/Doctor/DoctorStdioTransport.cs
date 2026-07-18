namespace Core.Agents.Doctor;

using System.Text;
using System.Text.Json;

/// <summary>
/// 医生 stdio 传输 — 从病人 stdout 读取 NDJSON 遥测事件，通过 stdin 发送指令
/// 复用 BridgeSubprocessHandle 的 NDJSON 解析模式
/// 仅支持单病人（1:1 父子进程模式），多病人场景使用 DoctorTcpServer
/// </summary>
public sealed class DoctorStdioTransport : IDoctorTransport
{
    private readonly PatientProcessManager _patientManager;
    private readonly string _patientId;
    private readonly Channel<DiagnosticEvent> _eventChannel;
    private int _isDisposed;

    /// <inheritdoc/>
    public bool IsConnected { get; private set; }

    /// <inheritdoc/>
    public IReadOnlyList<string> ConnectedPatientIds
    {
        get
        {
            var info = _patientManager.GetPatientInfo(_patientId);
            return info is not null ? [_patientId] : [];
        }
    }

    /// <inheritdoc/>
    public event EventHandler<DiagnosticEvent>? EventReceived;

    /// <inheritdoc/>
    public event EventHandler<string>? PatientConnected;

    /// <inheritdoc/>
    public event EventHandler<string>? PatientDisconnected;

    public DoctorStdioTransport(PatientProcessManager patientManager, string patientId)
    {
        _patientManager = patientManager ?? throw new ArgumentNullException(nameof(patientManager));
        _patientId = patientId ?? throw new ArgumentNullException(nameof(patientId));
        _eventChannel = Channel.CreateBounded<DiagnosticEvent>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });

        _patientManager.OutputLineReceived += OnOutputLineReceived;
    }

    /// <inheritdoc/>
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        IsConnected = true;
        DoctorDiag.Write($"[Doctor-stdio] IPC 客户端已连接，病人 {_patientId}");
        PatientConnected?.Invoke(this, _patientId);
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
        if (patientId != _patientId)
        {
            DoctorDiag.WriteError($"[Doctor-stdio] 病人 {patientId} 不匹配，期望 {_patientId}");
            return;
        }

        var stdin = _patientManager.GetStandardInput(_patientId);
        if (stdin is null || !stdin.BaseStream.CanWrite)
        {
            DoctorDiag.WriteError($"[Doctor-stdio] 病人 {_patientId} stdin 不可写，无法发送指令");
            return;
        }

        await stdin.WriteAsync(command.AsMemory(), cancellationToken).ConfigureAwait(false);
        await stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
        DoctorDiag.Write($"[Doctor-stdio] 已发送指令到病人 {_patientId}: {command[..Math.Min(command.Length, 100)]}");
    }

    /// <inheritdoc/>
    public Task BroadcastCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(_patientId, command, cancellationToken);
    }

    private void OnOutputLineReceived(object? sender, (string PatientId, string Line) e)
    {
        if (e.PatientId != _patientId) return;
        if (string.IsNullOrWhiteSpace(e.Line)) return;

        try
        {
            var evt = ParseDiagnosticEvent(e.Line, _patientId);
            if (evt is not null)
            {
                _eventChannel.Writer.TryWrite(evt);
                EventReceived?.Invoke(this, evt);
            }
        }
        catch (Exception ex)
        {
            DoctorDiag.Write($"[Doctor-stdio] 解析病人 {_patientId} stdout 行失败: {ex.Message}");
        }
    }

    internal static DiagnosticEvent? ParseDiagnosticEvent(string line, string patientId)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var eventType = DetectEventType(line);
        if (eventType is null) return null;

        return new DiagnosticEvent
        {
            EventType = eventType,
            PatientId = patientId,
            RawData = line,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

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
                var json = JsonSerializer.Deserialize(line, DoctorStdioJsonContext.Default.DictionaryStringJsonElement);
                if (json is not null && json.TryGetValue("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
                {
                    return typeEl.GetString();
                }
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[Doctor-stdio] NDJSON 解析失败: {ex.Message}"); }
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1) return;

        _patientManager.OutputLineReceived -= OnOutputLineReceived;
        _eventChannel.Writer.TryComplete();
        IsConnected = false;

        PatientDisconnected?.Invoke(this, _patientId);

        await Task.CompletedTask.ConfigureAwait(false);
    }
}

[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class DoctorStdioJsonContext : JsonSerializerContext;
