namespace Infrastructure.Localization;

public static partial class LocalizerInitializer
{
    private static void RegisterUserEntries(Dictionary<string, string> defaultEntries, Dictionary<string, string> zhEntries)
    {
        // === ListPeersToolHandlers ===
        defaultEntries[StringKey.ListPeersDesc] = "List connected peer sessions";
        defaultEntries[StringKey.ListPeersFilterDesc] = "Filter type: all/connected (default: all)";
        defaultEntries[StringKey.PeerListTitle] = "Peer List";
        defaultEntries[StringKey.PeerDiscoveryNotEnabled] = "Peer discovery service is not enabled";
        defaultEntries[StringKey.EnsureBridgeRunning] = "Please ensure the Bridge service is running";
        defaultEntries[StringKey.NoConnectedPeers] = "No connected peers";
        defaultEntries[StringKey.PeerAutoAppearHint] = "Hint: Other JCC sessions will automatically appear in this list once connected";
        defaultEntries[StringKey.ConnectedPeerCount] = "{0} connected peer(s):";
        defaultEntries[StringKey.LabelConnectedAt] = "Connected at: {0} (connected for {1})";
        defaultEntries[StringKey.ListPeersFailedLog] = "Failed to list peers";
        defaultEntries[StringKey.ListPeersFailed] = "Failed to list peers: {0}";
        defaultEntries[StringKey.DurationSeconds] = "{0}s";
        defaultEntries[StringKey.DurationMinutesSeconds] = "{0}m{1}s";
        defaultEntries[StringKey.DurationHoursMinutes] = "{0}h{1}m";

        zhEntries[StringKey.ListPeersDesc] = "列出已连接的对等节点会话";
        zhEntries[StringKey.ListPeersFilterDesc] = "过滤类型: all/connected（默认all）";
        zhEntries[StringKey.PeerListTitle] = "对等节点列表";
        zhEntries[StringKey.PeerDiscoveryNotEnabled] = "对等节点发现服务未启用";
        zhEntries[StringKey.EnsureBridgeRunning] = "请确保 Bridge 服务正在运行";
        zhEntries[StringKey.NoConnectedPeers] = "暂无已连接的对等节点";
        zhEntries[StringKey.PeerAutoAppearHint] = "提示: 其他 JCC 会话连接后，将自动出现在此列表中";
        zhEntries[StringKey.ConnectedPeerCount] = "共 {0} 个已连接的对等节点:";
        zhEntries[StringKey.LabelConnectedAt] = "连接时间: {0} (已连接 {1})";
        zhEntries[StringKey.ListPeersFailedLog] = "列出对等节点失败";
        zhEntries[StringKey.ListPeersFailed] = "列出对等节点失败: {0}";
        zhEntries[StringKey.DurationSeconds] = "{0}秒";
        zhEntries[StringKey.DurationMinutesSeconds] = "{0}分{1}秒";
        zhEntries[StringKey.DurationHoursMinutes] = "{0}时{1}分";

        // === SubscribePRToolHandlers ===
        defaultEntries[StringKey.SubscribePRDesc] = "Subscribe to GitHub Pull Request change notifications";
        defaultEntries[StringKey.SubscribePRActionDesc] = "Action: subscribe/unsubscribe/list";
        defaultEntries[StringKey.SubscribePRRefDesc] = "PR URL or repo#PR_number format (required for subscribe/unsubscribe)";
        defaultEntries[StringKey.SubscribePREventsDesc] = "Notification event types: all/comments/reviews/commits/status (optional, default: all)";
        defaultEntries[StringKey.GitHubServiceNotConfigured] = "GitHub service is not configured. Please set the JCC_GITHUB_TOKEN environment variable.";
        defaultEntries[StringKey.SubscribeRequiresPrRef] = "subscribe action requires pr_ref";
        defaultEntries[StringKey.UnsubscribeRequiresPrRef] = "unsubscribe action requires pr_ref";
        defaultEntries[StringKey.SubscribedPR] = "Subscribed to PR: {0}";
        defaultEntries[StringKey.LabelEventType] = "Event type: {0}";
        defaultEntries[StringKey.LabelSubscribedAt] = "Subscribed at: {0}";
        defaultEntries[StringKey.UnsubscribedPR] = "Unsubscribed from PR: {0}";
        defaultEntries[StringKey.PRSubscriptionList] = "PR Subscription List";
        defaultEntries[StringKey.NoPRSubscriptions] = "No PR subscriptions";
        defaultEntries[StringKey.PRSubscriptionCount] = "{0} subscription(s):";
        defaultEntries[StringKey.LabelEvents] = "events: {0}";
        defaultEntries[StringKey.LabelSubscribedOn] = "subscribed on: {0}";
        defaultEntries[StringKey.UnknownAction] = "Unknown action: {0}. Supported: subscribe/unsubscribe/list";
        defaultEntries[StringKey.PRSubscriptionFailedLog] = "PR subscription operation failed";
        defaultEntries[StringKey.PRSubscriptionFailed] = "PR subscription operation failed: {0}";

        zhEntries[StringKey.SubscribePRDesc] = "订阅 GitHub Pull Request 变更通知";
        zhEntries[StringKey.SubscribePRActionDesc] = "操作: subscribe/unsubscribe/list";
        zhEntries[StringKey.SubscribePRRefDesc] = "PR URL 或 仓库#PR号 格式（subscribe/unsubscribe 时必需）";
        zhEntries[StringKey.SubscribePREventsDesc] = "通知事件类型: all/comments/reviews/commits/status（可选，默认all）";
        zhEntries[StringKey.GitHubServiceNotConfigured] = "GitHub 服务未配置。请设置 JCC_GITHUB_TOKEN 环境变量。";
        zhEntries[StringKey.SubscribeRequiresPrRef] = "subscribe 操作需要指定 pr_ref";
        zhEntries[StringKey.UnsubscribeRequiresPrRef] = "unsubscribe 操作需要指定 pr_ref";
        zhEntries[StringKey.SubscribedPR] = "已订阅 PR: {0}";
        zhEntries[StringKey.LabelEventType] = "事件类型: {0}";
        zhEntries[StringKey.LabelSubscribedAt] = "订阅时间: {0}";
        zhEntries[StringKey.UnsubscribedPR] = "已取消订阅 PR: {0}";
        zhEntries[StringKey.PRSubscriptionList] = "PR 订阅列表";
        zhEntries[StringKey.NoPRSubscriptions] = "暂无 PR 订阅";
        zhEntries[StringKey.PRSubscriptionCount] = "共 {0} 个订阅:";
        zhEntries[StringKey.LabelEvents] = "事件: {0}";
        zhEntries[StringKey.LabelSubscribedOn] = "订阅于: {0}";
        zhEntries[StringKey.UnknownAction] = "未知操作: {0}，支持: subscribe/unsubscribe/list";
        zhEntries[StringKey.PRSubscriptionFailedLog] = "PR 订阅操作失败";
        zhEntries[StringKey.PRSubscriptionFailed] = "PR 订阅操作失败: {0}";
    }
}
