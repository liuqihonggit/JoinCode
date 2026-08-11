namespace Tools.Handlers;

/// <summary>
/// Agent 参数验证中间件 — 检查 description 和 prompt 的有效性
/// </summary>
[Register]
public sealed partial class AgentValidationMiddleware : ServiceEntity, IAgentToolMiddleware
{
    /// <inheritdoc />
    public int Order => 100;

    /// <inheritdoc />

    /// <summary>
    /// 创建 AgentValidationMiddleware
    /// </summary>
    public AgentValidationMiddleware() { }

    /// <inheritdoc />
    public Task InvokeAsync(AgentToolContext context, MiddlewareDelegate<AgentToolContext> next, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(context.Description))
        {
            context.ValidationError = "description cannot be empty";
            var diagnostic = BuildEmptyDescriptionDiagnostic();
            context.Result = ToolResultBuilder.Error()
                .WithText(diagnostic.FormattedMessage)
                .WithDiagnostic(diagnostic)
                .Build();
            return Task.CompletedTask; // 短路
        }

        if (string.IsNullOrWhiteSpace(context.Prompt))
        {
            context.ValidationError = "prompt cannot be empty";
            var diagnostic = BuildEmptyPromptDiagnostic();
            context.Result = ToolResultBuilder.Error()
                .WithText(diagnostic.FormattedMessage)
                .WithDiagnostic(diagnostic)
                .Build();
            return Task.CompletedTask; // 短路
        }

        return next(context, ct);
    }

    internal static ToolDiagnostic BuildEmptyDescriptionDiagnostic() =>
        ToolDiagnostic.Create(
            reason: "参数验证失败",
            formattedMessage: "description cannot be empty",
            details: [new DiagnosticDetail("field", "description")],
            suggestions: ["提供非空的 description 参数"]);

    internal static ToolDiagnostic BuildEmptyPromptDiagnostic() =>
        ToolDiagnostic.Create(
            reason: "参数验证失败",
            formattedMessage: "prompt cannot be empty",
            details: [new DiagnosticDetail("field", "prompt")],
            suggestions: ["提供非空的 prompt 参数"]);
}
