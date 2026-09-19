namespace Core.Context;

/// <summary>
/// 链路日志记录中间件 — 记录工具调用、API 调用、循环检测、异常事件到 JSONL 文件
/// 写入位置: ~/.jcc/sessions/{sessionId}/diag/{timestamp}.json
/// OnError=Continue：日志记录失败不影响管道继续执行
/// </summary>
[Register(typeof(IChatMiddleware), ServiceLifetime.Singleton)]
public sealed partial class DiagnosticLogRecorder : ServiceEntity, IChatMiddleware {

    /// <summary>
    /// 初始化诊断日志记录中间件
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="logger">可选日志记录器</param>
    public DiagnosticLogRecorder(IFileSystem fs, ILogger<DiagnosticLogRecorder>? logger = null) {
        _fs = fs;
        _logger = logger;
    }
    private readonly IFileSystem _fs;
    private readonly ILogger<DiagnosticLogRecorder>? _logger;

    /// <summary>错误行为策略：继续执行后续中间件</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 记录管道各阶段事件到 JSONL 诊断日志文件
    /// </summary>
    /// <param name="context">中间件共享上下文</param>
    /// <param name="next">下游中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>聊天流事件异步枚举</returns>
    public async IAsyncEnumerable<ChatStreamEvent> InvokeAsync(
        ChatMiddlewareContext context,
        StreamMiddlewareDelegate<ChatMiddlewareContext, ChatStreamEvent> next,
        [EnumeratorCancellation] CancellationToken ct) {
        var sessionId = context.SessionId;
        var logPath = BuildLogPath(sessionId);

        await EnsureDiagDirectoryAsync(logPath).ConfigureAwait(false);

        var entryWriter = new DiagnosticEntryWriter(_fs, logPath, _logger);

        await using (entryWriter) {
            await entryWriter.WriteEntryAsync(new DiagnosticLogEntry {
                EventType = "turn_start",
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = sessionId,
                Data = new Dictionary<string, string> {
                    ["message_length"] = context.Message.Length.ToString(),
                    ["conversation_turn"] = context.ConversationTurn.ToString(),
                }
            }, ct).ConfigureAwait(false);

            await foreach (var evt in next(context, ct).ConfigureAwait(false)) {
                var entry = MapEventToEntry(evt, sessionId);
                if (entry is not null) {
                    await entryWriter.WriteEntryAsync(entry, ct).ConfigureAwait(false);
                }

                yield return evt;
            }

            await entryWriter.WriteEntryAsync(new DiagnosticLogEntry {
                EventType = "turn_end",
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = sessionId,
                Data = new Dictionary<string, string> {
                    ["total_tool_calls"] = context.TotalToolCalls.ToString(),
                    ["loop_trigger_count"] = context.LoopTriggerCount.ToString(),
                    ["total_ms"] = context.Timing.TotalMs.ToString(),
                }
            }, ct).ConfigureAwait(false);
        }
    }

