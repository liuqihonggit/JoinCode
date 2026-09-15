

namespace McpToolDispatch;

/// <summary>
/// 上下文检查工具处理器 — 提供当前上下文窗口使用情况的检查功能
/// </summary>
[McpToolDispatch(ToolCategory.Context, Optional = true)]
public partial class CtxInspectToolHandlers
{
    private readonly IChatContextManager _contextManager;
    private readonly ILogger<CtxInspectToolHandlers>? _logger;

    /// <summary>
    /// 初始化 <see cref="CtxInspectToolHandlers"/> 实例
    /// </summary>
    /// <param name="contextManager">聊天上下文管理器</param>
    /// <param name="logger">日志记录器（可选）</param>
    public CtxInspectToolHandlers(IChatContextManager contextManager, ILogger<CtxInspectToolHandlers>? logger = null)
    {
        _contextManager = contextManager ?? throw new ArgumentNullException(nameof(contextManager));
        _logger = logger;
    }

    /// <summary>
    /// 检查当前上下文窗口使用情况
    /// </summary>
    /// <param name="inspect_type">检查类型：summary/detailed/layers（可选，默认 summary）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果</returns>
    [McpTool(SystemToolNameEnumConstants.CtxInspect, "Inspect current context window usage", "context")]
    public async Task<ToolResult> InspectContextAsync(
        [McpToolParameter("Inspection type: summary/detailed/layers (optional, default summary)", Required = false)] string? inspect_type = "summary",
        CancellationToken cancellationToken = default)
    {
        var inspectType = InspectTypeExtensions.FromValue(inspect_type) ?? InspectType.Summary;
        try
        {
            var maxTokens = _contextManager.GetContextMaxTokens();
            var history = await _contextManager.GetMessageListAsync(cancellationToken).ConfigureAwait(false);
            var deferredTools = _contextManager.GetDeferredTools();

            var response = new System.Text.StringBuilder();
            response.AppendLine(L.T(StringKey.CtxInspectTitle));
            response.AppendLine();

            var messageCount = history?.Count ?? 0;
            response.AppendLine(L.T(StringKey.CtxMaxTokens, maxTokens));
            response.AppendLine(L.T(StringKey.CtxMessageCount, messageCount));
            response.AppendLine(L.T(StringKey.CtxDeferredToolCount, deferredTools.Count()));

            if (inspectType == InspectType.Detailed || inspectType == InspectType.Layers)
            {
                response.AppendLine();
                response.AppendLine(L.T(StringKey.CtxDeferredToolDetails));
                foreach (var tool in deferredTools)
                {
                    response.AppendLine($"  {tool.Name}: {tool.Description ?? L.T(StringKey.CtxNoDescription)}{(tool.IsMcp ? " (MCP)" : "")}");
                }
            }

            if (inspectType == InspectType.Detailed && history != null)
            {
                response.AppendLine();
                response.AppendLine(L.T(StringKey.CtxRecentMessages));
                foreach (var msg in history.TakeLast(5))
                {
                    var role = msg.Role.ToString();
                    var content = msg.Content ?? "";
                    var truncated = content.Length > 80 ? content[..80] + "..." : content;
                    response.AppendLine($"  [{role}] {truncated}");
                }
            }

            return ToolResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.CtxInspectFailedLog));
            return ToolResultBuilder.Error().WithText(L.T(StringKey.CtxInspectFailed, ex.Message)).Build();
        }
    }
}
