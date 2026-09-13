namespace JoinCode.Abstractions.Exceptions;

/// <summary>
/// 代码执行异常
/// </summary>
public sealed class CodeExecutionException : WorkflowException
{
    /// <summary>
    /// 代码执行结果元数据
    /// </summary>
    public CodeExecutionResult? Result { get; }

    /// <inheritdoc />
    public override bool IsRetryable => Result?.ExitCode is null or 1 or 137; // 137 = SIGKILL (OOM)

    /// <inheritdoc />
    public override int? SuggestedRetryCount => IsRetryable ? 1 : null;

    /// <summary>
    /// 创建 CodeExecutionException
    /// </summary>
    public CodeExecutionException(
        string message,
        CodeExecutionResult? result = null,
        string? errorCode = null,
        ExceptionContext? context = null)
        : base(message, errorCode ?? global::JoinCode.Abstractions.Exceptions.ErrorCode.CodeExecutionGeneral.ToValue(), ErrorCategory.CodeExecution, context)
    {
        Result = result;
    }

    /// <summary>
    /// 创建 CodeExecutionException（带内部异常）
    /// </summary>
    public CodeExecutionException(
        string message,
        Exception innerException,
        CodeExecutionResult? result = null,
        string? errorCode = null,
        ExceptionContext? context = null)
        : base(message, innerException, errorCode ?? global::JoinCode.Abstractions.Exceptions.ErrorCode.CodeExecutionGeneral.ToValue(), ErrorCategory.CodeExecution, context)
    {
        Result = result;
    }
}