    private static DiagnosticLogEntry? MapEventToEntry(ChatStreamEvent evt, string sessionId) {
        return evt.Type switch {
            ChatStreamEventType.ToolCallStart => new DiagnosticLogEntry {
                EventType = "tool_start",
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = sessionId,
                Data = new Dictionary<string, string> {
                    ["tool_name"] = evt.ToolName ?? "",
                    ["tool_call_id"] = evt.ToolCallId ?? "",
                }
            },
            ChatStreamEventType.ToolCallEnd => new DiagnosticLogEntry {
                EventType = evt.IsToolError ? "tool_error" : "tool_end",
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = sessionId,
                IsAnomaly = evt.IsToolError,
                Data = new Dictionary<string, string> {
                    ["tool_name"] = evt.ToolName ?? "",
                    ["tool_call_id"] = evt.ToolCallId ?? "",
                    ["is_error"] = evt.IsToolError.ToString(),
                }
            },
            ChatStreamEventType.LoopDetected => new DiagnosticLogEntry {
                EventType = "loop_detected",
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = sessionId,
                IsAnomaly = true,
                Data = new Dictionary<string, string> {
                    ["trigger_count"] = evt.LoopTriggerCount.ToString(),
                    ["loop_start_index"] = evt.LoopStartIndex.ToString(),
                    ["repeated_pattern"] = evt.Content ?? "",
                }
            },
            ChatStreamEventType.Complete => new DiagnosticLogEntry {
                EventType = "api_complete",
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = sessionId,
                Data = new Dictionary<string, string> {
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

    private static string BuildLogPath(string sessionId) {
        return Path.Combine(AppDataConstants.Paths.SessionsDirectory, sessionId, "diag", $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.json");
    }

    private async Task EnsureDiagDirectoryAsync(string logPath) {
        try {
            var dir = Path.GetDirectoryName(logPath);
            if (dir is not null && !_fs.DirectoryExists(dir)) {
                _fs.CreateDirectory(dir);
            }
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "[DiagnosticLogRecorder] 无法创建诊断目录: {Path}", logPath);
        }
    }
}

/// <summary>
/// 诊断日志条目 — JSONL 文件中的一行
/// </summary>
public sealed record DiagnosticLogEntry {
    /// <summary>事件类型标识（如 turn_start、tool_start、tool_end、loop_detected 等）</summary>
    public required string EventType { get; init; }
    /// <summary>时间戳</summary>
    public required DateTimeOffset Timestamp { get; init; }
    /// <summary>会话标识</summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// 追踪ID — 每条日志唯一标识，用于构建追踪链
    /// </summary>
    public string TraceId { get; init; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>是否为异常事件（如工具错误、循环检测触发）</summary>
    public bool IsAnomaly { get; init; }
    /// <summary>附加事件数据键值对</summary>
    public Dictionary<string, string> Data { get; init; } = new();
}

/// <summary>
/// 诊断条目写入器 — 负责将 DiagnosticLogEntry 序列化并追加到 JSONL 文件
/// </summary>
internal sealed class DiagnosticEntryWriter : IAsyncDisposable {
    private readonly IFileSystem _fs;
    private readonly string _logPath;
    private readonly ILogger? _logger;
    private readonly WriteEntryActor _actor;

    public DiagnosticEntryWriter(IFileSystem fs, string logPath, ILogger? logger) {
        _fs = fs;
        _logPath = logPath;
        _logger = logger;
        _actor = new WriteEntryActor(fs, logPath, logger);
    }

    public async Task WriteEntryAsync(DiagnosticLogEntry entry, CancellationToken ct) {
        var anomalyFlag = entry.IsAnomaly ? ",\"anomaly\":true" : "";
        var dataProps = string.Join(",", entry.Data.Select(kv => $"\"{kv.Key}\":\"{EscapeJsonString(kv.Value)}\""));
        var line = $"{{\"ts\":\"{entry.Timestamp:O}\",\"event\":\"{entry.EventType}\",\"session\":\"{entry.SessionId}\",\"trace\":\"{entry.TraceId}\"{anomalyFlag},\"data\":{{{dataProps}}}}}";

        var reply = new TaskCompletionSource();
        await _actor.SendAsync(new WriteEntryCmd(line, reply), ct).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply, ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
        => await _actor.DisposeAsync().ConfigureAwait(false);

    private static string EscapeJsonString(string value) {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    private sealed record WriteEntryCmd(string Line, TaskCompletionSource Reply);

    private sealed class WriteEntryActor : ActorBase<WriteEntryCmd, Unit> {
        private readonly IFileSystem _fs;
        private readonly string _logPath;
        private readonly ILogger? _logger;

        public WriteEntryActor(IFileSystem fs, string logPath, ILogger? logger) : base() {
            _fs = fs;
            _logPath = logPath;
            _logger = logger;
        }

        public async Task AskReplyAsync(TaskCompletionSource tcs, CancellationToken ct = default)
            => await base.AskAwait(tcs, ct).ConfigureAwait(false);

        protected override async ValueTask HandleAsync(WriteEntryCmd cmd, CancellationToken ct) {
            try {
                await _fs.AppendAllTextAsync(_logPath, cmd.Line + "\n", ct).ConfigureAwait(false);
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "[DiagnosticLogRecorder] 写入诊断日志失败: {Path}", _logPath);
            }
            cmd.Reply.SetResult();
        }
    }
}