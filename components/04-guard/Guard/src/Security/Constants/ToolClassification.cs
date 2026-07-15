namespace Core.Security.Constants;

/// <summary>
/// 工具分类常量 - 统一管理工具的安全分类，避免多处硬编码
/// </summary>
public static class ToolClassification
{
    /// <summary>
    /// 只读工具 - 仅读取信息，不修改任何状态
    /// </summary>
    public static readonly FrozenSet<string> ReadOnlyTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        FileToolNameConstants.FileRead, FileToolNameConstants.FileList, FileToolNameConstants.DirectoryList, SearchToolNameConstants.Glob, SearchToolNameConstants.Grep, SearchToolNameConstants.Search,
        WebToolNameConstants.WebFetch, WebToolNameConstants.WebSearch,
        TaskToolNameConstants.TaskList, TaskToolNameConstants.TaskGet,
        TodoToolNameConstants.TodoList, TodoToolNameConstants.TodoRead,
        SearchToolNameConstants.CodeSearch, SearchToolNameConstants.SymbolSearch,
        // MCP 只读工具 — list/read 操作无副作用
        McpToolNameConstants.McpListTools, McpToolNameConstants.McpListResources,
        McpToolNameConstants.McpReadResource, McpToolNameConstants.McpListPrompts,
        McpToolNameConstants.McpGetPrompt, McpToolNameConstants.McpListServers,
        McpToolNameConstants.McpListClients
    }.ToFrozenSet();

    /// <summary>
    /// 安全写入工具 - 在自动审批模式下被视为低风险的写入操作
    /// </summary>
    public static readonly FrozenSet<string> SafeWriteTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        FileToolNameConstants.FileWrite, FileToolNameConstants.FileEdit, TodoToolNameConstants.TodoWrite,
        // MCP 状态变更工具 — connect/disconnect 是连接管理；call_tool 的实际风险由远端服务器管控
        McpToolNameConstants.McpConnect, McpToolNameConstants.McpDisconnect,
        McpToolNameConstants.McpCallTool
    }.ToFrozenSet();

    /// <summary>
    /// 敏感工具 - 需要额外确认的高风险操作（自动审批视角）
    /// </summary>
    public static readonly FrozenSet<string> SensitiveTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ShellToolNameConstants.Bash, ShellToolNameConstants.Powershell,
        FileToolNameConstants.FileDelete, GitToolNameConstants.GitReset, GitToolNameConstants.GitClean, GitToolNameConstants.GitPush
    }.ToFrozenSet();

    /// <summary>
    /// 破坏性工具 - Agent 权限上下文中被视为破坏性的操作
    /// 注意：Agent 视角下 FileWrite 也被视为破坏性，因为 Agent 不应随意写文件
    /// </summary>
    public static readonly FrozenSet<string> AgentDestructiveTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        FileToolNameConstants.FileDelete, FileToolNameConstants.FileMove, FileToolNameConstants.FileWrite,
        ShellToolNameConstants.Bash, ShellToolNameConstants.Powershell,
        GitToolNameConstants.GitReset, GitToolNameConstants.GitClean
    }.ToFrozenSet();
}
