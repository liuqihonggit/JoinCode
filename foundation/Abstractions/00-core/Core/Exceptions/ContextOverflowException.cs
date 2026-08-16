namespace JoinCode.Abstractions.Exceptions;

/// <summary>
/// 上下文溢出异常 — L5 兜底报错
/// <para>当 L4 历史压缩后仍超过 EmergencyThreshold 时抛出，对齐 openCode ContextOverflowError。</para>
/// <para>不可重试 — 上下文已无法容纳，必须停止并要求用户处理。</para>
/// </summary>
public sealed class ContextOverflowException : WorkflowException
{
    /// <summary>
    /// 上下文窗口最大 token 数
    /// </summary>
    public int ContextMaxTokens { get; }

    /// <summary>
    /// 当前已用 token 数（折叠后）
    /// </summary>
    public int CurrentTokens { get; }

    /// <inheritdoc />
    public override bool IsRetryable => false;

    /// <summary>
    /// 创建 ContextOverflowException
    /// </summary>
    public ContextOverflowException(
        string message,
        int contextMaxTokens,
        int currentTokens,
        ExceptionContext? context = null)
        : base(message, errorCode: global::JoinCode.Abstractions.Exceptions.ErrorCode.ContextOverflow.ToValue(), ErrorCategory.Resource, context)
    {
        ContextMaxTokens = contextMaxTokens;
        CurrentTokens = currentTokens;
    }

    /// <summary>
    /// 创建 ContextOverflowException（带内部异常）
    /// </summary>
    public ContextOverflowException(
        string message,
        Exception innerException,
        int contextMaxTokens,
        int currentTokens,
        ExceptionContext? context = null)
        : base(message, innerException, errorCode: global::JoinCode.Abstractions.Exceptions.ErrorCode.ContextOverflow.ToValue(), ErrorCategory.Resource, context)
    {
        ContextMaxTokens = contextMaxTokens;
        CurrentTokens = currentTokens;
    }
}
