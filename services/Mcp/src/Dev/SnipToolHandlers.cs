

namespace McpToolHandlers;

[McpToolHandler(ToolCategory.Snip, Optional = true)]
public partial class SnipToolHandlers
{
    private readonly IChatContextManager _contextManager;
    [Inject] private readonly ILogger<SnipToolHandlers>? _logger;

    public SnipToolHandlers(IChatContextManager contextManager, ILogger<SnipToolHandlers>? logger = null)
    {
        _contextManager = contextManager ?? throw new ArgumentNullException(nameof(contextManager));
        _logger = logger;
    }

    [McpTool(SystemToolNameConstants.Snip, "Snip old messages from conversation history to free context window space", "context")]
    public async Task<ToolResult> SnipHistoryAsync(
        [McpToolParameter("Snip mode: rewind/rewind_to/clear (default rewind)", Required = false)] string? mode = "rewind",
        [McpToolParameter("Message index (used in rewind_to mode)", Required = false)] int? message_index = null,
        CancellationToken cancellationToken = default)
    {
        var snipMode = SnipModeExtensions.FromValue(mode);
        if (snipMode == null)
            return McpResultBuilder.Error().WithText(L.T(StringKey.SnipUnknownMode, mode)).Build();

        try
        {
            var response = new System.Text.StringBuilder();

            switch (snipMode.Value)
            {
                case SnipMode.Rewind:
                    var rewindResult = await _contextManager.RewindLastTurnAsync(cancellationToken).ConfigureAwait(false);
                    response.AppendLine(L.T(StringKey.SnipRewindSuccess));
                    response.AppendLine(L.T(StringKey.SnipRemovedCount, rewindResult.RemovedCount));
                    break;

                case SnipMode.RewindTo:
                    if (message_index == null)
                        return McpResultBuilder.Error().WithText(L.T(StringKey.SnipRewindToRequiresIndex)).Build();
                    var rewindToResult = await _contextManager.RewindToMessageIndexAsync(message_index.Value, cancellationToken).ConfigureAwait(false);
                    response.AppendLine(L.T(StringKey.SnipRewindToSuccess, message_index));
                    response.AppendLine(L.T(StringKey.SnipRemovedCount, rewindToResult.RemovedCount));
                    break;

                case SnipMode.Clear:
                    var clearResult = await _contextManager.RewindToStartAsync(cancellationToken).ConfigureAwait(false);
                    response.AppendLine(L.T(StringKey.SnipClearSuccess));
                    response.AppendLine(L.T(StringKey.SnipRemovedCount, clearResult.RemovedCount));
                    break;
            }

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.SnipFailedLog));
            return McpResultBuilder.Error().WithText(L.T(StringKey.SnipFailed, ex.Message)).Build();
        }
    }
}
