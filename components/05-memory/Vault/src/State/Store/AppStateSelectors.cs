
namespace State;

/// <summary>
/// AppState 常用选择器
/// 提供类型安全的派生状态选择
/// </summary>
[Register]
public sealed partial class AppStateSelectors
{
    [Inject] private readonly IStore<AppState> _store;
    [Inject] private readonly ITelemetryService? _telemetryService;

    private void RecordSelectorMetrics(string selectorCategory, string selectorName)
        => _telemetryService?.RecordCount("vault.selector.count", new Dictionary<string, string> { ["category"] = selectorCategory, ["selector"] = selectorName }, "count", "State selector creation count");

    #region Session Selectors

    /// <summary>
    /// 选择当前会话 ID
    /// </summary>
    public IStoreSelector<AppState, string> SelectSessionId()
    {
        RecordSelectorMetrics("session", "sessionId");
        return _store.Select(state => state.Session.SessionId);
    }

    /// <summary>
    /// 选择系统提示词
    /// </summary>
    public IStoreSelector<AppState, string> SelectSystemPrompt()
    {
        RecordSelectorMetrics("session", "systemPrompt");
        return _store.Select(state => state.Session.SystemPrompt);
    }

    /// <summary>
    /// 选择聊天历史
    /// </summary>
    public IStoreSelector<AppState, ImmutableList<ApiMessageState>> SelectMessageList()
    {
        RecordSelectorMetrics("session", "chatHistory");
        return _store.Select(state => state.Session.MessageList);
    }

    /// <summary>
    /// 选择当前模型
    /// </summary>
    public IStoreSelector<AppState, string?> SelectCurrentModel()
    {
        RecordSelectorMetrics("session", "currentModel");
        return _store.Select(state => state.Session.CurrentModel);
    }

    /// <summary>
    /// 选择是否处于计划模式
    /// </summary>
    public IStoreSelector<AppState, bool> SelectIsPlanMode()
    {
        RecordSelectorMetrics("session", "isPlanMode");
        return _store.Select(state => state.Session.IsPlanMode);
    }

    #endregion

    #region Agent Selectors

    /// <summary>
    /// 选择所有 Agent
    /// </summary>
    public IStoreSelector<AppState, ImmutableDictionary<string, AgentState>> SelectAgents()
    {
        RecordSelectorMetrics("agent", "agents");
        return _store.Select(state => state.Agents);
    }

    /// <summary>
    /// 选择特定 Agent
    /// </summary>
    public IStoreSelector<AppState, AgentState?> SelectAgent(string agentId)
    {
        RecordSelectorMetrics("agent", "agent");
        return _store.Select(state => state.Agents.GetValueOrDefault(agentId));
    }

    /// <summary>
    /// 选择运行中的 Agent 数量
    /// </summary>
    public IStoreSelector<AppState, int> SelectRunningAgentCount()
    {
        RecordSelectorMetrics("agent", "runningAgentCount");
        return _store.Select(state => state.Agents.Count(a => a.Value.Status == AgentStatus.Running));
    }

    /// <summary>
    /// 选择活跃 Agent 列表
    /// </summary>
    public IStoreSelector<AppState, ImmutableList<AgentState>> SelectActiveAgents()
    {
        RecordSelectorMetrics("agent", "activeAgents");
        return _store.Select(state => state.Agents
            .Where(a => a.Value.Status != AgentStatus.Idle)
            .Select(a => a.Value)
            .ToImmutableList());
    }

    #endregion

    #region Task Selectors

    /// <summary>
    /// 选择所有任务
    /// </summary>
    public IStoreSelector<AppState, ImmutableDictionary<string, TaskState>> SelectTasks()
    {
        RecordSelectorMetrics("task", "tasks");
        return _store.Select(state => state.Tasks);
    }

    /// <summary>
    /// 选择特定任务
    /// </summary>
    public IStoreSelector<AppState, TaskState?> SelectTask(string taskId)
    {
        RecordSelectorMetrics("task", "task");
        return _store.Select(state => state.Tasks.GetValueOrDefault(taskId));
    }

