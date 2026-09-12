
namespace Core.Security.Interceptors;

public sealed class SecurityBlockException : WorkflowException
{
    public required IReadOnlyList<SecretFinding> Findings { get; init; }

    public SecurityBlockException(string message)
        : base(message, "SEC_BLOCK", ErrorCategory.Security)
    {
    }
}
