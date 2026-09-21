namespace JoinCode.Abstractions.Models.ErrorRecovery;

public sealed class CrashSnapshot {
    /// <summary>获取崩溃快照唯一标识。</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>获取捕获时间。</summary>
    public DateTimeOffset CapturedAt { get; } = DateTimeOffset.UtcNow;

    /// <summary>获取围栏名称。</summary>
    public string FenceName { get; }

    /// <summary>获取严重程度。</summary>
    public CrashSeverity Severity { get; }

    /// <summary>获取或设置快照状态。</summary>
    public CrashSnapshotState State { get; set; }

    /// <summary>获取异常类型全名。</summary>
    public string ExceptionType { get; }

    /// <summary>获取异常消息。</summary>
    public string ExceptionMessage { get; }

    /// <summary>获取错误代码。</summary>
    public string? ErrorCode { get; }

    /// <summary>获取错误类别。</summary>
    public ErrorCategory? ErrorCategory { get; }

    /// <summary>获取堆栈跟踪。</summary>
    public string? StackTrace { get; }

    /// <summary>获取异常链。</summary>
    public CrashExceptionChain ExceptionChain { get; }

    /// <summary>获取执行上下文。</summary>
    public CrashExecutionContext ExecutionContext { get; }

    /// <summary>获取标签字典。</summary>
    public Dictionary<string, string> Tags { get; } = new(StringComparer.Ordinal);

    /// <summary>获取附件字典。</summary>
    public Dictionary<string, string> Attachments { get; } = new(StringComparer.Ordinal);

    /// <summary>构造崩溃快照。</summary>
    /// <param name="fenceName">围栏名称。</param>
    /// <param name="severity">严重程度。</param>
    /// <param name="exception">异常对象。</param>
    /// <param name="executionContext">执行上下文。</param>
    /// <param name="errorCode">错误代码。</param>
    /// <param name="errorCategory">错误类别。</param>
    public CrashSnapshot(
        string fenceName,
        CrashSeverity severity,
        Exception exception,
        CrashExecutionContext? executionContext = null,
        string? errorCode = null,
        ErrorCategory? errorCategory = null) {
        ArgumentException.ThrowIfNullOrEmpty(fenceName);
        ArgumentNullException.ThrowIfNull(exception);

        FenceName = fenceName;
        Severity = severity;
        ExceptionType = exception.GetType().FullName ?? exception.GetType().Name;
        ExceptionMessage = exception.Message;
        ErrorCode = errorCode ?? (exception is WorkflowException we ? we.ErrorCode : null);
        ErrorCategory = errorCategory ?? (exception is WorkflowException we2 ? we2.Category : null);
        StackTrace = exception.StackTrace;
        ExceptionChain = CrashExceptionChain.Build(exception);
        ExecutionContext = executionContext ?? new CrashExecutionContext();
        State = CrashSnapshotState.Captured;
    }

    /// <summary>添加标签并返回当前快照。</summary>
    /// <param name="key">标签键。</param>
    /// <param name="value">标签值。</param>
    public CrashSnapshot WithTag(string key, string value) {
        Tags[key] = value;
        return this;
    }

    /// <summary>添加附件并返回当前快照。</summary>
    /// <param name="name">附件名称。</param>
    /// <param name="content">附件内容。</param>
    public CrashSnapshot WithAttachment(string name, string content) {
        Attachments[name] = content;
        return this;
    }

    /// <summary>转换为摘要字符串。</summary>
    public string ToSummary() {
        var sb = new StringBuilder();
        sb.Append($"[{Severity.ToValue()}] {FenceName}: {ExceptionType}: {ExceptionMessage}");
        if (ErrorCode is not null)
            sb.Append($" (Code={ErrorCode})");
        if (ExecutionContext.ToolName is not null)
            sb.Append($" Tool={ExecutionContext.ToolName}");
        if (ExecutionContext.TurnIndex is not null)
            sb.Append($" Turn={ExecutionContext.TurnIndex}");
        return sb.ToString();
    }
}

public enum CrashSeverity {
    [EnumValue("WARN")] Warning,
    [EnumValue("ERROR")] Error,
    [EnumValue("FATAL")] Fatal,
}

public enum CrashSnapshotState {
    [EnumValue("captured")]
    Captured,
    [EnumValue("acknowledged")]
    Acknowledged,
    [EnumValue("resolved")]
    Resolved,
    [EnumValue("suppressed")]
    Suppressed,
}

public sealed class CrashExceptionChain {
    /// <summary>获取异常链深度。</summary>
    public int Depth { get; }
    /// <summary>获取根异常类型。</summary>
    public string RootExceptionType { get; }
    /// <summary>获取根异常消息。</summary>
    public string RootExceptionMessage { get; }
    /// <summary>获取异常帧列表。</summary>
    public ImmutableArray<CrashExceptionFrame> Frames { get; }

    private CrashExceptionChain(int depth, string rootType, string rootMessage, ImmutableArray<CrashExceptionFrame> frames) {
        Depth = depth;
        RootExceptionType = rootType;
        RootExceptionMessage = rootMessage;
        Frames = frames;
    }

    /// <summary>从异常构建异常链。</summary>
    /// <param name="exception">异常对象。</param>
    public static CrashExceptionChain Build(Exception exception) {
        var frames = ImmutableArray.CreateBuilder<CrashExceptionFrame>();
        var current = exception;
        var depth = 0;

        while (current is not null && depth < 10) {
            frames.Add(new CrashExceptionFrame(
                depth,
                current.GetType().FullName ?? current.GetType().Name,
                current.Message,
                current.StackTrace,
                current is WorkflowException we ? we.ErrorCode : null));

            current = current.InnerException;
            depth++;
        }

        var root = frames.Count > 0 ? frames[0] : null;
        return new CrashExceptionChain(
            depth,
            root?.ExceptionType ?? "Unknown",
            root?.Message ?? "",
            frames.ToImmutable());
    }
}

public sealed record CrashExceptionFrame(
    int Depth,
    string ExceptionType,
    string Message,
    string? StackTrace,
    string? ErrorCode);

public sealed class CrashExecutionContext {
    /// <summary>获取或设置操作名称。</summary>
    public string? OperationName { get; set; }
    /// <summary>获取或设置工具名称。</summary>
    public string? ToolName { get; set; }
    /// <summary>获取或设置工具组。</summary>
    public string? ToolGroup { get; set; }
    /// <summary>获取或设置轮次索引。</summary>
    public int? TurnIndex { get; set; }
    /// <summary>获取或设置请求标识。</summary>
    public string? RequestId { get; set; }
    /// <summary>获取或设置会话标识。</summary>
    public string? SessionId { get; set; }
    /// <summary>获取或设置模型标识。</summary>
    public string? ModelId { get; set; }
    /// <summary>获取额外数据字典。</summary>
    public Dictionary<string, string> Extra { get; } = new(StringComparer.Ordinal);

    /// <summary>添加额外数据并返回当前上下文。</summary>
    /// <param name="key">键。</param>
    /// <param name="value">值。</param>
    public CrashExecutionContext With(string key, string value) {
        Extra[key] = value;
        return this;
    }
}