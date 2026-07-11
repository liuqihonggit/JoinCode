

namespace McpToolHandlers;

[McpToolHandler(ToolCategory.Context, Optional = true)]
public partial class CtxInspectToolHandlers
{
    private readonly IChatContextManager _contextManager;
    [Inject] private readonly ILogger<CtxInspectToolHandlers>? _logger;

    public CtxInspectToolHandlers(IChatContextManager contextManager, ILogger<CtxInspectToolHandlers>? logger = null)
    {
        _contextManager = contextManager ?? throw new ArgumentNullException(nameof(contextManager));
        _logger = logger;
    }

    [McpTool(SystemToolNameConstants.CtxInspect, "Inspect current context window usage", "context")]
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
            response.AppendLine(L.T(StringKey.CtxDeferredToolCount, deferredTools.Count));

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

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.CtxInspectFailedLog));
            return McpResultBuilder.Error().WithText(L.T(StringKey.CtxInspectFailed, ex.Message)).Build();
        }
    }
}
