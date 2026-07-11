namespace JoinCode.Abstractions.Exceptions;

public class WorkflowException : Exception
{
    /// <summary>
    /// 错误码
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// 错误类别
    /// </summary>
    public ErrorCategory Category { get; }

    /// <summary>
    /// 异常上下文
    /// </summary>
    public ExceptionContext Context { get; }

    /// <summary>
    /// 是否可重试
    /// </summary>
    public virtual bool IsRetryable => false;

    /// <summary>
    /// 建议的重试次数（仅在 IsRetryable 为 true 时有效）
    /// </summary>
    public virtual int? SuggestedRetryCount => null;

    /// <summary>
    /// 创建 WorkflowException
    /// </summary>
    public WorkflowException(
        string message,
        string? errorCode = null,
        ErrorCategory category = ErrorCategory.Workflow,
        ExceptionContext? context = null)
        : base(message)
    {
        ErrorCode = errorCode ?? global::JoinCode.Abstractions.Exceptions.ErrorCode.WorkflowGeneral.ToValue();
        Category = category;
        Context = context ?? new ExceptionContext();
    }

    /// <summary>
    /// 创建 WorkflowException（带内部异常）
    /// </summary>
    public WorkflowException(
        string message,
        Exception innerException,
        string? errorCode = null,
        ErrorCategory category = ErrorCategory.Workflow,
        ExceptionContext? context = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode ?? global::JoinCode.Abstractions.Exceptions.ErrorCode.WorkflowGeneral.ToValue();
        Category = category;
        Context = context ?? new ExceptionContext();
    }

    public WorkflowException WithContext(Action<ExceptionContext> configure)
    {
        configure(Context);
        return this;
    }

    /// <summary>
    /// 添加请求ID
    /// </summary>
    public WorkflowException WithRequestId(string requestId)
    {
        Context.RequestId = requestId;
        return this;
    }

    /// <summary>
    /// 添加操作名称
    /// </summary>
    public WorkflowException WithOperation(string operationName)
    {
        Context.OperationName = operationName;
        return this;
    }
}

/// <summary>
/// 错误类别
/// </summary>
public enum ErrorCategory
{
    Workflow,
    Configuration,
    Api,
    CodeExecution,
    Permission,
    Security,
    Mcp,
    Validation,
    Resource,
    Scheduling,
    General
}
