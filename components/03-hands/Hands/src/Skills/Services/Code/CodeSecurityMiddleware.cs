namespace Core.Skills;

/// <summary>
/// 代码安全验证中间件 — Execute 操作的安全检查
/// </summary>
[Register]
public sealed partial class CodeSecurityMiddleware : ICodeMiddleware
{
    [Inject] private readonly ICodeSecurityValidator _securityValidator;

    /// <inheritdoc />

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc />
    public Task InvokeAsync(CodeContext context, MiddlewareDelegate<CodeContext> next, CancellationToken ct)
    {
        // 仅 Execute 操作需要安全验证
        if (context.Operation != CodeOperation.Execute)
        {
            return next(context, ct);
        }

        // 代码长度检查
        if (string.IsNullOrWhiteSpace(context.Input))
        {
            context.Result = L.T(StringKey.CodeServiceCodeCannotBeEmpty);
            return Task.CompletedTask; // 短路
        }

        if (context.Input.Length > WorkflowConstants.Limits.CodeLengthMax)
        {
            context.Result = L.T(StringKey.CodeServiceCodeLengthExceeded, WorkflowConstants.Limits.CodeLengthMax);
            return Task.CompletedTask; // 短路
        }

        // 安全验证
        var securityResult = _securityValidator.Validate(context.Input, allowExternalLibs: false);
        if (!securityResult.IsValid)
        {
            context.IsSecurityFail = true;
            context.Result = L.T(StringKey.CodeServiceCodeValidationError, securityResult.Message);
            return Task.CompletedTask; // 短路
        }

        return next(context, ct);
    }
}
