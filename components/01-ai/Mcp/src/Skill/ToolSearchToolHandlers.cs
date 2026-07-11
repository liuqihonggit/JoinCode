

namespace McpToolHandlers;

[McpToolHandler(SystemToolNameConstants.ToolSearch, Optional = true)]
public partial class ToolSearchToolHandlers
{
    private readonly IMcpToolRegistry _toolRegistry;
    [Inject] private readonly ILogger<ToolSearchToolHandlers>? _logger;

    public ToolSearchToolHandlers(IMcpToolRegistry toolRegistry, ILogger<ToolSearchToolHandlers>? logger = null)
    {
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _logger = logger;
    }

    [McpTool(SystemToolNameConstants.ToolSearch, "Search available tools by keyword or exact selection", "system")]
    public async Task<ToolResult> SearchToolsAsync(
        [McpToolParameter("Search query: keyword search, 'select:Name1,Name2' for exact selection, '+name' for must-include")] string query,
        [McpToolParameter("Maximum number of results (optional, default 10)", Required = false)] int? max_results = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return McpResultBuilder.Error().WithText(L.T(StringKey.ToolSearchQueryCannotBeEmpty)).Build();

        try
        {
            var allTools = await _toolRegistry.GetAllToolsAsync(cancellationToken).ConfigureAwait(false);
            var deferredTools = allTools.Select(t => new DeferredToolInfo(t.Key, t.Value.Description, null, false)).ToList();
            var engine = new ToolSearchEngine(deferredTools);
            var result = engine.Search(query, max_results ?? 10);

            var response = new System.Text.StringBuilder();
            response.AppendLine(L.T(StringKey.ToolSearchResultTitle, query));
            response.AppendLine();

            if (result.MatchedToolNames.Count == 0)
            {
                response.AppendLine(L.T(StringKey.ToolSearchNoMatchingTools));
                response.AppendLine(L.T(StringKey.ToolSearchRegisteredToolsCount, allTools.Count));
            }
            else
            {
                foreach (var name in result.MatchedToolNames)
                {
                    if (allTools.TryGetValue(name, out var tool))
                        response.AppendLine($"- {name}: {tool.Description}");
                    else
                        response.AppendLine($"- {name}: no description");
                }

                response.AppendLine();
                response.AppendLine(L.T(StringKey.ToolSearchMatchedCount, result.MatchedToolNames.Count, allTools.Count));
            }

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.ToolSearchFailedLog));
            return McpResultBuilder.Error().WithText(L.T(StringKey.ToolSearchFailed, ex.Message)).Build();
        }
    }
}
