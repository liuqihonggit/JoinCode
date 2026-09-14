

namespace McpToolDispatch;

/// <summary>
/// PR 订阅工具处理器 — 提供 Pull Request 订阅/取消订阅/列表查询功能
/// </summary>
[McpToolDispatch(ToolCategory.PrSubscription, Optional = true)]
public partial class SubscribePRToolHandlers
{
    private readonly ILogger<SubscribePRToolHandlers>? _logger;
    private readonly IGitHubService? _gitHubService;

    /// <summary>
    /// 初始化 PR 订阅工具处理器
    /// </summary>
    /// <param name="logger">日志记录器（可选）</param>
    /// <param name="gitHubService">GitHub 服务（可选）</param>
    public SubscribePRToolHandlers(ILogger<SubscribePRToolHandlers>? logger = null, IGitHubService? gitHubService = null)
    {
        _logger = logger;
        _gitHubService = gitHubService;
    }

    /// <summary>
    /// 管理 PR 订阅 — 支持 subscribe/unsubscribe/list 三种操作
    /// </summary>
    /// <param name="action">操作类型：subscribe/unsubscribe/list（默认 list）</param>
    /// <param name="pr_ref">PR 引用（subscribe/unsubscribe 时必填）</param>
    /// <param name="events">订阅事件类型（可选，默认 all）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果</returns>
    [McpTool(SystemToolNameEnumConstants.SubscribePR, StringKey.SubscribePRDesc, "github")]
    public async Task<ToolResult> SubscribePRAsync(
        [McpToolParameter(StringKey.SubscribePRActionDesc, Required = false)] string action = "list",
        [McpToolParameter(StringKey.SubscribePRRefDesc, Required = false)] string? pr_ref = null,
        [McpToolParameter(StringKey.SubscribePREventsDesc, Required = false)] string? events = "all",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var subAction = PrSubscriptionActionExtensions.FromValue(action);
            if (subAction == null)
                return ToolResultBuilder.Error().WithText(L.T(StringKey.UnknownAction, action)).Build();

            if (_gitHubService == null)
            {
                return ToolResultBuilder.Error()
                    .WithText(L.T(StringKey.GitHubServiceNotConfigured))
                    .Build();
            }

            var response = new System.Text.StringBuilder();

            switch (subAction.Value)
            {
                case PrSubscriptionAction.Subscribe:
                    if (string.IsNullOrEmpty(pr_ref))
                        return ToolResultBuilder.Error().WithText(L.T(StringKey.SubscribeRequiresPrRef)).Build();

                    var subscription = await _gitHubService.SubscribeAsync(pr_ref, events ?? "all", cancellationToken).ConfigureAwait(false);
                    response.AppendLine(L.T(StringKey.SubscribedPR, subscription.PrRef));
                    response.AppendLine(L.T(StringKey.LabelEventType, subscription.Events));
                    response.AppendLine(L.T(StringKey.LabelSubscribedAt, $"{subscription.SubscribedAt:yyyy-MM-dd HH:mm:ss}"));
                    break;

                case PrSubscriptionAction.Unsubscribe:
                    if (string.IsNullOrEmpty(pr_ref))
                        return ToolResultBuilder.Error().WithText(L.T(StringKey.UnsubscribeRequiresPrRef)).Build();

                    await _gitHubService.UnsubscribeAsync(pr_ref, cancellationToken).ConfigureAwait(false);
                    response.AppendLine(L.T(StringKey.UnsubscribedPR, pr_ref));
                    break;

                case PrSubscriptionAction.List:
                    var subscriptions = await _gitHubService.ListSubscriptionsAsync(cancellationToken).ConfigureAwait(false);
                    response.AppendLine(L.T(StringKey.PRSubscriptionList));
                    response.AppendLine();

                    if (subscriptions.Count == 0)
                    {
                        response.AppendLine(L.T(StringKey.NoPRSubscriptions));
                    }
                    else
                    {
                        response.AppendLine(L.T(StringKey.PRSubscriptionCount, subscriptions.Count));
                        foreach (var sub in subscriptions)
                        {
                            response.AppendLine($"  {sub.PrRef} ({L.T(StringKey.LabelEvents, sub.Events)}, {L.T(StringKey.LabelSubscribedOn, $"{sub.SubscribedAt:yyyy-MM-dd HH:mm:ss}")})");
                        }
                    }
                    break;
            }

            return ToolResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "{Message}", L.T(StringKey.PRSubscriptionFailedLog));
            return ToolResultBuilder.Error().WithText(L.T(StringKey.PRSubscriptionFailed, ex.Message)).Build();
        }
    }
}
