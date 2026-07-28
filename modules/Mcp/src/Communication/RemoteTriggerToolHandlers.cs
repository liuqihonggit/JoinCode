

namespace McpToolHandlers;

[McpToolHandler(ToolCategory.RemoteTrigger, Optional = true)]
public partial class RemoteTriggerToolHandlers
{
    [Inject] private readonly ILogger<RemoteTriggerToolHandlers>? _logger;
    private readonly IRemoteTriggerService? _triggerService;

    public RemoteTriggerToolHandlers(ILogger<RemoteTriggerToolHandlers>? logger = null, IRemoteTriggerService? triggerService = null)
    {
        _logger = logger;
        _triggerService = triggerService;
    }

    [McpTool(SystemToolNameConstants.RemoteTrigger, "Manage remote triggers (list/get/create/update/run)", "trigger")]
    public async Task<ToolResult> ManageRemoteTriggerAsync(
        [McpToolParameter("Action type: list/get/create/update/run", Required = false)] string action = "list",
        [McpToolParameter("Trigger ID (required for get/update/run)", Required = false)] string? trigger_id = null,
        [McpToolParameter("Trigger config (JSON format, optional for create/update)", Required = false)] string? body = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_triggerService == null)
            {
                return McpResultBuilder.Error()
                    .WithText(L.T(StringKey.RemoteTriggerServiceNotConfigured))
                    .Build();
            }

            var triggerAction = action.ToLowerInvariant() switch
            {
                "list" => TriggerAction.List,
                "get" => TriggerAction.Get,
                "create" => TriggerAction.Create,
                "update" => TriggerAction.Update,
                "run" => TriggerAction.Run,
                _ => (TriggerAction?)null
            };

            if (triggerAction == null)
            {
                return McpResultBuilder.Error()
                    .WithText(L.T(StringKey.RemoteTriggerUnknownAction, action))
                    .Build();
            }

            if ((triggerAction == TriggerAction.Get || triggerAction == TriggerAction.Update || triggerAction == TriggerAction.Run)
                && string.IsNullOrEmpty(trigger_id))
            {
                return McpResultBuilder.Error()
                    .WithText(L.T(StringKey.RemoteTriggerActionRequiresId, action))
                    .Build();
            }

            var result = await _triggerService.ExecuteAsync(triggerAction.Value, trigger_id, body, cancellationToken).ConfigureAwait(false);

            var response = new System.Text.StringBuilder();
            response.AppendLine(L.T(StringKey.RemoteTriggerHeader, action));
            response.AppendLine(L.T(StringKey.RemoteTriggerLabelStatusCode, result.Status.ToString()));

            if (result.Status >= 200 && result.Status < 300)
            {
                response.AppendLine();
                response.AppendLine(result.Json);
                return McpResultBuilder.Success().WithText(response.ToString()).Build();
            }

            response.AppendLine();
            response.AppendLine(L.T(StringKey.RemoteTriggerErrorResponse, result.Json));
            return McpResultBuilder.Error().WithText(response.ToString()).Build();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "{Message}", L.T(StringKey.RemoteTriggerFailedLog));
            return McpResultBuilder.Error().WithText(L.T(StringKey.RemoteTriggerFailed, ex.Message)).Build();
        }
    }
}
