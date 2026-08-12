namespace JoinCode.Abstractions.Models.ErrorRecovery;

public sealed class CrashSnapshot
{
    public Guid Id { get; } = Guid.NewGuid();

    public DateTimeOffset CapturedAt { get; } = DateTimeOffset.UtcNow;

    public string FenceName { get; }

    public CrashSeverity Severity { get; }

    public CrashSnapshotState State { get; set; }

    public string ExceptionType { get; }

    public string ExceptionMessage { get; }

    public string? ErrorCode { get; }

    public ErrorCategory? ErrorCategory { get; }

    public string? StackTrace { get; }

    public CrashExceptionChain ExceptionChain { get; }

    public CrashExecutionContext ExecutionContext { get; }

    public Dictionary<string, string> Tags { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> Attachments { get; } = new(StringComparer.Ordinal);

    public CrashSnapshot(
        string fenceName,
        CrashSeverity severity,
        Exception exception,
        CrashExecutionContext? executionContext = null,
        string? errorCode = null,
        ErrorCategory? errorCategory = null)
    {
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

    public CrashSnapshot WithTag(string key, string value)
    {
        Tags[key] = value;
        return this;
    }

    public CrashSnapshot WithAttachment(string name, string content)
    {
        Attachments[name] = content;
        return this;
    }

    public string ToSummary()
    {
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

public enum CrashSeverity
{
    [EnumValue("WARN")] Warning,
    [EnumValue("ERROR")] Error,
    [EnumValue("FATAL")] Fatal,
}

public enum CrashSnapshotState
{
    Captured,
    Acknowledged,
    Resolved,
    Suppressed,
}

public sealed class CrashExceptionChain
{
    public int Depth { get; }
    public string RootExceptionType { get; }
    public string RootExceptionMessage { get; }
    public ImmutableArray<CrashExceptionFrame> Frames { get; }

    private CrashExceptionChain(int depth, string rootType, string rootMessage, ImmutableArray<CrashExceptionFrame> frames)
    {
        Depth = depth;
        RootExceptionType = rootType;
        RootExceptionMessage = rootMessage;
        Frames = frames;
    }

    public static CrashExceptionChain Build(Exception exception)
    {
        var frames = ImmutableArray.CreateBuilder<CrashExceptionFrame>();
        var current = exception;
        var depth = 0;

        while (current is not null && depth < 10)
        {
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

public sealed class CrashExecutionContext
{
    public string? OperationName { get; set; }
    public string? ToolName { get; set; }
    public string? ToolGroup { get; set; }
    public int? TurnIndex { get; set; }
    public string? RequestId { get; set; }
    public string? SessionId { get; set; }
    public string? ModelId { get; set; }
    public Dictionary<string, string> Extra { get; } = new(StringComparer.Ordinal);

    public CrashExecutionContext With(string key, string value)
    {
        Extra[key] = value;
        return this;
    }
}
