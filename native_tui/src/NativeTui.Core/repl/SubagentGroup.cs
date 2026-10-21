namespace NativeTui.Core.Repl;

/// <summary>子代理工具活动记录（交互层 Step2）— 活动日志环形缓冲的单元（展开态逐行渲染）。</summary>
/// <param name="ToolName">工具名（活动行前缀）。</param>
/// <param name="Summary">活动摘要（活动行正文）。</param>
public sealed record SubagentActivity(string ToolName, string Summary);

/// <summary>
/// 子代理条目（M7-7）— 一个并行派发子代理的渲染状态投影（不可变 record，Coordinator 替换式更新）：
/// 身份（Id/AgentType/Description）+ 进度累计（ToolUseCount/TokenCount/LastToolName/LastToolSummary）+
/// 活动日志（RecentActivities 最近 3 条环形 + HiddenActivityCount 丢弃计数 — 展开态逐行渲染，交互层 Step2）+
/// 终态（IsDone/IsError）。对齐基准 AgentProgressLine 行数据：状态行 "Type (desc) · N tool uses · M tokens"
/// + 活动行 "lastToolName: summary | Initializing… | Done"。
/// </summary>
/// <param name="Id">子代理标识（引擎侧生成，事件路由键）。</param>
/// <param name="AgentType">代理类型（组头同型拼接 + 行内八色取色键）。</param>
/// <param name="Description">任务描述（状态行括号段；全同型组内提升为行首）。</param>
/// <param name="ToolUseCount">子代理内工具使用次数（RecordToolUse 累计）。</param>
/// <param name="TokenCount">令牌消耗累计（TokenDelta 逐次累加）。</param>
/// <param name="LastToolName">最后工具名（活动行前缀；空 = 未开始 → Initializing…）。</param>
/// <param name="LastToolSummary">最后工具摘要（活动行正文）。</param>
/// <param name="IsDone">是否定格（FinishAgent 或回合收尾 FinalizePending）。</param>
/// <param name="IsError">是否失败（组头 ● Error 红聚合）。</param>
/// <param name="RecentActivities">最近工具活动日志（环形保留最近 3 条 — 展开态逐行渲染）。</param>
/// <param name="HiddenActivityCount">环形丢弃的更早活动数（展开态 "+N more tool uses" 行依据）。</param>
public sealed record SubagentEntry(
    string Id,
    string AgentType,
    string Description,
    int ToolUseCount,
    int TokenCount,
    string LastToolName,
    string LastToolSummary,
    bool IsDone,
    bool IsError,
    IReadOnlyList<SubagentActivity> RecentActivities,
    int HiddenActivityCount);

/// <summary>
/// 子代理分组协调器（M7-7）— 对齐基准 AgentTool 分组渲染语义（groupToolUses + AgentProgressLine）：
/// 事件驱动状态机（AddAgent 追加未完成条目 / RecordToolUse 累计进度 / FinishAgent 定格 /
/// FinalizePending 回合收尾把未完成条目定格为非错误）+ 组级聚合（AnyUnresolved/AnyError/AllComplete）+
/// 组头文案派生（Running N {type} agents… / N {type} agents finished，全同型带类型名）+
/// 可见窗口 min(10, n) + 活动跟随滚动（最新活动条目滚入窗口 — 区别于模型选择器的焦点跟随，分组非模态无焦点）。
/// 与基准的有意偏离：基准组内行数无截断靠 React 虚拟化扛 300 规模，本项目无虚拟化（inline 全量屏），
/// 窗口 + "and N more…" 溢出是对等机制（方案 A 用户确认）。消费方：ReplBridgeDriver 路由 + SubagentGroupItem 渲染投影。
/// </summary>
public sealed class SubagentGroupCoordinator
{
    /// <summary>最大可见条数（对齐模型选择器 visibleOptionCount=Math.min(10, n) 先例；基准组内无截断靠虚拟化，本项目窗口为对等机制）。</summary>
    public const int MaxVisible = 10;

    /// <summary>活动日志环形容量（交互层 Step2 — 基准单代理折叠尾部窗口 3 条语义：最近 3 条 + "+N more"）。</summary>
    public const int MaxRecentActivities = 3;

    private readonly List<SubagentEntry> _agents = [];

    /// <summary>条目清单（渲染与断言投影）。</summary>
    public IReadOnlyList<SubagentEntry> Agents => _agents;

    /// <summary>条目总数。</summary>
    public int Count => _agents.Count;

