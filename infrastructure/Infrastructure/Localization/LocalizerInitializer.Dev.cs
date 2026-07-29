namespace Infrastructure.Localization;

public static partial class LocalizerInitializer
{
    private static void RegisterDevEntries(Dictionary<string, string> defaultEntries, Dictionary<string, string> zhEntries)
    {
        // === AnalyticsToolHandlers ===
        defaultEntries[StringKey.AnalyticsUsageReport] = "Usage statistics report";
        defaultEntries[StringKey.AnalyticsStatPeriod] = "Statistics period: last {0} days";
        defaultEntries[StringKey.AnalyticsTotalEvents] = "Total events: {0}";
        defaultEntries[StringKey.AnalyticsToolCalls] = "Tool calls: {0}";
        defaultEntries[StringKey.AnalyticsToolSuccessRate] = "Tool success rate: {0}%";
        defaultEntries[StringKey.AnalyticsAvgExecTime] = "Average execution time: {0}ms";
        defaultEntries[StringKey.AnalyticsErrorRate] = "Error rate: {0}%";
        defaultEntries[StringKey.AnalyticsTopTools] = "Top tools:";
        defaultEntries[StringKey.AnalyticsToolEntry] = "{0} times (success rate: {1}%)";
        defaultEntries[StringKey.AnalyticsDailyStats] = "Daily statistics:";
        defaultEntries[StringKey.AnalyticsDailyEntry] = "{0}: {1} events, {2} calls, {3} errors";
        defaultEntries[StringKey.AnalyticsToolUsageStats] = "Tool usage statistics";
        defaultEntries[StringKey.AnalyticsToolCount] = "{0} tools total";
        defaultEntries[StringKey.AnalyticsNoToolData] = "No tool usage data available";
        defaultEntries[StringKey.AnalyticsCallSummary] = "Calls: {0} | Success: {1} | Failed: {2}";
        defaultEntries[StringKey.AnalyticsSuccessRateDuration] = "Success rate: {0}% | Avg duration: {1}ms";
        defaultEntries[StringKey.AnalyticsLastCall] = "Last call: {0}";
        defaultEntries[StringKey.AnalyticsEventHistory] = "Event history";
        defaultEntries[StringKey.AnalyticsEventCount] = "{0} events total";
        defaultEntries[StringKey.AnalyticsNoEventData] = "No event data available";
        defaultEntries[StringKey.AnalyticsAgent] = "Agent: {0}";
        defaultEntries[StringKey.AnalyticsDuration] = "Duration: {0}ms";
        defaultEntries[StringKey.AnalyticsErrorInfo] = "Error: {0}";
        defaultEntries[StringKey.AnalyticsDataInfo] = "Data: {0}";
        defaultEntries[StringKey.AnalyticsDataExport] = "Analytics data export";
        defaultEntries[StringKey.AnalyticsStartDate] = "Start date: {0}";
        defaultEntries[StringKey.AnalyticsEndDate] = "End date: {0}";
        defaultEntries[StringKey.AnalyticsJsonData] = "JSON data:";
        defaultEntries[StringKey.AnalyticsTruncated] = "({0} characters total, truncated)";
        defaultEntries[StringKey.AnalyticsConfirmClear] = "Please enter 'yes' to confirm clearing analytics data";
        defaultEntries[StringKey.AnalyticsClearedOlder] = "Cleared analytics data older than {0} days";
        defaultEntries[StringKey.AnalyticsClearedAll] = "Cleared all analytics data";

        zhEntries[StringKey.AnalyticsUsageReport] = "使用统计报告";
        zhEntries[StringKey.AnalyticsStatPeriod] = "统计周期: 最近 {0} 天";
        zhEntries[StringKey.AnalyticsTotalEvents] = "总事件数: {0}";
        zhEntries[StringKey.AnalyticsToolCalls] = "工具调用: {0}";
        zhEntries[StringKey.AnalyticsToolSuccessRate] = "工具成功率: {0}%";
        zhEntries[StringKey.AnalyticsAvgExecTime] = "平均执行时间: {0}ms";
        zhEntries[StringKey.AnalyticsErrorRate] = "错误率: {0}%";
        zhEntries[StringKey.AnalyticsTopTools] = "最常用工具:";
        zhEntries[StringKey.AnalyticsToolEntry] = "{0} 次 (成功率: {1}%)";
        zhEntries[StringKey.AnalyticsDailyStats] = "每日统计:";
        zhEntries[StringKey.AnalyticsDailyEntry] = "{0}: {1} 事件, {2} 调用, {3} 错误";
        zhEntries[StringKey.AnalyticsToolUsageStats] = "工具使用统计";
        zhEntries[StringKey.AnalyticsToolCount] = "共 {0} 个工具";
        zhEntries[StringKey.AnalyticsNoToolData] = "暂无工具使用数据";
        zhEntries[StringKey.AnalyticsCallSummary] = "调用: {0} | 成功: {1} | 失败: {2}";
        zhEntries[StringKey.AnalyticsSuccessRateDuration] = "成功率: {0}% | 平均耗时: {1}ms";
        zhEntries[StringKey.AnalyticsLastCall] = "最后调用: {0}";
        zhEntries[StringKey.AnalyticsEventHistory] = "事件历史";
        zhEntries[StringKey.AnalyticsEventCount] = "共 {0} 条事件";
        zhEntries[StringKey.AnalyticsNoEventData] = "暂无事件数据";
        zhEntries[StringKey.AnalyticsAgent] = "代理: {0}";
        zhEntries[StringKey.AnalyticsDuration] = "耗时: {0}ms";
        zhEntries[StringKey.AnalyticsErrorInfo] = "错误: {0}";
        zhEntries[StringKey.AnalyticsDataInfo] = "数据: {0}";
        zhEntries[StringKey.AnalyticsDataExport] = "分析数据导出";
        zhEntries[StringKey.AnalyticsStartDate] = "开始日期: {0}";
        zhEntries[StringKey.AnalyticsEndDate] = "结束日期: {0}";
        zhEntries[StringKey.AnalyticsJsonData] = "JSON数据:";
        zhEntries[StringKey.AnalyticsTruncated] = "(共 {0} 字符，已截断)";
        zhEntries[StringKey.AnalyticsConfirmClear] = "请输入 'yes' 确认清除分析数据";
        zhEntries[StringKey.AnalyticsClearedOlder] = "已清除 {0} 天前的分析数据";
        zhEntries[StringKey.AnalyticsClearedAll] = "已清除所有分析数据";

        // === CtxInspectToolHandlers ===
        defaultEntries[StringKey.CtxInspectTitle] = "Context inspection";
        defaultEntries[StringKey.CtxMaxTokens] = "Context max tokens: {0}";
        defaultEntries[StringKey.CtxMessageCount] = "Message count: {0}";
        defaultEntries[StringKey.CtxDeferredToolCount] = "Deferred tools count: {0}";
        defaultEntries[StringKey.CtxDeferredToolDetails] = "Deferred tool details:";
        defaultEntries[StringKey.CtxNoDescription] = "No description";
        defaultEntries[StringKey.CtxRecentMessages] = "Recent messages:";
        defaultEntries[StringKey.CtxInspectFailedLog] = "Context inspection failed";
        defaultEntries[StringKey.CtxInspectFailed] = "Context inspection failed: {0}";

        zhEntries[StringKey.CtxInspectTitle] = "上下文检查";
        zhEntries[StringKey.CtxMaxTokens] = "上下文最大 Token: {0}";
        zhEntries[StringKey.CtxMessageCount] = "消息数量: {0}";
        zhEntries[StringKey.CtxDeferredToolCount] = "延迟工具数: {0}";
        zhEntries[StringKey.CtxDeferredToolDetails] = "延迟工具详情:";
        zhEntries[StringKey.CtxNoDescription] = "无描述";
        zhEntries[StringKey.CtxRecentMessages] = "最近消息:";
        zhEntries[StringKey.CtxInspectFailedLog] = "上下文检查失败";
        zhEntries[StringKey.CtxInspectFailed] = "上下文检查失败: {0}";

        // === MonitorToolHandlers ===
        defaultEntries[StringKey.MonitorStatusOverview] = "MCP status overview";
        defaultEntries[StringKey.MonitorLocalToolCount] = "Local tools: {0}";
        defaultEntries[StringKey.MonitorRemoteClientCount] = "Remote clients: {0}";
        defaultEntries[StringKey.MonitorRegisteredTools] = "Registered tools ({0})";
        defaultEntries[StringKey.MonitorRemoteClients] = "Remote MCP clients ({0})";
        defaultEntries[StringKey.MonitorUnknown] = "Unknown";
        defaultEntries[StringKey.MonitorClientNotFound] = "Client not found: {0}";
        defaultEntries[StringKey.MonitorClientHealthCheck] = "Client health check: {0}";
        defaultEntries[StringKey.MonitorConnected] = "Connected";
        defaultEntries[StringKey.MonitorDisconnected] = "Disconnected";
        defaultEntries[StringKey.MonitorHealthCheck] = "MCP health check";
        defaultEntries[StringKey.MonitorNoRemoteClients] = "No remote clients";
        defaultEntries[StringKey.MonitorUnknownType] = "Unknown monitor type: {0}, supported: status/tools/clients/health";
        defaultEntries[StringKey.MonitorFailedLog] = "MCP monitor failed";
        defaultEntries[StringKey.MonitorFailed] = "MCP monitor failed: {0}";

        zhEntries[StringKey.MonitorStatusOverview] = "MCP 状态概览";
        zhEntries[StringKey.MonitorLocalToolCount] = "本地工具数: {0}";
        zhEntries[StringKey.MonitorRemoteClientCount] = "远程客户端数: {0}";
        zhEntries[StringKey.MonitorRegisteredTools] = "已注册工具 ({0})";
        zhEntries[StringKey.MonitorRemoteClients] = "远程 MCP 客户端 ({0})";
        zhEntries[StringKey.MonitorUnknown] = "未知";
        zhEntries[StringKey.MonitorClientNotFound] = "未找到客户端: {0}";
        zhEntries[StringKey.MonitorClientHealthCheck] = "客户端健康检查: {0}";
        zhEntries[StringKey.MonitorConnected] = "已连接";
        zhEntries[StringKey.MonitorDisconnected] = "未连接";
        zhEntries[StringKey.MonitorHealthCheck] = "MCP 健康检查";
        zhEntries[StringKey.MonitorNoRemoteClients] = "无远程客户端";
        zhEntries[StringKey.MonitorUnknownType] = "未知监控类型: {0}，支持: status/tools/clients/health";
        zhEntries[StringKey.MonitorFailedLog] = "MCP 监控失败";
        zhEntries[StringKey.MonitorFailed] = "MCP 监控失败: {0}";

        // === SnipToolHandlers ===
        defaultEntries[StringKey.SnipRewindSuccess] = "Rewound last conversation turn";
        defaultEntries[StringKey.SnipRemovedCount] = "Removed messages: {0}";
        defaultEntries[StringKey.SnipRewindToRequiresIndex] = "rewind_to mode requires message_index";
        defaultEntries[StringKey.SnipRewindToSuccess] = "Rewound to message index {0}";
        defaultEntries[StringKey.SnipClearSuccess] = "Cleared all conversation history";
        defaultEntries[StringKey.SnipUnknownMode] = "Unknown snip mode: {0}, supported: rewind/rewind_to/clear";
        defaultEntries[StringKey.SnipFailedLog] = "History snip failed";
        defaultEntries[StringKey.SnipFailed] = "History snip failed: {0}";

        zhEntries[StringKey.SnipRewindSuccess] = "已撤回最后一轮对话";
        zhEntries[StringKey.SnipRemovedCount] = "撤回消息数: {0}";
        zhEntries[StringKey.SnipRewindToRequiresIndex] = "rewind_to 模式需要指定 message_index";
        zhEntries[StringKey.SnipRewindToSuccess] = "已撤回到消息索引 {0}";
        zhEntries[StringKey.SnipClearSuccess] = "已清空全部对话历史";
        zhEntries[StringKey.SnipUnknownMode] = "未知的裁剪模式: {0}，支持: rewind/rewind_to/clear";
        zhEntries[StringKey.SnipFailedLog] = "历史裁剪失败";
        zhEntries[StringKey.SnipFailed] = "历史裁剪失败: {0}";

        // === WebBrowserToolHandlers ===
        defaultEntries[StringKey.BrowserTargetCannotBeEmpty] = "target cannot be empty";
        defaultEntries[StringKey.BrowserFetched] = "Fetched: {0}";
        defaultEntries[StringKey.BrowserContentSize] = "Content size: {0} bytes";
        defaultEntries[StringKey.BrowserContentTruncated] = "(Content truncated)";
        defaultEntries[StringKey.BrowserFetchFailed] = "Fetch failed: {0}";
        defaultEntries[StringKey.BrowserUnknownError] = "Unknown error";
        defaultEntries[StringKey.BrowserScreenshotNotSupported] = "Screenshot requires browser runtime support";
        defaultEntries[StringKey.BrowserUseWebFetch] = "Please use web_fetch tool to get web content";
        defaultEntries[StringKey.BrowserJsNotSupported] = "JavaScript execution requires browser runtime support";
        defaultEntries[StringKey.BrowserUnknownAction] = "Unknown action: {0}, supported: open/screenshot/evaluate";
        defaultEntries[StringKey.BrowserFailedLog] = "Browser operation failed";
        defaultEntries[StringKey.BrowserFailed] = "Browser operation failed: {0}";

        zhEntries[StringKey.BrowserTargetCannotBeEmpty] = "target 不能为空";
        zhEntries[StringKey.BrowserFetched] = "已获取: {0}";
        zhEntries[StringKey.BrowserContentSize] = "内容大小: {0} 字节";
        zhEntries[StringKey.BrowserContentTruncated] = "(内容已截断)";
        zhEntries[StringKey.BrowserFetchFailed] = "获取失败: {0}";
        zhEntries[StringKey.BrowserUnknownError] = "未知错误";
        zhEntries[StringKey.BrowserScreenshotNotSupported] = "截图功能需要浏览器运行时支持";
        zhEntries[StringKey.BrowserUseWebFetch] = "请使用 web_fetch 工具获取网页内容";
        zhEntries[StringKey.BrowserJsNotSupported] = "JavaScript 执行需要浏览器运行时支持";
        zhEntries[StringKey.BrowserUnknownAction] = "未知操作: {0}，支持: open/screenshot/evaluate";
        zhEntries[StringKey.BrowserFailedLog] = "浏览器操作失败";
        zhEntries[StringKey.BrowserFailed] = "浏览器操作失败: {0}";
    }
}
