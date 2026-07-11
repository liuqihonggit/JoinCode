namespace Tools.Handlers;

/// <summary>
/// Agent Fork 判断中间件 — 当 subagent_type 为空且 ForkManager 可用时，走 fork 路径
/// 对齐 TS: 省略 subagent_type 时 fork 自己，继承完整对话上下文
/// </summary>
[Register]
public sealed partial class AgentForkMiddleware : IAgentToolMiddleware
{
    [Inject] private readonly IForkSubAgentManager? _forkManager;
    [Inject] private readonly ITelemetryService? _telemetryService;
    [Inject] private readonly ISubAgentContextAccessor _subAgentContextAccessor;

    /// <inheritdoc />
    public int Order => 200;

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    /// <inheritdoc />
    public async Task InvokeAsync(AgentToolContext context, MiddlewareDelegate<AgentToolContext> next, CancellationToken ct)
    {
        var isForkPath = string.IsNullOrEmpty(context.SubagentType) && _forkManager is not null;

        if (!isForkPath)
        {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        // 执行 fork 路径
        var sessionId = _subAgentContextAccessor.Current?.SessionId ?? "default";
        var parentCacheSafeParams = _subAgentContextAccessor.Current?.CacheSafeParams;

        var forkOptions = new ForkOptions
        {
            ParentSessionId = sessionId,
            TaskDescription = context.Prompt,
            ShareCache = true,
            ShareContext = true,
            UseExactTools = true,
            RunInBackground = true,
            PermissionMode = PermissionMode.Plan,
            MaxIterations = 200,
            CacheSafeParams = parentCacheSafeParams
        };

        var result = await _forkManager!.ForkAsync(forkOptions, ct).ConfigureAwait(false);

        var response = new StringBuilder();
        response.AppendLine("Fork sub-agent launched");
        response.AppendLine($"ForkID: {result.ForkId}");
        response.AppendLine($"Instructions: {context.Prompt}");
        response.AppendLine();
        response.AppendLine("Status: async_launched");
        response.AppendLine("Fork sub-agent is running in the background. You will be notified when it completes.");
        response.AppendLine("Fork inherits the parent's full context and tool pool, sharing Prompt Cache.");

        RecordAgentMetrics("fork", true);
        context.ForkResult = ToolResultBuilder.Success()
            .WithText(response.ToString())
            .Build();
        context.Result = context.ForkResult;
        // 短路 — fork 路径不需要后续中间件
    }

    private void RecordAgentMetrics(string operation, bool isSuccess)
        => _telemetryService?.RecordCount("agent.handler.count", new Dictionary<string, string> { ["operation"] = operation, ["success"] = isSuccess.ToString() }, description: "Agent handler count");
}
