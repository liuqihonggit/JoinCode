namespace Core.Utils;

[Register]
public sealed partial class AgentToolRestrictions : ServiceEntity, IAgentToolRestrictions
{
    private readonly FrozenSet<string> _autoAllowed;
    private readonly FrozenSet<string> _planAllowed;
    private readonly FrozenSet<string> _askAllowed;
    private readonly FrozenSet<string> _autoDenied;
    private readonly FrozenSet<string> _planDenied;
    private readonly FrozenSet<string> _askDenied;

    public AgentToolRestrictions(
        IOptions<PermissionConfig>? configOptions = null,
        ITelemetryService? telemetryService = null)
    {
        _telemetryService = telemetryService;
        var overrides = configOptions?.Value?.ToolOverrides;
        _autoAllowed = MergeWithOverrides(AutoAllowedToolsDefault, overrides, "auto", allow: true);
        _planAllowed = MergeWithOverrides(PlanAllowedToolsDefault, overrides, "plan", allow: true);
        _askAllowed = MergeWithOverrides(AskAllowedToolsDefault, overrides, "ask", allow: true);
        _autoDenied = MergeWithOverrides(AutoDeniedToolsDefault, overrides, "auto", allow: false);
        _planDenied = MergeWithOverrides(PlanDeniedToolsDefault, overrides, "plan", allow: false);
        _askDenied = MergeWithOverrides(AskDeniedToolsDefault, overrides, "ask", allow: false);
    }

    [Inject] private readonly ITelemetryService? _telemetryService;

    private static readonly FrozenSet<string> AutoAllowedToolsDefault = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        FileToolNameConstants.FileRead, FileToolNameConstants.DirectoryList, SearchToolNameConstants.Glob, SearchToolNameConstants.Grep,
        SearchToolNameConstants.SearchCode, SearchToolNameConstants.SearchText,
        SearchToolNameConstants.SearchFiles, SearchToolNameConstants.SearchCodebase,
        SearchToolNameConstants.CodeSearch, SearchToolNameConstants.SymbolSearch,
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

    private static readonly FrozenSet<string> PlanAllowedToolsDefault = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        FileToolNameConstants.FileRead, FileToolNameConstants.DirectoryList, SearchToolNameConstants.Glob, SearchToolNameConstants.Grep,
        SearchToolNameConstants.SearchCode, SearchToolNameConstants.SearchText,
        SearchToolNameConstants.SearchFiles, SearchToolNameConstants.SearchCodebase,
        SearchToolNameConstants.CodeSearch, SearchToolNameConstants.SymbolSearch,
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

    private static readonly FrozenSet<string> AskAllowedToolsDefault = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        FileToolNameConstants.FileRead, FileToolNameConstants.DirectoryList, SearchToolNameConstants.Glob, SearchToolNameConstants.Grep,
        SearchToolNameConstants.SearchCode, SearchToolNameConstants.SearchText,
        SearchToolNameConstants.SearchFiles, SearchToolNameConstants.SearchCodebase,
        SearchToolNameConstants.CodeSearch, SearchToolNameConstants.SymbolSearch,
        WebToolNameConstants.WebFetch, WebToolNameConstants.WebSearch,
        TaskToolNameConstants.TaskList, TaskToolNameConstants.TaskGet,
        SystemToolNameConstants.TaskOutput,
        TodoToolNameConstants.TodoList,
        FileToolNameConstants.FileWrite, FileToolNameConstants.FileEdit,
        ShellToolNameConstants.Bash, ShellToolNameConstants.Powershell,
        CodeToolNameConstants.CodeIndexSearch, CodeToolNameConstants.CodeIndexSearchComprehensive,
        CodeToolNameConstants.CodeIndexFindDefinition,
        // Ask 模式允许所有 MCP 工具（最宽松）
        McpToolNameConstants.McpConnect, McpToolNameConstants.McpDisconnect,
        McpToolNameConstants.McpListTools, McpToolNameConstants.McpCallTool,
        McpToolNameConstants.McpListResources, McpToolNameConstants.McpReadResource,
        McpToolNameConstants.McpListPrompts, McpToolNameConstants.McpGetPrompt,
        McpToolNameConstants.McpListServers, McpToolNameConstants.McpListClients
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> AutoDeniedToolsDefault = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ShellToolNameConstants.Bash, ShellToolNameConstants.Powershell,
        FileToolNameConstants.FileDelete,
        GitToolNameConstants.GitCommit, GitToolNameConstants.GitPush
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> PlanDeniedToolsDefault = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        FileToolNameConstants.FileWrite, FileToolNameConstants.FileEdit,
        FileToolNameConstants.FileDelete,
        ShellToolNameConstants.Bash, ShellToolNameConstants.Powershell,
        GitToolNameConstants.GitCommit, GitToolNameConstants.GitPush
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> AskDeniedToolsDefault = FrozenSet<string>.Empty;

    public IReadOnlySet<string> GetAllowedTools(PermissionMode mode)
    {
        return mode switch
        {
            PermissionMode.Auto => _autoAllowed,
            PermissionMode.Plan => _planAllowed,
            PermissionMode.Ask => _askAllowed,
            PermissionMode.Bypass => _autoAllowed,
            _ => _autoAllowed
        };
    }

    public IReadOnlySet<string> GetDeniedTools(PermissionMode mode)
    {
        return mode switch
        {
            PermissionMode.Auto => _autoDenied,
            PermissionMode.Plan => _planDenied,
            PermissionMode.Ask => _askDenied,
            PermissionMode.Bypass => FrozenSet<string>.Empty,
            _ => _autoDenied
        };
    }

    public bool IsToolAllowedForMode(string toolName, PermissionMode mode)
    {
        if (mode == PermissionMode.Bypass)
        {
            RecordPermissionCheckMetrics(toolName, mode, true);
            return true;
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
            RecordPermissionCheckMetrics(toolName, mode, true);
            return true;
        }

        var allowedResult = allowed.Contains(toolName);
        RecordPermissionCheckMetrics(toolName, mode, allowedResult);
        return allowedResult;
    }

    private void RecordPermissionCheckMetrics(string toolName, PermissionMode mode, bool isAllowed)
        => _telemetryService?.RecordCount("guard.permission.check.count", new() { ["tool"] = toolName, ["mode"] = mode.ToString(), ["allowed"] = isAllowed.ToString() }, description: "Permission check count");

    private static FrozenSet<string> MergeWithOverrides(
        FrozenSet<string> defaults,
        Dictionary<string, ToolOverrideEntry>? overrides,
        string modeKey,
        bool allow)
    {
        if (overrides is null || !overrides.TryGetValue(modeKey, out var entry))
            return defaults;

        var list = allow ? entry.Allow : entry.Deny;
        if (list is null or { Count: 0 })
            return defaults;

        var merged = new HashSet<string>(defaults, StringComparer.OrdinalIgnoreCase);
        merged.UnionWith(list);
        return merged.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }
}