    /// <summary>
    /// 选择运行中的任务
    /// </summary>
    public IStoreSelector<AppState, ImmutableList<TaskState>> SelectRunningTasks()
    {
        RecordSelectorMetrics("task", "runningTasks");
        return _store.Select(state => state.Tasks
            .Where(t => t.Value.Status == TaskExecutionStatus.Running)
            .Select(t => t.Value)
            .ToImmutableList());
    }

    /// <summary>
    /// 选择待处理任务数量
    /// </summary>
    public IStoreSelector<AppState, int> SelectPendingTaskCount()
    {
        RecordSelectorMetrics("task", "pendingTaskCount");
        return _store.Select(state => state.Tasks.Count(t => t.Value.Status == TaskExecutionStatus.Pending));
    }

    /// <summary>
    /// 选择已完成任务数量
    /// </summary>
    public IStoreSelector<AppState, int> SelectCompletedTaskCount()
    {
        RecordSelectorMetrics("task", "completedTaskCount");
        return _store.Select(state => state.Tasks.Count(t => t.Value.Status == TaskExecutionStatus.Completed));
    }

    #endregion

    #region Config Selectors

    /// <summary>
    /// 选择详细日志模式
    /// </summary>
    public IStoreSelector<AppState, bool> SelectVerboseMode()
    {
        RecordSelectorMetrics("config", "verboseMode");
        return _store.Select(state => state.Config.Verbose);
    }

    /// <summary>
    /// 选择简洁模式
    /// </summary>
    public IStoreSelector<AppState, bool> SelectBriefMode()
    {
        RecordSelectorMetrics("config", "briefMode");
        return _store.Select(state => state.Config.IsBriefMode);
    }

    /// <summary>
    /// 选择当前主题
    /// </summary>
    public IStoreSelector<AppState, string> SelectTheme()
    {
        RecordSelectorMetrics("config", "theme");
        return _store.Select(state => state.Config.Theme);
    }

    /// <summary>
    /// 选择令牌使用情况
    /// </summary>
    public IStoreSelector<AppState, (long? MaxBudget, long Used)> SelectTokenUsage()
    {
        RecordSelectorMetrics("config", "tokenUsage");
        return _store.Select(state => (state.Config.MaxTokenBudget, state.Config.UsedTokens));
    }

    #endregion

    #region UI Selectors

    /// <summary>
    /// 选择状态栏文本
    /// </summary>
    public IStoreSelector<AppState, string?> SelectStatusLineText()
    {
        RecordSelectorMetrics("ui", "statusLineText");
        return _store.Select(state => state.Ui.StatusLineText);
    }

    /// <summary>
    /// 选择是否加载中
    /// </summary>
    public IStoreSelector<AppState, bool> SelectIsLoading()
    {
        RecordSelectorMetrics("ui", "isLoading");
        return _store.Select(state => state.Ui.IsLoading);
    }

    /// <summary>
    /// 选择当前通知
    /// </summary>
    public IStoreSelector<AppState, NotificationState?> SelectCurrentNotification()
    {
        RecordSelectorMetrics("ui", "currentNotification");
        return _store.Select(state => state.Ui.CurrentNotification);
    }

    #endregion

    #region MCP Selectors

    /// <summary>
    /// 选择 MCP 服务器列表
    /// </summary>
    public IStoreSelector<AppState, ImmutableList<McpServerState>> SelectMcpServers()
    {
        RecordSelectorMetrics("mcp", "mcpServers");
        return _store.Select(state => state.Mcp.Servers);
    }

    /// <summary>
    /// 选择可用工具列表
    /// </summary>
    public IStoreSelector<AppState, ImmutableList<string>> SelectAvailableTools()
    {
        RecordSelectorMetrics("mcp", "availableTools");
        return _store.Select(state => state.Mcp.AvailableTools);
    }

    /// <summary>
    /// 选择已连接的 MCP 服务器数量
    /// </summary>
    public IStoreSelector<AppState, int> SelectConnectedMcpServerCount()
    {
        RecordSelectorMetrics("mcp", "connectedMcpServerCount");
        return _store.Select(state => state.Mcp.Servers.Count(s => s.Status == McpConnectionStatus.Connected));
    }

    #endregion

    #region Bridge Selectors

