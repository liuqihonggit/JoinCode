
namespace Core.Security.Interceptors;

/// <summary>
/// 安全阻断异常 — 当检测到密钥泄漏等安全风险时抛出,携带所有发现项
/// </summary>
public sealed class SecurityBlockException : WorkflowException
{
    /// <summary>
    /// 安全发现项列表
    /// </summary>
    public required IReadOnlyList<SecretFinding> Findings { get; init; }

    /// <summary>
    /// 初始化安全阻断异常实例
    /// </summary>
    public SecurityBlockException(string message)
        : base(message, "SEC_BLOCK", ErrorCategory.Security)
    {
    }
}
