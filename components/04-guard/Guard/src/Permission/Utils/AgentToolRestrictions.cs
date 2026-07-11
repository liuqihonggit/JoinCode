namespace Core.Utils;

[Register]
public sealed partial class AgentToolRestrictions : IAgentToolRestrictions
{
    [Inject] private readonly ITelemetryService? _telemetryService;

    private static readonly FrozenSet<string> AutoAllowedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        FileToolNameConstants.FileRead, FileToolNameConstants.DirectoryList, SearchToolNameConstants.Glob, SearchToolNameConstants.Grep,
        SearchToolNameConstants.SearchCode, SearchToolNameConstants.SearchText,
        WebToolNameConstants.WebFetch, WebToolNameConstants.WebSearch,
        TaskToolNameConstants.TaskList, TaskToolNameConstants.TaskGet,
        SystemToolNameConstants.TaskOutput,
        TodoToolNameConstants.TodoList, TodoToolNameConstants.TodoWrite,
        FileToolNameConstants.FileWrite, FileToolNameConstants.FileEdit,
        CodeToolNameConstants.CodeIndexSearch, CodeToolNameConstants.CodeIndexSearchComprehensive,
        CodeToolNameConstants.CodeIndexFindDefinition,
        // MCP 管理工具 — connect/disconnect 是状态变更但无持久副作用；list/read 是只读；call_tool 需先 connect
        McpToolNameConstants.McpConnect, McpToolNameConstants.McpDisconnect,
        McpToolNameConstants.McpListTools, McpToolNameConstants.McpCallTool,
        McpToolNameConstants.McpListResources, McpToolNameConstants.McpReadResource,
        McpToolNameConstants.McpListPrompts, McpToolNameConstants.McpGetPrompt,
        McpToolNameConstants.McpListServers, McpToolNameConstants.McpListClients,
        // Agent 工具 — 子代理 spawn 和管理（子代理继承父级权限模式，spawn 管道自有安全检查）
        AgentToolNameConstants.Agent, AgentToolNameConstants.AgentSpawn,
        AgentToolNameConstants.AgentList, AgentToolNameConstants.AgentStatus,
        AgentToolNameConstants.AgentStop, AgentToolNameConstants.AgentRunning,
        AgentToolNameConstants.AgentRunningStats,
        AgentToolNameConstants.AgentSendMessage
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> PlanAllowedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        FileToolNameConstants.FileRead, FileToolNameConstants.DirectoryList, SearchToolNameConstants.Glob, SearchToolNameConstants.Grep,
        SearchToolNameConstants.SearchCode, SearchToolNameConstants.SearchText,
        WebToolNameConstants.WebFetch, WebToolNameConstants.WebSearch,
        TaskToolNameConstants.TaskList, TaskToolNameConstants.TaskGet,
        SystemToolNameConstants.TaskOutput,
        TodoToolNameConstants.TodoList, TodoToolNameConstants.TodoWrite,
        CodeToolNameConstants.CodeIndexSearch, CodeToolNameConstants.CodeIndexSearchComprehensive,
        CodeToolNameConstants.CodeIndexFindDefinition,
        // Plan 模式只允许只读 MCP 工具（list/read）
        McpToolNameConstants.McpListTools, McpToolNameConstants.McpListResources,
        McpToolNameConstants.McpReadResource, McpToolNameConstants.McpListPrompts,
        McpToolNameConstants.McpGetPrompt, McpToolNameConstants.McpListServers,
        McpToolNameConstants.McpListClients
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> AskAllowedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        FileToolNameConstants.FileRead, FileToolNameConstants.DirectoryList, SearchToolNameConstants.Glob, SearchToolNameConstants.Grep,
        SearchToolNameConstants.SearchCode, SearchToolNameConstants.SearchText,
        WebToolNameConstants.WebFetch, WebToolNameConstants.WebSearch,
        TaskToolNameConstants.TaskList, TaskToolNameConstants.TaskGet,
        SystemToolNameConstants.TaskOutput,
        TodoToolNameConstants.TodoList,
        FileToolNameConstants.FileWrite, FileToolNameConstants.FileEdit,
        ShellToolNameConstants.ShellExecute, ShellToolNameConstants.Powershell,
        CodeToolNameConstants.CodeIndexSearch, CodeToolNameConstants.CodeIndexSearchComprehensive,
        CodeToolNameConstants.CodeIndexFindDefinition,
        // Ask 模式允许所有 MCP 工具（最宽松）
        McpToolNameConstants.McpConnect, McpToolNameConstants.McpDisconnect,
        McpToolNameConstants.McpListTools, McpToolNameConstants.McpCallTool,
        McpToolNameConstants.McpListResources, McpToolNameConstants.McpReadResource,
        McpToolNameConstants.McpListPrompts, McpToolNameConstants.McpGetPrompt,
        McpToolNameConstants.McpListServers, McpToolNameConstants.McpListClients
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> DenyAllowedTools = FrozenSet<string>.Empty;

    private static readonly FrozenSet<string> AutoDeniedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ShellToolNameConstants.ShellExecute, ShellToolNameConstants.Powershell,
        GitToolNameConstants.GitCommit, GitToolNameConstants.GitPush
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> PlanDeniedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        FileToolNameConstants.FileWrite, FileToolNameConstants.FileEdit,
        ShellToolNameConstants.ShellExecute, ShellToolNameConstants.Powershell,
        GitToolNameConstants.GitCommit, GitToolNameConstants.GitPush
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> AskDeniedTools = FrozenSet<string>.Empty;

    private static readonly FrozenSet<string> DenyDeniedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "*"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> GetAllowedTools(PermissionMode mode)
    {
        return mode switch
        {
            PermissionMode.Auto => AutoAllowedTools,
            PermissionMode.Plan => PlanAllowedTools,
            PermissionMode.Ask => AskAllowedTools,
            PermissionMode.Deny => DenyAllowedTools,
            _ => AutoAllowedTools
        };
    }

    public IReadOnlySet<string> GetDeniedTools(PermissionMode mode)
    {
        return mode switch
        {
            PermissionMode.Auto => AutoDeniedTools,
            PermissionMode.Plan => PlanDeniedTools,
            PermissionMode.Ask => AskDeniedTools,
            PermissionMode.Deny => DenyDeniedTools,
            _ => AutoDeniedTools
        };
    }

    public bool IsToolAllowedForMode(string toolName, PermissionMode mode)
    {
        if (mode == PermissionMode.Deny)
        {
            RecordPermissionCheckMetrics(toolName, mode, false);
            return false;
        }

        var denied = GetDeniedTools(mode);
        if (denied.Contains(toolName))
        {
            RecordPermissionCheckMetrics(toolName, mode, false);
            return false;
        }

        if (denied.Contains("*"))
        {
            RecordPermissionCheckMetrics(toolName, mode, false);
            return false;
        }

        var allowed = GetAllowedTools(mode);
        if (allowed.Count == 0)
        {
            var result = mode != PermissionMode.Deny;
            RecordPermissionCheckMetrics(toolName, mode, result);
            return result;
        }

        var allowedResult = allowed.Contains(toolName);
        RecordPermissionCheckMetrics(toolName, mode, allowedResult);
        return allowedResult;
    }

    private void RecordPermissionCheckMetrics(string toolName, PermissionMode mode, bool isAllowed)
        => _telemetryService?.RecordCount("guard.permission.check.count", new() { ["tool"] = toolName, ["mode"] = mode.ToString(), ["allowed"] = isAllowed.ToString() }, description: "Permission check count");
}
