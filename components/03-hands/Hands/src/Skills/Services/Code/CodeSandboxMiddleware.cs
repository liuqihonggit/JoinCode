namespace Core.Skills;

/// <summary>
/// 代码沙箱执行中间件 — Execute 操作的沙箱执行
/// </summary>
[Register]
public sealed partial class CodeSandboxMiddleware : ICodeMiddleware
{
    private readonly ICodeSandboxService _sandboxService;
    private readonly WorkflowConfig _config;

    /// <inheritdoc />

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    /// <summary>
    /// 创建 CodeSandboxMiddleware
    /// </summary>
    public CodeSandboxMiddleware(ICodeSandboxService sandboxService, IOptions<WorkflowConfig> configOptions)
    {
        _sandboxService = sandboxService;
        _config = configOptions.Value;
    }

    /// <inheritdoc />
    public async Task InvokeAsync(CodeContext context, MiddlewareDelegate<CodeContext> next, CancellationToken ct)
    {
        // 仅 Execute 操作使用沙箱
        if (context.Operation != CodeOperation.Execute)
        {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        var timeoutMs = _config.CodeExecution.ExecutionTimeoutSeconds * 1000;
        var result = await _sandboxService.ExecuteAsync(context.Input, timeoutMs, ct).ConfigureAwait(false);
        context.Result = L.T(StringKey.CodeServiceExecutionResult, result);

        await next(context, ct).ConfigureAwait(false);
    }
}
