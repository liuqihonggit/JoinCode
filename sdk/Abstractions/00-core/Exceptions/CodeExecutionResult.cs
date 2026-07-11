namespace JoinCode.Abstractions.Exceptions;

/// <summary>
/// 代码执行结果元数据
/// </summary>
public sealed record CodeExecutionResult(
    string? CodeSnippet = null,
    string? Language = null,
    int? ExitCode = null,
    string? StandardOutput = null,
    string? StandardError = null,
    TimeSpan? ExecutionDuration = null);
