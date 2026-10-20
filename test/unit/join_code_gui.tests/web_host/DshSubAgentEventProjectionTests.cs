namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// 子代理流事件投影测试（TASK058 B6）：引擎 AgentStarted/AgentFinished → DSH tool-workflow 事件族。
/// 契约（前端 workflow-run 节点）：run-start{name} 建卡片 → agent-start{seq,label,childId} 加成员 →
/// agent-end{seq,outcome} 定格 → run-end{stopReason} 收尾；seq 为轮内成员序号（start/end 按 AgentId 配对）。
/// </summary>
public sealed class DshSubAgentEventProjectionTests {
    /// <summary>轮内首个 agent-start 自动先发 run-start</summary>
    [Fact]
    public void FirstAgentStart_EmitsRunStartThenAgentStart() {
        var tracker = new DshAgentRunTracker(runId: "s1:2:agents");

        var events = tracker.OnAgentStarted(new ChatStreamEvent {
            Type = ChatStreamEventType.AgentStarted,
            AgentId = "agent-1",
            AgentName = "explore",
        });

        events.Should().HaveCount(2);
        events[0].Type.Should().Be("tool-workflow/run-start");
        events[0].Data.GetProperty("runId").GetString().Should().Be("s1:2:agents");
        events[0].Data.GetProperty("name").GetString().Should().NotBeNullOrEmpty();
        events[1].Type.Should().Be("tool-workflow/agent-start");
        events[1].Data.GetProperty("runId").GetString().Should().Be("s1:2:agents");
        events[1].Data.GetProperty("seq").GetInt32().Should().Be(1);
        events[1].Data.GetProperty("label").GetString().Should().Be("explore");
        events[1].Data.GetProperty("childId").GetString().Should().Be("agent-1");
    }

    [Fact]
    public void SubsequentAgentStart_SingleEvent_SequentialSeq() {
        var tracker = new DshAgentRunTracker("run");
        tracker.OnAgentStarted(AgentStarted("agent-1", "explore"));

        var events = tracker.OnAgentStarted(AgentStarted("agent-2", "coder"));

        events.Should().ContainSingle().Which.Type.Should().Be("tool-workflow/agent-start");
        events[0].Data.GetProperty("seq").GetInt32().Should().Be(2);
    }

    [Fact]
    public void AgentEnd_MatchesSeqByAgentId_OutcomeMapsSuccess() {
        var tracker = new DshAgentRunTracker("run");
        tracker.OnAgentStarted(AgentStarted("agent-1", "explore"));
        tracker.OnAgentStarted(AgentStarted("agent-2", "coder"));

        var events = tracker.OnAgentFinished(new ChatStreamEvent {
            Type = ChatStreamEventType.AgentFinished,
            AgentId = "agent-1",
            AgentSuccess = true,
        });

        events.Should().ContainSingle().Which.Type.Should().Be("tool-workflow/agent-end");
        events[0].Data.GetProperty("seq").GetInt32().Should().Be(1, "按 agent-1 的 start 序号配对");
        events[0].Data.GetProperty("outcome").GetString().Should().Be("completed");
    }

    [Fact]
    public void AgentEnd_Failure_OutcomeFailed() {
        var tracker = new DshAgentRunTracker("run");
        tracker.OnAgentStarted(AgentStarted("agent-1", "explore"));

        var events = tracker.OnAgentFinished(new ChatStreamEvent {
            Type = ChatStreamEventType.AgentFinished,
            AgentId = "agent-1",
            AgentSuccess = false,
        });

        events[0].Data.GetProperty("outcome").GetString().Should().Be("failed");
    }

    [Fact]
    public void AgentEnd_UnknownAgent_IgnoredNoEvent() {
        var tracker = new DshAgentRunTracker("run");

        tracker.OnAgentStarted(AgentStarted("agent-1", "explore"));
        var events = tracker.OnAgentFinished(new ChatStreamEvent {
            Type = ChatStreamEventType.AgentFinished,
            AgentId = "never-started",
        });

        events.Should().BeEmpty("未 start 的 agent end 无法配对，静默丢弃");
    }

    [Fact]
    public void RunEnd_WithoutAgents_NoEvent() {
        new DshAgentRunTracker("run").BuildRunEnd("completed").Should().BeNull();
    }

    [Fact]
    public void RunEnd_AfterAgents_CarriesRunIdAndStopReason() {
        var tracker = new DshAgentRunTracker("s1:9:agents");
        tracker.OnAgentStarted(AgentStarted("agent-1", "explore"));

        var end = tracker.BuildRunEnd("cancelled").Should().BeAssignableTo<DshAgentRunTracker.WorkflowEvent>().Subject;

        end.Type.Should().Be("tool-workflow/run-end");
        end.Data.GetProperty("runId").GetString().Should().Be("s1:9:agents");
        end.Data.GetProperty("stopReason").GetString().Should().Be("cancelled");
    }

    /// <summary>构造 AgentStarted 事件</summary>
    private static ChatStreamEvent AgentStarted(string agentId, string name) => new() {
        Type = ChatStreamEventType.AgentStarted,
        AgentId = agentId,
        AgentName = name,
    };
}
