namespace JoinCode.Abs.Tests.LLM.Chat;

/// <summary>
/// ChatStreamEvent 子代理身份扩展测试 — AgentStarted/AgentFinished 事件与 AgentId 路由字段。
/// 对齐 GUI 多 subAgent 运行期显示需求：子代理活动事件必须携带身份，主对话按 AgentId 分组路由。
/// </summary>
public class ChatStreamEventAgentTests
{
    private const string TestAgentId = "agent-001";

    [Fact]
    public void AgentStartedEvent_ShouldCarryIdentity()
    {
        var evt = ChatStreamEvent.AgentStarted(TestAgentId, "explore", "搜索现有实现模式", "researcher");

        evt.Type.Should().Be(ChatStreamEventType.AgentStarted);
        evt.AgentId.Should().Be(TestAgentId);
        evt.AgentName.Should().Be("explore");
        evt.AgentDescription.Should().Be("搜索现有实现模式");
        evt.AgentRole.Should().Be("researcher");
    }

    [Fact]
    public void AgentFinishedEvent_ShouldCarryStatistics()
    {
        var usage = new TokenUsage(100, 200);

        var evt = ChatStreamEvent.AgentFinished(
            TestAgentId, success: true, executionTimeMs: 132_000, usage: usage, finalOutput: "调研完成");

        evt.Type.Should().Be(ChatStreamEventType.AgentFinished);
        evt.AgentId.Should().Be(TestAgentId);
        evt.AgentSuccess.Should().BeTrue();
        evt.AgentExecutionTimeMs.Should().Be(132_000);
        evt.Usage.Should().BeSameAs(usage);
        evt.Content.Should().Be("调研完成");
    }

    [Fact]
    public void AgentFinishedEvent_WhenFailed_ShouldCarryErrorMessage()
    {
        var evt = ChatStreamEvent.AgentFinished(
            TestAgentId, success: false, executionTimeMs: 5_000, usage: null, finalOutput: "boom");

        evt.Type.Should().Be(ChatStreamEventType.AgentFinished);
        evt.AgentSuccess.Should().BeFalse();
        evt.Content.Should().Be("boom");
    }

    [Fact]
    public void ActivityEvent_WithAgentId_ShouldRouteToSubAgent()
    {
        // 子代理中间活动复用现有事件类型 + AgentId 标记（对齐 TS onProgress 附着 toolUseID 模式）
        var evt = new ChatStreamEvent
        {
            Type = ChatStreamEventType.ToolCallStart,
            ToolName = "FileRead",
            ToolCallId = "call_1",
            AgentId = TestAgentId
        };

        evt.AgentId.Should().Be(TestAgentId);
        evt.IsSubAgentActivity.Should().BeTrue();
    }

    [Fact]
    public void MainConversationEvent_WithoutAgentId_ShouldNotBeSubAgentActivity()
    {
        ChatStreamEvent.Text("hello").IsSubAgentActivity.Should().BeFalse();
    }

    [Fact]
    public void Switch_WithAgentCallbacks_ShouldDispatchAgentEvents()
    {
        var started = false;
        var finished = false;

        ChatStreamEvent.AgentStarted(TestAgentId, "explore", "d", "r").Switch(
            onText: _ => throw new InvalidOperationException("不应路由到文本"),
            onThinking: _ => throw new InvalidOperationException(),
            onToolStart: (_, _, _) => throw new InvalidOperationException(),
            onToolEnd: (_, _, _, _, _) => throw new InvalidOperationException(),
            onToolProgress: (_, _, _) => throw new InvalidOperationException(),
            onLoopDetected: (_, _, _) => throw new InvalidOperationException(),
            onTimingSummary: _ => throw new InvalidOperationException(),
            onDone: (_, _) => throw new InvalidOperationException(),
            onAgentStarted: _ => started = true,
            onAgentFinished: (_, _) => finished = false);

        started.Should().BeTrue();

        ChatStreamEvent.AgentFinished(TestAgentId, true, 1, null, null).Switch(
            onText: _ => throw new InvalidOperationException(),
            onThinking: _ => throw new InvalidOperationException(),
            onToolStart: (_, _, _) => throw new InvalidOperationException(),
            onToolEnd: (_, _, _, _, _) => throw new InvalidOperationException(),
            onToolProgress: (_, _, _) => throw new InvalidOperationException(),
            onLoopDetected: (_, _, _) => throw new InvalidOperationException(),
            onTimingSummary: _ => throw new InvalidOperationException(),
            onDone: (_, _) => throw new InvalidOperationException(),
            onAgentStarted: _ => { },
            onAgentFinished: (_, _) => finished = true);

        finished.Should().BeTrue();
    }

    [Fact]
    public void Switch_WithoutAgentCallbacks_ShouldIgnoreAgentEventsCompatibly()
    {
        // 现有消费方（AskClarifyCommand/SessionController）未传 agent 回调时不得抛异常 — 兼容性铁律
        var record = new System.Collections.Generic.List<string>();

        ChatStreamEvent.AgentStarted(TestAgentId, "explore", "d", "r").Switch(
            onText: record.Add,
            onThinking: _ => record.Add("thinking"),
            onToolStart: (n, _, _) => record.Add(n),
            onToolEnd: (n, _, _, _, _) => record.Add(n),
            onToolProgress: (n, _, _) => record.Add(n),
            onLoopDetected: (_, _, _) => record.Add("loop"),
            onTimingSummary: record.Add,
            onDone: (_, _) => record.Add("done"));

        record.Should().BeEmpty();
    }

    [Fact]
    public void Match_WithAgentCallbacks_ShouldReturnMappedValue()
    {
        var result = ChatStreamEvent.AgentStarted(TestAgentId, "explore", "d", "r").Match(
            onText: _ => "text",
            onThinking: _ => "thinking",
            onToolStart: (_, _, _) => "toolStart",
            onToolEnd: (_, _, _, _, _) => "toolEnd",
            onToolProgress: (_, _, _) => "toolProgress",
            onLoopDetected: (_, _, _) => "loop",
            onTimingSummary: _ => "timing",
            onDone: (_, _) => "done",
            onAgentStarted: _ => "agentStarted",
            onAgentFinished: (_, _) => "agentFinished");

        result.Should().Be("agentStarted");
    }
}
