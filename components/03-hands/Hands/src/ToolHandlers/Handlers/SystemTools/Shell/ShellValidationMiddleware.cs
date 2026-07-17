namespace Tools.Shell;

/// <summary>
/// Shell 命令参数验证中间件 — 检查命令、超时、工作目录等参数的有效性
/// </summary>
[Register]
public sealed partial class ShellValidationMiddleware : IShellMiddleware
{
    /// <inheritdoc />

    /// <inheritdoc />

    /// <summary>
    /// 创建 ShellValidationMiddleware
    /// </summary>
    public ShellValidationMiddleware() { }

    /// <inheritdoc />
    public Task InvokeAsync(ShellPipelineContext context, MiddlewareDelegate<ShellPipelineContext> next, CancellationToken ct)
    {
        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(context.Command, "command"),
            ValidationHelper.ValidateStringLength(context.Command, 8192, "command"),
            ValidationHelper.ValidateRange(context.Timeout, 1000, 600000, "timeout"),
            ValidationHelper.ValidateStringLength(context.WorkingDirectory, 4096, "working_directory"));

        if (validationError != null)
        {
            context.ValidationError = validationError;
            context.Result = ResultBuilder.Error().WithText(validationError).Build();
            return Task.CompletedTask; // 短路
        }

        return next(context, ct);
    }
}