    /// <summary>展开态（消息光标 Enter 切换 — 交互层 Step1）— true = 全量渲染突破 min(10,n) 窗口
    /// （对齐基准 Enter 展开 grouped_tool_use → verbose 不分组 → 逐代理独立详细渲染）；
    /// 收起时窗口归零回到组头视角（后续事件活动跟随自动拉回）。</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            _isExpanded = value;
            WindowStart = 0;
        }
    }

    private bool _isExpanded;

    /// <summary>可见条数 = 展开全量，折叠 min(10, n)。</summary>
    public int VisibleCount => _isExpanded ? _agents.Count : Math.Min(MaxVisible, _agents.Count);

    /// <summary>折叠条数 = 展开恒 0，折叠 max(0, n - 10)（"and N more…" 行依据）。</summary>
    public int HiddenCount => _isExpanded ? 0 : Math.Max(0, _agents.Count - VisibleCount);

    /// <summary>滚动窗口起点（0 起，活动跟随保证最新活动条目可见）。</summary>
    public int WindowStart { get; private set; }

    /// <summary>有未完成条目（组头 Running 态依据）。</summary>
    public bool AnyUnresolved => _agents.Exists(static agent => !agent.IsDone);

    /// <summary>任一条目失败（组头 ● Error 红依据 — 基准 anyError loader 变红）。</summary>
    public bool AnyError => _agents.Exists(static agent => agent.IsError);

    /// <summary>全部完成（组头 finished 态依据）。</summary>
    public bool AllComplete => _agents.Count > 0 && _agents.TrueForAll(static agent => agent.IsDone);

    /// <summary>全组同类型（组头带类型名 + 行内隐藏类型 — 基准 allSameType/commonType）。</summary>
    public bool AllSameType => _agents.Count > 0 && _agents.TrueForAll(agent => agent.AgentType == _agents[0].AgentType);

    /// <summary>组头文案 — 运行中 "Running N {type} agents…" / 完成 "N {type} agents finished"（全同型带类型名）。</summary>
    public string HeaderText =>
        (AllComplete ? $"{Count} " : $"Running {Count} ") + (AllSameType ? $"{_agents[0].AgentType} " : "") + "agents" + (AllComplete ? " finished" : "…");

    /// <summary>追加子代理条目（未完成态）+ 窗口滚动到该条目（活动跟随）。</summary>
    public void AddAgent(string id, string agentType, string description)
    {
        _agents.Add(new SubagentEntry(id, agentType, description, ToolUseCount: 0, TokenCount: 0,
            LastToolName: string.Empty, LastToolSummary: string.Empty, IsDone: false, IsError: false,
            RecentActivities: [], HiddenActivityCount: 0));
        EnsureVisible(_agents.Count - 1);
    }

    /// <summary>记录子代理工具活动 — 计数/令牌累计 + 最后活动更新 + 活动日志环形追加（超容量丢最旧并计数）+ 窗口跟随。</summary>
    public void RecordToolUse(string id, string toolName, string summary, int tokenDelta)
    {
        UpdateEntry(id, entry =>
        {
            var activities = entry.RecentActivities.Append(new SubagentActivity(toolName, summary)).ToList();
            var hidden = entry.HiddenActivityCount;
            while (activities.Count > MaxRecentActivities)
            {
                activities.RemoveAt(0);
                hidden++;
            }

            return entry with
            {
                ToolUseCount = entry.ToolUseCount + 1,
                TokenCount = entry.TokenCount + tokenDelta,
                LastToolName = toolName,
                LastToolSummary = summary,
                RecentActivities = activities,
                HiddenActivityCount = hidden,
            };
        });
    }

    /// <summary>定格子代理条目（isError 聚合组头 ● 红）+ token 权威校正 + 窗口跟随。
    /// tokenTotal &gt; 0 时覆盖 TokenCount（AgentFinished 事件 Usage.TotalTokens — 引擎完成态权威值，
    /// 优于逐活动增量累加）；0 = 无 usage 数据（FinalizePending 兜底路径）保持原累计。</summary>
    public void FinishAgent(string id, bool isError, int tokenTotal = 0)
    {
        UpdateEntry(id, entry => entry with
        {
            IsDone = true,
            IsError = isError,
            TokenCount = tokenTotal > 0 ? tokenTotal : entry.TokenCount,
        });
    }

    /// <summary>回合收尾 — 未完成条目定格为非错误完成（TurnCompleted/TurnInterrupted 调用；幂等 — 全完成 no-op）。</summary>
    public void FinalizePending()
    {
        for (var i = 0; i < _agents.Count; i++)
        {
            if (!_agents[i].IsDone)
            {
                _agents[i] = _agents[i] with { IsDone = true };
            }
        }
    }

    /// <summary>替换式更新条目（定位 + with 投影）+ 窗口跟随。</summary>
    private void UpdateEntry(string id, Func<SubagentEntry, SubagentEntry> update)
    {
        var index = _agents.FindIndex(entry => entry.Id == id);
        if (index < 0)
        {
            throw new KeyNotFoundException($"子代理条目不存在：{id}（Driver 保证 SubagentStarted 先于进度/完成事件 — 开心路径断言）。");
        }

        _agents[index] = update(_agents[index]);
        EnsureVisible(index);
    }

    /// <summary>窗口跟随 — 活动条目滚入可见窗口（上边界推进；展开态全量可见 no-op）。</summary>
    private void EnsureVisible(int index)
    {
        if (_isExpanded)
        {
            return;
        }

        if (index < WindowStart)
        {
            WindowStart = index;
        }
        else if (index >= WindowStart + VisibleCount)
        {
            WindowStart = index - VisibleCount + 1;
        }
    }
}
