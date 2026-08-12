namespace Core.Agents.Doctor;

using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.Interfaces.Doctor;

/// <summary>
/// 诊断日志文件监控器 — 定期扫描 .jcc/diag/ 目录，将新日志行转为 DiagnosticEvent
/// 作为 IPC 遥测的补充数据源，当 IPC 断连时仍可从日志文件恢复诊断信息
/// </summary>
public sealed class DiagnosticLogWatcher : IAsyncDisposable
{
    private readonly IFileSystem _fs;
    private readonly DiagnosticEngine _diagnosticEngine;
    private readonly string _diagDirectory;
    private readonly TimeSpan _pollInterval;
    private readonly Dictionary<string, long> _filePositions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Timer? _pollTimer;
    private int _isStarted;
    private int _isDisposed;

    public event EventHandler<DiagnosticEvent>? EventDetected;

    public DiagnosticLogWatcher(
        IFileSystem fs,
        DiagnosticEngine diagnosticEngine,
        string? diagDirectory = null,
        TimeSpan? pollInterval = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _diagnosticEngine = diagnosticEngine ?? throw new ArgumentNullException(nameof(diagnosticEngine));
        _diagDirectory = diagDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jcc", "diag");
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(5);
    }

    public void Start()
    {
        if (_isDisposed == 1)
            throw new ObjectDisposedException(nameof(DiagnosticLogWatcher));

        if (Interlocked.Exchange(ref _isStarted, 1) == 1)
            return;

        _pollTimer = new Timer(
            callback: async _ => await PollOnceAsync().ConfigureAwait(false),
            state: null,
            dueTime: _pollInterval,
            period: _pollInterval);

        DoctorDiag.Write($"[LogWatcher] 开始监控: {_diagDirectory}");
    }

    public void Stop()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
        Interlocked.Exchange(ref _isStarted, 0);
        DoctorDiag.Write("[LogWatcher] 停止监控");
    }

    internal async Task PollOnceAsync()
    {
        if (!_fs.DirectoryExists(_diagDirectory))
            return;

        var files = _fs.GetFiles(_diagDirectory, "*.log", SearchOption.TopDirectoryOnly);
        if (files is null || files.Length == 0)
            return;

        foreach (var file in files)
        {
            try
            {
                await ProcessFileAsync(file).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                DoctorDiag.WriteError($"[LogWatcher] 处理文件 {file} 失败: {ex.Message}");
            }
        }
    }

    internal async Task ProcessFileAsync(string filePath)
    {
        if (!_fs.FileExists(filePath))
            return;

        var lastPosition = _filePositions.GetValueOrDefault(filePath, 0);
        var fileSize = _fs.GetFileLength(filePath);

        if (fileSize <= lastPosition)
            return;

        var allContent = await _fs.ReadAllTextAsync(filePath).ConfigureAwait(false);
        _filePositions[filePath] = fileSize;

        var allLines = allContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var newLines = lastPosition == 0
            ? allLines
            : SkipAlreadyProcessedLines(allLines, lastPosition);

        foreach (var line in newLines)
        {
            var evt = ParseLogLine(line, filePath);
            if (evt is null) continue;

            _diagnosticEngine.Evaluate(evt);
            EventDetected?.Invoke(this, evt);
        }
    }

    private static string[] SkipAlreadyProcessedLines(string[] lines, long lastPosition)
    {
        var estimatedLineLength = 128L;
        var estimatedProcessedLines = (int)(lastPosition / estimatedLineLength);
        var startIndex = Math.Max(0, estimatedProcessedLines - 1);

        for (var i = startIndex; i < lines.Length; i++)
        {
            var posBefore = 0L;
            for (var j = 0; j < i; j++)
                posBefore += lines[j].Length + 1;

            if (posBefore >= lastPosition)
                return lines[i..];
        }

        return lines;
    }

    internal static DiagnosticEvent? ParseLogLine(string line, string sourceFile)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var eventType = DetectEventType(line);
        if (eventType is null)
            return null;

        return new DiagnosticEvent
        {
            EventType = eventType,
            PatientId = Path.GetFileNameWithoutExtension(sourceFile),
            RawData = line,
            Timestamp = ExtractTimestamp(line) ?? DateTimeOffset.UtcNow,
            Properties = new Dictionary<string, string>
            {
                ["source"] = "log_file",
                ["file"] = sourceFile
            }
        };
    }

    private static string? DetectEventType(string line)
    {
        if (line.Contains("[LOOP]", StringComparison.OrdinalIgnoreCase))
            return "loop_detected";
        if (line.Contains("[PERM_DENIED]", StringComparison.OrdinalIgnoreCase) || line.Contains("PermissionDenied", StringComparison.OrdinalIgnoreCase))
            return "permission_denied";
        if (line.Contains("[API_ERROR]", StringComparison.OrdinalIgnoreCase) || line.Contains("ApiError", StringComparison.OrdinalIgnoreCase))
            return "api_error";
        if (line.Contains("[TOOL_ERROR]", StringComparison.OrdinalIgnoreCase) || line.Contains("ToolError", StringComparison.OrdinalIgnoreCase))
            return "tool_error";
        if (line.Contains("[HUNG]", StringComparison.OrdinalIgnoreCase) || line.Contains($"ExitCode={(int)ExitCode.AwaitTimeout}", StringComparison.OrdinalIgnoreCase))
            return "process_hung";
        if (line.Contains("[CTX_OVERFLOW]", StringComparison.OrdinalIgnoreCase) || line.Contains("ContextOverflow", StringComparison.OrdinalIgnoreCase))
            return "context_overflow";
        if (line.Contains("[WIRE]", StringComparison.OrdinalIgnoreCase) || line.Contains("[STEP]", StringComparison.OrdinalIgnoreCase))
            return "diag_output";

        return null;
    }

    private static DateTimeOffset? ExtractTimestamp(string line)
    {
        if (line.Length < 2 || line[0] != '[') return null;

        var bracketEnd = line.IndexOf(']', 1);
        if (bracketEnd <= 1) return null;

        var bracketContent = line[1..bracketEnd];

        if (DateTimeOffset.TryParseExact(bracketContent, "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var ts))
            return ts;

        if (DateTimeOffset.TryParseExact(bracketContent, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var ts2))
            return ts2;

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1) return;
        Stop();
        await _lock.WaitAsync().ConfigureAwait(false);
        try { _filePositions.Clear(); }
        finally { _lock.Release(); }
    }
}
