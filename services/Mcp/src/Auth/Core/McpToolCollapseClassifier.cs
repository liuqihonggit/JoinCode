namespace McpClient.Auth;

/// <summary>
/// MCP 工具折叠分类器 — 对齐 TS classifyMcpToolForCollapse
/// 将 MCP 工具分类为"搜索"或"读取"操作，用于 UI 折叠显示
/// </summary>
public static partial class McpToolCollapseClassifier
{
    /// <summary>
    /// 分类结果 — 对齐 TS { isSearch: boolean; isRead: boolean }
    /// </summary>
    public sealed class CollapseClassification
    {
        public bool IsSearch { get; init; }
        public bool IsRead { get; init; }
    }

    /// <summary>
    /// 对工具名进行分类 — 对齐 TS classifyMcpToolForCollapse
    /// </summary>
    public static CollapseClassification Classify(string toolName)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolName);
        var normalized = Normalize(toolName);
        return new CollapseClassification
        {
            IsSearch = SearchTools.Contains(normalized),
            IsRead = ReadTools.Contains(normalized)
        };
    }

    /// <summary>
    /// 名称规范化 — 对齐 TS normalize
    /// camelCase → snake_case, kebab-case → snake_case, toLowerCase
    /// </summary>
    private static string Normalize(string name)
    {
        return CamelCaseRegex().Replace(name, "$1_$2")
            .Replace('-', '_')
            .ToLowerInvariant();
    }

    /// <summary>
    /// 搜索类工具白名单 — 对齐 TS SEARCH_TOOLS
    /// </summary>
    private static readonly FrozenSet<string> SearchTools = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "search_code", "search_repositories", "search_issues", "search_users",
        "search_public", "search_channels", "search_messages", "search_files",
        "search_emails", "search_events", "search_tasks", "search_notes",
        "search_documents", "search_records", "search_items", "search_works",
        "search_incidents", "search_logs", "search_metrics", "search_traces",
        "search_alerts", "search_dashboards", "search_organizations",
        "web_search", "search", "query", "find", "lookup"
    );

    /// <summary>
    /// 读取类工具白名单 — 对齐 TS READ_TOOLS
    /// </summary>
    private static readonly FrozenSet<string> ReadTools = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "get", "get_file", "get_issue", "get_pull_request", "get_user",
        "get_channel", "get_message", "get_event", "get_task", "get_note",
        "get_document", "get_record", "get_item", "get_work", "get_incident",
        "get_log", "get_metric", "get_trace", "get_alert", "get_dashboard",
        "read", "read_file", "read_resource", "read_content",
        "list", "list_files", "list_issues", "list_repos", "list_users",
        "list_channels", "list_messages", "list_events", "list_tasks", "list_notes",
        "list_documents", "list_records", "list_items", "list_works",
        "list_incidents", "list_logs", "list_metrics", "list_traces",
        "list_alerts", "list_dashboards", "list_organizations",
        "fetch", "fetch_file", "fetch_issue", "fetch_resource",
        "describe", "describe_cluster", "describe_instance",
        "query", "query_database", "query_log", "query_metric",
        "aggregate", "aggregate_log", "aggregate_metric",
        "show", "view", "display", "inspect", "info"
    );

    [GeneratedRegex(@"([a-z])([A-Z])", RegexOptions.Compiled)]
    private static partial Regex CamelCaseRegex();
}
