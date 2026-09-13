

namespace McpToolDispatch;

/// <summary>
/// 远程触发器工具处理器 — 提供远程触发的列表/查询/创建/更新/执行功能
/// </summary>
[McpToolDispatch(ToolCategory.RemoteTrigger, Optional = true)]
public partial class RemoteTriggerToolHandlers
{
    private readonly ILogger<RemoteTriggerToolHandlers>? _logger;
    private readonly IRemoteTriggerService? _triggerService;

    /// <summary>
    /// 初始化远程触发器工具处理器
    /// </summary>
    /// <param name="logger">日志记录器（可选）</param>
    /// <param name="triggerService">远程触发服务（可选）</param>
    public RemoteTriggerToolHandlers(ILogger<RemoteTriggerToolHandlers>? logger = null, IRemoteTriggerService? triggerService = null)
    {
        _logger = logger;
        _triggerService = triggerService;
    }

    /// <summary>
    /// 管理远程触发器 — 支持 list/get/create/update/run 五种操作
    /// </summary>
    /// <param name="action">操作类型：list/get/create/update/run</param>
    /// <param name="trigger_id">触发器 ID（get/update/run 时必填）</param>
    /// <param name="body">触发器配置（JSON 格式，create/update 时可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果</returns>
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
                return ToolResultBuilder.Error()
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
                return ToolResultBuilder.Error()
                    .WithText(L.T(StringKey.RemoteTriggerUnknownAction, action))
                    .Build();
            }

            if ((triggerAction == TriggerAction.Get || triggerAction == TriggerAction.Update || triggerAction == TriggerAction.Run)
                && string.IsNullOrEmpty(trigger_id))
            {
                return ToolResultBuilder.Error()
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
                return ToolResultBuilder.Success().WithText(response.ToString()).Build();
            }

            response.AppendLine();
            response.AppendLine(L.T(StringKey.RemoteTriggerErrorResponse, result.Json));
            return ToolResultBuilder.Error().WithText(response.ToString()).Build();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "{Message}", L.T(StringKey.RemoteTriggerFailedLog));
            return ToolResultBuilder.Error().WithText(L.T(StringKey.RemoteTriggerFailed, ex.Message)).Build();
        }
    }
}