    /// <summary>
    /// 选择 Bridge 连接状态
    /// </summary>
    public IStoreSelector<AppState, bool> SelectBridgeConnected()
    {
        RecordSelectorMetrics("bridge", "bridgeConnected");
        return _store.Select(state => state.Bridge.IsConnected);
    }

    /// <summary>
    /// 选择 Bridge 是否启用
    /// </summary>
    public IStoreSelector<AppState, bool> SelectBridgeEnabled()
    {
        RecordSelectorMetrics("bridge", "bridgeEnabled");
        return _store.Select(state => state.Bridge.IsEnabled);
    }

    #endregion

    #region Permission Selectors

    /// <summary>
    /// 选择权限模式
    /// </summary>
    public IStoreSelector<AppState, PermissionMode> SelectPermissionMode()
    {
        RecordSelectorMetrics("permission", "permissionMode");
        return _store.Select(state => state.Permission.PermissionMode);
    }

    /// <summary>
    /// 选择待处理的权限请求
    /// </summary>
    public IStoreSelector<AppState, ImmutableList<PermissionRequestState>> SelectPendingPermissions()
    {
        RecordSelectorMetrics("permission", "pendingPermissions");
        return _store.Select(state => state.Permission.PendingRequests);
    }

    #endregion

    #region Combined Selectors

    /// <summary>
    /// 选择会话概览（组合多个字段）
    /// </summary>
    public IStoreSelector<AppState, SessionOverview> SelectSessionOverview()
    {
        RecordSelectorMetrics("combined", "sessionOverview");
        return _store.SelectByValue(state => new SessionOverview(
            state.Session.SessionId,
            state.Session.CurrentModel,
            state.Session.MessageList.Count,
            state.Session.IsPlanMode
        ), SessionOverviewComparer.Instance);
    }

    /// <summary>
    /// 选择工作负载概览
    /// </summary>
    public IStoreSelector<AppState, WorkloadOverview> SelectWorkloadOverview()
    {
        RecordSelectorMetrics("combined", "workloadOverview");
        return _store.SelectByValue(state => new WorkloadOverview(
            state.Agents.Count(a => a.Value.Status == AgentStatus.Running),
            state.Tasks.Count(t => t.Value.Status == TaskExecutionStatus.Running),
            state.Tasks.Count(t => t.Value.Status == TaskExecutionStatus.Pending)
        ), WorkloadOverviewComparer.Instance);
    }

    #endregion
}

/// <summary>
/// 会话概览记录
/// </summary>
public sealed record SessionOverview(
    string SessionId,
    string? CurrentModel,
    int MessageCount,
    bool IsPlanMode
);

/// <summary>
/// 工作负载概览记录
/// </summary>
public sealed record WorkloadOverview(
    int RunningAgents,
    int RunningTasks,
    int PendingTasks
);

/// <summary>
/// SessionOverview 相等比较器
/// </summary>
public sealed class SessionOverviewComparer : IEqualityComparer<SessionOverview>
{
    public static SessionOverviewComparer Instance { get; } = new();

    private SessionOverviewComparer() { }

    public bool Equals(SessionOverview? x, SessionOverview? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        return x.SessionId == y.SessionId &&
               x.CurrentModel == y.CurrentModel &&
               x.MessageCount == y.MessageCount &&
               x.IsPlanMode == y.IsPlanMode;
    }

    public int GetHashCode(SessionOverview obj)
    {
        return HashCode.Combine(obj.SessionId, obj.CurrentModel, obj.MessageCount, obj.IsPlanMode);
    }
}

/// <summary>
/// WorkloadOverview 相等比较器
/// </summary>
public sealed class WorkloadOverviewComparer : IEqualityComparer<WorkloadOverview>
{
    public static WorkloadOverviewComparer Instance { get; } = new();

    private WorkloadOverviewComparer() { }

    public bool Equals(WorkloadOverview? x, WorkloadOverview? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        return x.RunningAgents == y.RunningAgents &&
               x.RunningTasks == y.RunningTasks &&
               x.PendingTasks == y.PendingTasks;
    }

    public int GetHashCode(WorkloadOverview obj)
    {
        return HashCode.Combine(obj.RunningAgents, obj.RunningTasks, obj.PendingTasks);
    }
}
