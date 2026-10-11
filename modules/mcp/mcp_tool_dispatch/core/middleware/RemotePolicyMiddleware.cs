
namespace McpToolRegistry;

/// <summary>
/// 远程策略检查中间件 — Order=600 — 检查远程策略是否允许工具执行
/// </summary>
[Register(typeof(IToolExecutionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class RemotePolicyMiddleware : ServiceEntity, IToolExecutionMiddleware {

    private readonly IRemotePolicyService? _remotePolicyService;
    private readonly ILogger<RemotePolicyMiddleware> _logger;

    /// <summary>
    /// 构造函数 — 注入远程策略服务和日志记录器
    /// </summary>
    /// <param name="remotePolicyService">远程策略服务实例，为 null 则跳过策略检查</param>
    /// <param name="logger">日志记录器实例</param>
    public RemotePolicyMiddleware(
        IRemotePolicyService? remotePolicyService,
        ILogger<RemotePolicyMiddleware> logger) {
        _remotePolicyService = remotePolicyService;
        _logger = logger;
    }

    /// <summary>
    /// 构造策略上下文（工具名+参数）调用远程策略服务评估；若不允许则拒绝工具执行并返回原因，否则调用下一层中间件
    /// </summary>
    /// <param name="context">工具执行上下文</param>
    /// <param name="next">下一层中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task InvokeAsync(
        ToolExecutionContext context,
        MiddlewareDelegate<ToolExecutionContext> next,
        CancellationToken ct) {
        if (_remotePolicyService is not null) {
            var policyContext = new Dictionary<string, string> {
                ["toolName"] = context.ToolName
            };

            foreach (var kvp in context.Arguments) {
                policyContext[$"arg_{kvp.Key}"] = kvp.Value.ValueKind == JsonValueKind.String
                    ? kvp.Value.GetString() ?? string.Empty
                    : kvp.Value.GetRawText();
            }

            var result = await _remotePolicyService.EvaluateAsync(
                context.ToolName, policyContext, ct).ConfigureAwait(false);

            if (!result.Allowed) {
                _logger.LogWarning(L.T(StringKey.RemotePolicyDeniedLog,
                    context.ToolName, result.RuleId, result.Reason));
                context.Deny(L.T(StringKey.RemotePolicyDeniedTool, context.ToolName, result.Reason));
                return;
            }
        }

        await next(context, ct).ConfigureAwait(false);
    }
}