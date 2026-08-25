namespace JoinCode.Gui.ViewModels;

/// <summary>
/// 多 subAgent 运行态聚合器 — 消费带 AgentId 的 ChatStreamEvent（IsSubAgentActivity），
/// 把引擎事件流归约为每 agent 一行的运行记录，供 AgentRunPanelView 绑定。
/// 纯 C# 无 UI 依赖，行为对齐 ClaudeCode：尾部 N 条活动 + 连续搜索/读取折叠 + 展开上限 LRU 驱逐
/// （展开管理语义移植自旧 TUI SubAgentCardManager）。
/// </summary>
public sealed class SubAgentRunTracker
{
    private readonly int _maxVisibleActivities;
    private readonly int _maxExpanded;

    /// <summary>agentId → 运行记录（保持插入顺序）</summary>
    private readonly Dictionary<string, SubAgentRun> _runs = new(StringComparer.Ordinal);

    /// <summary>展开状态 LRU：最早展开的排在最前</summary>
    private readonly LinkedList<string> _expandedOrder = new();
    private readonly HashSet<string> _expandedSet = new(StringComparer.Ordinal);

    /// <summary>连续搜索/读取类工具名 — 命中时折叠成计数摘要（对齐 ClaudeCode getSearchReadSummaryText）</summary>
    private static readonly FrozenSet<string> SearchReadTools = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "Grep", "Glob", "Read", "FileRead", "FileSearch", "Search", "LS", "List");

    public SubAgentRunTracker(int maxVisibleActivities = 3, int maxExpanded = 3)
    {
        _maxVisibleActivities = maxVisibleActivities;
        _maxExpanded = maxExpanded;
    }

    /// <summary>全部运行记录（含终态定格的）</summary>
    public IReadOnlyList<SubAgentRun> Runs => [.. _runs.Values];

    /// <summary>运行中的 agent 数（驱动全局状态条聚合）</summary>
    public int RunningCount => CountState(SubAgentRunState.Running);

    /// <summary>已完成 agent 数</summary>
    public int CompletedCount => CountState(SubAgentRunState.Completed);

    /// <summary>
    /// 消费一条子代理事件 — 未知 agentId 的活动事件静默忽略；
    /// 终态后的迟到事件不复活统计。返回被 LRU 驱逐的展开项 ID（null 表示无驱逐）。
    /// </summary>
    public string? Observe(ChatStreamEvent evt)
    {
        if (!evt.IsSubAgentActivity || evt.AgentId is null)
            return null;

        switch (evt.Type)
        {
            case ChatStreamEventType.AgentStarted:
                OnStarted(evt);
                return null;
            case ChatStreamEventType.AgentFinished:
                return OnFinished(evt);
            default:
                OnActivity(evt);
                return null;
        }
    }
    private void OnStarted(ChatStreamEvent evt)
    {
        if (_runs.TryGetValue(evt.AgentId!, out var existing))
        {
            // 重复 Started（重试等）— 刷新元数据但不重置状态
            if (evt.AgentName is not null)
                existing.Name = evt.AgentName;
            if (evt.AgentDescription is not null)
                existing.Description = evt.AgentDescription;
            return;
        }

        _runs[evt.AgentId!] = new SubAgentRun
        {
            AgentId = evt.AgentId!,
            Name = evt.AgentName ?? evt.AgentRole ?? "agent",
            Description = evt.AgentDescription ?? string.Empty,
            Role = evt.AgentRole ?? string.Empty
        };
    }

    private void OnActivity(ChatStreamEvent evt)
    {
        if (!_runs.TryGetValue(evt.AgentId!, out var run) || run.State != SubAgentRunState.Running)
            return;

        switch (evt.Type)
        {
            case ChatStreamEventType.ToolCallStart when !string.IsNullOrEmpty(evt.ToolName):
                var calling = $"正在调用 {evt.ToolName}…";
                AppendCollapsed(run, calling);
                run.LastActivityText = calling;
                break;

            case ChatStreamEventType.ToolCallEnd when !string.IsNullOrEmpty(evt.ToolName):
                run.ToolUseCount++;
                string endText;
                if (SearchReadTools.Contains(evt.ToolName))
                {
                    // 连续第 2 次起折叠成计数摘要（对齐 ClaudeCode：单次不折叠，保留工具名可读性）
                    run.SearchReadStreak++;
                    endText = run.SearchReadStreak >= 2
                        ? $"搜索/读取 {run.SearchReadStreak} 次…"
                        : $"✓ {evt.ToolName}";
                }
                else
                {
                    run.SearchReadStreak = 0;
                    endText = evt.IsToolError ? $"✗ {evt.ToolName}" : $"✓ {evt.ToolName}";
                }

                AppendCollapsed(run, endText);
                run.LastActivityText = endText;
                break;

            case ChatStreamEventType.Content when !string.IsNullOrWhiteSpace(evt.Content):
                var content = evt.Content.Length > 80 ? evt.Content[..80] + "…" : evt.Content;
                AppendCollapsed(run, content);
                run.LastActivityText = content;
                break;

            case ChatStreamEventType.ToolProgress when !string.IsNullOrEmpty(evt.ProgressMessage):
                AppendCollapsed(run, evt.ProgressMessage);
                run.LastActivityText = evt.ProgressMessage;
                break;
        }
    }

    private string? OnFinished(ChatStreamEvent evt)
    {
        if (!_runs.TryGetValue(evt.AgentId!, out var run))
            return null;

        // 终态定格 — 统计冻结，后续迟到事件被 Running 状态检查自然丢弃；
        // 不强制收起展开位（用户可能正查看该 agent 的回放入口）
        run.State = evt.AgentSuccess == true ? SubAgentRunState.Completed : SubAgentRunState.Failed;
        run.IsSuccess = evt.AgentSuccess == true;
        run.ExecutionTimeMs = evt.AgentExecutionTimeMs;
        run.FinalOutput = evt.Content;
        return null;
    }

    private void AppendCollapsed(SubAgentRun run, string text)
    {
        run._visibleActivities.Add(text);
        if (run._visibleActivities.Count > _maxVisibleActivities)
        {
            run._visibleActivities.RemoveAt(0);
            run.HiddenActivityCount++;
        }
    }

    // === 展开管理（LRU 驱逐，语义移植自旧 TUI SubAgentCardManager） ===

    /// <summary>当前展开的 agent ID 列表（按展开时间排序）</summary>
    public IReadOnlyList<string> Expanded => [.. _expandedOrder];

    /// <summary>指定 agent 是否已展开</summary>
    public bool IsExpanded(string agentId)
    {
        lock (_expandedOrder)
        {
            return _expandedSet.Contains(agentId);
        }
    }

    /// <summary>展开 agent — 超过上限时自动折叠最早展开的，返回被驱逐者 ID（null 表示无驱逐）</summary>
    public string? Expand(string agentId)
    {
        lock (_expandedOrder)
        {
            if (!_expandedSet.Add(agentId))
                return null;

            _expandedOrder.AddLast(agentId);

            if (_expandedOrder.Count <= _maxExpanded)
                return null;

            var evicted = _expandedOrder.First!.Value;
            _expandedOrder.RemoveFirst();
            _expandedSet.Remove(evicted);
            return evicted;
        }
    }

    /// <summary>折叠 agent（false 表示原本未展开）</summary>
    public bool Collapse(string agentId)
    {
        lock (_expandedOrder)
        {
            if (!_expandedSet.Remove(agentId))
                return false;
            var node = _expandedOrder.Find(agentId);
            if (node is not null)
                _expandedOrder.Remove(node);
            return true;
        }
    }

    private int CountState(SubAgentRunState state) => _runs.Values.Count(r => r.State == state);
}
