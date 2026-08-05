using JoinCode.Abstractions.Attributes;

namespace Core.Context;

/// <summary>
/// 链路日志记录中间件 — 记录工具调用、API 调用、循环检测、异常事件到 JSONL 文件
/// 写入位置: .jcc/diag/{sessionId}/{timestamp}.jsonl
/// OnError=Continue：日志记录失败不影响管道继续执行
/// </summary>
[Register]
public sealed partial class DiagnosticLogRecorder : ServiceEntity, IChatMiddleware
{
    [Inject] private readonly IFileSystem _fs;
    [Inject] private readonly ILogger<DiagnosticLogRecorder>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async IAsyncEnumerable<ChatStreamEvent> InvokeAsync(
        ChatMiddlewareContext context,
        StreamMiddlewareDelegate<ChatMiddlewareContext, ChatStreamEvent> next,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var sessionId = context.SpanName;
        var logPath = BuildLogPath(sessionId);

        await EnsureDiagDirectoryAsync(logPath).ConfigureAwait(false);

        var entryWriter = new DiagnosticEntryWriter(_fs, logPath, _logger);

        await entryWriter.WriteEntryAsync(new DiagnosticLogEntry
        {
            EventType = "turn_start",
            Timestamp = DateTimeOffset.UtcNow,
            SessionId = sessionId,
            Data = new Dictionary<string, string>
            {
                ["message_length"] = context.Message.Length.ToString(),
                ["conversation_turn"] = context.ConversationTurn.ToString(),
            }
        }, ct).ConfigureAwait(false);

        await foreach (var evt in next(context, ct).ConfigureAwait(false))
        {
            var entry = MapEventToEntry(evt, sessionId);
            if (entry is not null)
            {
                await entryWriter.WriteEntryAsync(entry, ct).ConfigureAwait(false);
            }

            yield return evt;
        }

        await entryWriter.WriteEntryAsync(new DiagnosticLogEntry
        {
            EventType = "turn_end",
            Timestamp = DateTimeOffset.UtcNow,
            SessionId = sessionId,
            Data = new Dictionary<string, string>
            {
                ["total_tool_calls"] = context.TotalToolCalls.ToString(),
                ["loop_trigger_count"] = context.LoopTriggerCount.ToString(),
                ["total_ms"] = context.Timing.TotalMs.ToString(),
            }
        }, ct).ConfigureAwait(false);
    }

    private static DiagnosticLogEntry? MapEventToEntry(ChatStreamEvent evt, string sessionId)
    {
        return evt.Type switch
        {
            ChatStreamEventType.ToolCallStart => new DiagnosticLogEntry
            {
                EventType = "tool_start",
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = sessionId,
                Data = new Dictionary<string, string>
                {
                    ["tool_name"] = evt.ToolName ?? "",
                    ["tool_call_id"] = evt.ToolCallId ?? "",
                }
            },
            ChatStreamEventType.ToolCallEnd => new DiagnosticLogEntry
            {
                EventType = evt.IsToolError ? "tool_error" : "tool_end",
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = sessionId,
                IsAnomaly = evt.IsToolError,
                Data = new Dictionary<string, string>
                {
                    ["tool_name"] = evt.ToolName ?? "",
                    ["tool_call_id"] = evt.ToolCallId ?? "",
                    ["is_error"] = evt.IsToolError.ToString(),
                }
            },
            ChatStreamEventType.LoopDetected => new DiagnosticLogEntry
            {
                EventType = "loop_detected",
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = sessionId,
                IsAnomaly = true,
                Data = new Dictionary<string, string>
                {
                    ["trigger_count"] = evt.LoopTriggerCount.ToString(),
                    ["loop_start_index"] = evt.LoopStartIndex.ToString(),
                    ["repeated_pattern"] = evt.Content ?? "",
                }
            },
            ChatStreamEventType.Complete => new DiagnosticLogEntry
            {
                EventType = "api_complete",
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = sessionId,
                Data = new Dictionary<string, string>
                {
                    ["model_id"] = evt.ModelId ?? "",
                    ["input_tokens"] = evt.Usage?.PromptTokens.ToString() ?? "0",
                    ["output_tokens"] = evt.Usage?.CompletionTokens.ToString() ?? "0",
                    ["cache_creation"] = evt.Usage?.CacheCreationInputTokens.ToString() ?? "0",
                    ["cache_read"] = evt.Usage?.CacheReadInputTokens.ToString() ?? "0",
                }
            },
            _ => null
        };
    }

    private static string BuildLogPath(string sessionId)
    {
        var homeDir = Environment.GetEnvironmentVariable("USERPROFILE")
            ?? Environment.GetEnvironmentVariable("HOME")
            ?? AppContext.BaseDirectory;
        return Path.Combine(homeDir, ".jcc", "diag", sessionId, $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.jsonl");
    }

    private async Task EnsureDiagDirectoryAsync(string logPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(logPath);
            if (dir is not null && !_fs.DirectoryExists(dir))
            {
                _fs.CreateDirectory(dir);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[DiagnosticLogRecorder] 无法创建诊断目录: {Path}", logPath);
        }
    }
}

/// <summary>
/// 诊断日志条目 — JSONL 文件中的一行
/// </summary>
public sealed record DiagnosticLogEntry
{
    public required string EventType { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string SessionId { get; init; }

    /// <summary>
    /// 追踪ID — 每条日志唯一标识，用于构建追踪链
    /// </summary>
    public string TraceId { get; init; } = Guid.NewGuid().ToString("N")[..12];

    public bool IsAnomaly { get; init; }
    public Dictionary<string, string> Data { get; init; } = new();
}

/// <summary>
/// 诊断条目写入器 — 负责将 DiagnosticLogEntry 序列化并追加到 JSONL 文件
/// </summary>
internal sealed class DiagnosticEntryWriter
{
    private readonly IFileSystem _fs;
    private readonly string _logPath;
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public DiagnosticEntryWriter(IFileSystem fs, string logPath, ILogger? logger)
    {
        _fs = fs;
        _logPath = logPath;
        _logger = logger;
    }

    public async Task WriteEntryAsync(DiagnosticLogEntry entry, CancellationToken ct)
    {
        try
        {
            var anomalyFlag = entry.IsAnomaly ? ",\"anomaly\":true" : "";
            var dataProps = string.Join(",", entry.Data.Select(kv => $"\"{kv.Key}\":\"{EscapeJsonString(kv.Value)}\""));
            var line = $"{{\"ts\":\"{entry.Timestamp:O}\",\"event\":\"{entry.EventType}\",\"session\":\"{entry.SessionId}\",\"trace\":\"{entry.TraceId}\"{anomalyFlag},\"data\":{{{dataProps}}}}}";

            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _fs.AppendAllTextAsync(_logPath, line + "\n", ct).ConfigureAwait(false);
            }
            finally
            {
                _lock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[DiagnosticLogRecorder] 写入诊断日志失败: {Path}", _logPath);
        }
    }

    private static string EscapeJsonString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}
