namespace Tools.Handlers;

/// <summary>
/// Agent 参数验证中间件 — 检查 description 和 prompt 的有效性
/// </summary>
[Register]
public sealed partial class AgentValidationMiddleware : IAgentToolMiddleware
{
    /// <inheritdoc />
    public int Order => 100;

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

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
            context.Result = ToolResultBuilder.Error()
                .WithText("description cannot be empty")
                .Build();
            return Task.CompletedTask; // 短路
        }

        if (string.IsNullOrWhiteSpace(context.Prompt))
        {
            context.ValidationError = "prompt cannot be empty";
            context.Result = ToolResultBuilder.Error()
                .WithText("prompt cannot be empty")
                .Build();
            return Task.CompletedTask; // 短路
        }

        return next(context, ct);
    }
}
