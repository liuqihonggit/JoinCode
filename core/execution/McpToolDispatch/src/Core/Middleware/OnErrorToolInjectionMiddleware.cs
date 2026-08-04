namespace McpToolRegistry;

/// <summary>
/// 报错组动态注入中间件 — Order=800 — 工具执行失败时自动注入OnError工具说明
/// OnError工具不出现在首次系统提示词，仅留函数名；首次报错时弹出工具说明让LLM选择
/// </summary>
[Register]
public sealed partial class OnErrorToolInjectionMiddleware : IToolExecutionMiddleware
{
    [Inject] private readonly IToolRegistry _registry = null!;
    [Inject] private readonly ILogger<OnErrorToolInjectionMiddleware> _logger = null!;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(ToolExecutionContext context, MiddlewareDelegate<ToolExecutionContext> next, CancellationToken ct)
    {
        await next(context, ct).ConfigureAwait(false);

        if (context.Result is null || !context.Result.IsError) return;

        var onErrorTools = await _registry.GetToolsByKindAsync(ToolKind.OnError, ct).ConfigureAwait(false);
        if (onErrorTools.Count == 0) return;

        var relevantTools = FindRelevantOnErrorTools(context.ToolName, onErrorTools);
        if (relevantTools.Count == 0) return;

        var sb = new StringBuilder(512);
        sb.AppendLine($"工具 '{context.ToolName}' 执行失败。以下修复工具可用：");
        foreach (var tool in relevantTools.Values)
        {
            sb.AppendLine($"- {tool.Name}: {tool.Description}");
        }
        sb.AppendLine("请选择合适的修复工具，或尝试其他方式解决问题。");

        var injection = new JoinCode.Abstractions.LLM.Chat.ApiMessage(
            JoinCode.Abstractions.LLM.Chat.MessageRole.User, sb.ToString());
        context.Result = context.Result with
        {
            InjectedMessages = [.. (context.Result.InjectedMessages ?? []), injection]
        };

        _logger?.LogDebug("已注入 {Count} 个OnError工具说明到上下文", relevantTools.Count);
    }

    private static Dictionary<string, IToolHandler> FindRelevantOnErrorTools(
        string failedToolName,
        IReadOnlyDictionary<string, IToolHandler> onErrorTools)
    {
        var result = new Dictionary<string, IToolHandler>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in onErrorTools)
        {
            if (tool.Value.GroupName is not null &&
                tool.Value.GroupName.Equals(failedToolName, StringComparison.OrdinalIgnoreCase))
            {
                result[tool.Key] = tool.Value;
            }
        }

        if (result.Count == 0)
        {
            foreach (var tool in onErrorTools)
                result[tool.Key] = tool.Value;
        }

        return result;
    }
}
