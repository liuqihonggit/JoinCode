namespace Tools.Handlers;

/// <summary>
/// Agent 参数验证中间件 — 检查 description 和 prompt 的有效性
/// </summary>
[Register(typeof(IAgentToolMiddleware), ServiceLifetime.Singleton)]
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

        // 解析 Agent(worker,researcher) 语法 — 对齐 TS 原版 resolveAgentTools allowedAgentTypes
        // SubagentType 含逗号时,提取 PrimaryType 作为实际 spawn 类型,AllowedTypes 限制可递归 spawn 的子类型
        if (!string.IsNullOrWhiteSpace(context.SubagentType))
        {
            var (primaryType, allowedTypes) = AgentTypeSpecParser.Parse(context.SubagentType);
            if (allowedTypes is not null)
            {
                context.ResolvedPrimaryType = primaryType;
                context.AllowedAgentTypes = allowedTypes;
            }
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
