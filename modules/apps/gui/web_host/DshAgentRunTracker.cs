namespace JoinCode.Gui.WebHost;

/// <summary>
/// 子代理轮内运行追踪器（TASK058 B6，翻译单点）：把引擎 <c>AgentStarted/AgentFinished</c> 流事件
/// 投影为 DSH <c>tool-workflow</c> 事件族（前端 workflow-run 节点渲染子代理卡片）。
/// 每轮一个合成 run（RunId 由调用方给出）；首个成员自动补发 run-start；
/// start/end 按 AgentId 配对轮内序号；未 start 的 end 静默丢弃。
/// 值域契约：outcome = completed/cancelled/failed；stopReason = completed/cancelled/error。
/// </summary>
internal sealed class DshAgentRunTracker(string runId) {
    /// <summary>发布事件（type + data 树）</summary>
    internal sealed record WorkflowEvent(string Type, JsonElement Data);

    private readonly Dictionary<string, int> _seqByAgent = new(StringComparer.Ordinal);
    private int _nextSeq;
    private bool _runStarted;

    /// <summary>是否已发出过 run-start（有成员加入即视为有 run）</summary>
    public bool RunStarted => _runStarted;

    /// <summary>处理一个 AgentStarted：首个自动先发 run-start，再发 agent-start（轮内序号 1 起）</summary>
    internal List<WorkflowEvent> OnAgentStarted(ChatStreamEvent evt) {
        if (string.IsNullOrEmpty(evt.AgentId)) {
            return [];
        }
        var events = new List<WorkflowEvent>();
        if (!_runStarted) {
            _runStarted = true;
            events.Add(new WorkflowEvent("tool-workflow/run-start", Serialize(new DshWorkflowRunStartData {
                RunId = runId,
                Name = "subagents",
            })));
        }
        var seq = ++_nextSeq;
        _seqByAgent[evt.AgentId] = seq;
        events.Add(new WorkflowEvent("tool-workflow/agent-start", Serialize(new DshWorkflowAgentStartData {
            RunId = runId,
            Seq = seq,
            Label = string.IsNullOrEmpty(evt.AgentName) ? evt.AgentId : evt.AgentName,
            ChildId = evt.AgentId,
        })));
        return events;
    }

    /// <summary>处理一个 AgentFinished：按 AgentId 配对序号发 agent-end（成功→completed，失败/未知→failed）；未 start 的丢弃</summary>
    internal List<WorkflowEvent> OnAgentFinished(ChatStreamEvent evt) {
        if (string.IsNullOrEmpty(evt.AgentId) || !_seqByAgent.TryGetValue(evt.AgentId, out var seq)) {
            return [];
        }
        return [
            new WorkflowEvent("tool-workflow/agent-end", Serialize(new DshWorkflowAgentEndData {
                RunId = runId,
                Seq = seq,
                Outcome = evt.AgentSuccess is true ? "completed" : "failed",
            })),
        ];
    }

    /// <summary>轮次收尾：无成员返回 null（不发空 run）；stopReason 值域 completed/cancelled/error</summary>
    internal WorkflowEvent? BuildRunEnd(string stopReason) =>
        _runStarted
            ? new WorkflowEvent("tool-workflow/run-end", Serialize(new DshWorkflowRunEndData {
                RunId = runId,
                StopReason = stopReason,
            }))
            : null;

    /// <summary>DTO → data 树（与 DshSessionStreamHub.Allocate 同款 GetTypeInfo 查表）</summary>
    private static JsonElement Serialize<T>(T data) =>
        JsonSerializer.SerializeToElement(data, DshJsonContext.Default.GetTypeInfo(typeof(T)));
}
