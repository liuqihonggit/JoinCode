namespace JoinCode.Cli.Output;

/// <summary>
/// CLI 结构化错误模型 — 对齐架构指南4字段规范
/// </summary>
public sealed class CliStructuredError
{
    /// <summary>机器可读错误码（如 AUTH_API_KEY_MISSING、CONFIG_INVALID_MODEL）</summary>
    public string Code { get; init; }

    /// <summary>人类可读错误描述</summary>
    public string Message { get; init; }

    /// <summary>修复建议（可选，如 "请运行 jcc --doctor 检查配置"）</summary>
    public string? Hint { get; init; }

    /// <summary>是否可重试（如网络超时=true，认证失败=false）</summary>
    public bool Retryable { get; init; }

    public CliStructuredError(string code, string message, string? hint = null, bool retryable = false)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Message = message ?? throw new ArgumentNullException(nameof(message));
        Hint = hint;
        Retryable = retryable;
    }
}
