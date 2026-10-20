namespace NativeTui.Host.Tests;

/// <summary>
/// InProcessEventConsumer 翻译测试 — IEventConsumer 回调（回合线程）→ 内存 BridgeEvent record
/// 进 Channel（Actor 邮箱模型：回合线程生产，主循环 tick 排空消费，无共享锁）。
/// 语义对齐 engine BridgeEventConsumer（唯一差异：WriteEvent 序列化 stdout → Channel 写内存对象）：
/// OnText → token_delta；OnThinking 单次信号 + 墙钟差结算 thinking_ended；OnToolStart/End →
/// tool_call/tool_result（摘要截断 200 + error 前缀）；OnDone → assistant_message(全文)；
/// Reset 清回合内状态（跨回合不重复发 thinking_started）。
/// </summary>
public sealed class InProcessEventConsumerTests
{
    [Fact]
    public void OnText_EmitsTokenDeltaPerCall()
    {
        var consumer = new InProcessEventConsumer();

        consumer.OnText("你好");
        consumer.OnText("，世界");

        var events = consumer.DrainEvents();
        Assert.Equal(2, events.Count);
        var delta1 = Assert.IsType<BridgeEvent.TokenDelta>(events[0]);
        var delta2 = Assert.IsType<BridgeEvent.TokenDelta>(events[1]);
        Assert.Equal(2, delta1.Length);
        Assert.Equal(3, delta2.Length);
        Assert.Empty(consumer.DrainEvents());
    }

    /// <summary>子代理三回调映射（DSG041 批4）— OnSubagentStarted/OnSubagentToolUse/OnSubagentFinished
    /// → SubagentStarted/SubagentToolUse/SubagentFinished 内存事件（ReplBridgeDriver 路由既有，零改动验收面）。</summary>
    [Fact]
    public void SubagentCallbacks_EnqueueSubagentEvents()
    {
        var consumer = new InProcessEventConsumer();

        consumer.OnSubagentStarted("agent-1", "explore", "搜索渲染链");
        consumer.OnSubagentToolUse("agent-1", "Grep", "命中 3 处", 120);
        consumer.OnSubagentFinished("agent-1", isError: false);

        var events = consumer.DrainEvents();
        Assert.Equal(3, events.Count);
        var started = Assert.IsType<BridgeEvent.SubagentStarted>(events[0]);
        Assert.Equal("agent-1", started.SubagentId);
        Assert.Equal("explore", started.AgentType);
        Assert.Equal("搜索渲染链", started.Description);
        var toolUse = Assert.IsType<BridgeEvent.SubagentToolUse>(events[1]);
        Assert.Equal("agent-1", toolUse.SubagentId);
        Assert.Equal("Grep", toolUse.ToolName);
        Assert.Equal("命中 3 处", toolUse.Summary);
        Assert.Equal(120, toolUse.TokenGain);
        var finished = Assert.IsType<BridgeEvent.SubagentFinished>(events[2]);
        Assert.Equal("agent-1", finished.SubagentId);
        Assert.False(finished.IsError);
    }

    [Fact]
    public void OnThinking_SignalsOncePerTurn()
    {
        var consumer = new InProcessEventConsumer();

        consumer.OnThinking("第一步推理");
        consumer.OnThinking("继续推理");

        var events = consumer.DrainEvents();
        var started = Assert.Single(events);
        Assert.IsType<BridgeEvent.ThinkingStarted>(started);
    }

    [Fact]
    public void OnText_AfterThinking_EndsThinkingWithWallClockDelta()
    {
        var consumer = new InProcessEventConsumer(static () => 1_000);

        consumer.OnThinking("思考");
        consumer.OnText("产出");

        var events = consumer.DrainEvents();
        Assert.Equal(3, events.Count);
        Assert.IsType<BridgeEvent.ThinkingStarted>(events[0]);
        var ended = Assert.IsType<BridgeEvent.ThinkingEnded>(events[1]);
        Assert.Equal(0, ended.DurationMs);
        Assert.IsType<BridgeEvent.TokenDelta>(events[2]);
    }

    [Fact]
    public void OnToolStartEmitsToolCall_AfterThinkingEnds()
    {
        var consumer = new InProcessEventConsumer();

        consumer.OnThinking("要用工具");
        consumer.OnToolStart("Read", "call-1", "{\"path\":\"a.cs\"}");

        var events = consumer.DrainEvents();
        Assert.Equal(3, events.Count);
        Assert.IsType<BridgeEvent.ThinkingStarted>(events[0]);
        Assert.IsType<BridgeEvent.ThinkingEnded>(events[1]);
        var toolCall = Assert.IsType<BridgeEvent.ToolCall>(events[2]);
        Assert.Equal("Read", toolCall.Name);
        Assert.Equal("{\"path\":\"a.cs\"}", toolCall.Args);
    }

    [Fact]
    public void OnToolEnd_EmitsToolResultWithTruncatedSummary()
    {
        var consumer = new InProcessEventConsumer();
        var longText = new string('x', 250);

        consumer.OnToolEnd("Bash", longText, "call-1", isError: false, patch: null);

        var events = consumer.DrainEvents();
        var result = Assert.IsType<BridgeEvent.ToolResult>(Assert.Single(events));
        Assert.Equal(203, result.Summary.Length);
        Assert.EndsWith("...", result.Summary);
    }

    [Fact]
    public void OnToolEnd_ErrorPrefixesSummary()
    {
        var consumer = new InProcessEventConsumer();

        consumer.OnToolEnd("Bash", "boom", "call-1", isError: true, patch: null);

        var events = consumer.DrainEvents();
        var result = Assert.IsType<BridgeEvent.ToolResult>(Assert.Single(events));
        Assert.Equal("[error] boom", result.Summary);
    }

    [Fact]
    public void OnToolEnd_WithPatch_ProjectsHunksToToolResult()
    {
        var consumer = new InProcessEventConsumer();
        var patch = new[]
        {
            new StructuredPatchHunk
            {
                OldStart = 1, OldLines = 3, NewStart = 1, NewLines = 4,
                Header = "@@ -1,3 +1,4 @@",
                Lines = new PatchLine[]
                {
                    new() { Type = PatchLineType.Context, Content = "keep" },
                    new() { Type = PatchLineType.Added, Content = "add" },
                    new() { Type = PatchLineType.Removed, Content = "drop" },
                },
            },
        };

        consumer.OnToolEnd("Edit", "Edited a.cs", "call-1", isError: false, patch);

        var events = consumer.DrainEvents();
        var result = Assert.IsType<BridgeEvent.ToolResult>(Assert.Single(events));
        Assert.Equal("Edited a.cs", result.Summary);
        // 引擎 StructuredPatchHunk → Core 零依赖 DiffHunk 投影（Kind 数值对齐可直转）
        var hunk = Assert.Single(result.Patch!);
        Assert.Equal("@@ -1,3 +1,4 @@", hunk.Header);
        Assert.Equal(3, hunk.Lines.Length);
        Assert.Equal(DiffLineKind.Context, hunk.Lines[0].Kind);
        Assert.Equal("keep", hunk.Lines[0].Content);
        Assert.Equal(DiffLineKind.Added, hunk.Lines[1].Kind);
        Assert.Equal("add", hunk.Lines[1].Content);
        Assert.Equal(DiffLineKind.Removed, hunk.Lines[2].Kind);
        Assert.Equal("drop", hunk.Lines[2].Content);
    }

    [Fact]
    public void OnToolEnd_WithEmptyPatch_KeepsPatchNull()
    {
        var consumer = new InProcessEventConsumer();

        consumer.OnToolEnd("Read", "41 lines", "call-1", isError: false, patch: []);

        var events = consumer.DrainEvents();
        var result = Assert.IsType<BridgeEvent.ToolResult>(Assert.Single(events));
        Assert.Null(result.Patch);
    }

    [Fact]
    public void OnDone_EmitsAssistantMessageWithFullText()
    {
        var consumer = new InProcessEventConsumer();

        consumer.OnText("部分");
        consumer.OnText("回答");
        consumer.OnDone(usage: null, modelId: "test-model");

        var events = consumer.DrainEvents();
        Assert.Equal(3, events.Count);
        Assert.IsType<BridgeEvent.TokenDelta>(events[0]);
        Assert.IsType<BridgeEvent.TokenDelta>(events[1]);
        var message = Assert.IsType<BridgeEvent.AssistantMessage>(events[2]);
        Assert.Equal("部分回答", message.Text);
    }

    [Fact]
    public void Reset_ClearsTurnState_ForNewThinkingSignal()
    {
        var consumer = new InProcessEventConsumer();

        consumer.OnThinking("第一回合");
        consumer.Reset();
        consumer.OnThinking("第二回合");

        var events = consumer.DrainEvents();
        Assert.Equal(2, events.Count);
        Assert.IsType<BridgeEvent.ThinkingStarted>(events[0]);
        Assert.IsType<BridgeEvent.ThinkingStarted>(events[1]);
    }

    [Fact]
    public void IgnoredCallbacks_EmitNothing()
    {
        var consumer = new InProcessEventConsumer();

        consumer.OnToolProgress("WebSearch", "query_update", "搜索中");
        consumer.OnLoopDetected(3, 0, "重复模式");
        consumer.OnTimingSummary("耗时 1s");

        Assert.Empty(consumer.DrainEvents());
    }

    [Fact]
    public void OnDone_MarkdownTextCarriesProjectedDocument()
    {
        var consumer = new InProcessEventConsumer();

        consumer.OnText("# 标题\n\n**加粗** 与 `code`");
        consumer.OnDone(usage: null, modelId: "test-model");

        var events = consumer.DrainEvents();
        var message = Assert.IsType<BridgeEvent.AssistantMessage>(events[^1]);
        Assert.NotNull(message.Markdown);
        var heading = Assert.IsType<MarkdownHeading>(message.Markdown.Blocks[0]);
        Assert.Equal(1, heading.Level);
        Assert.Contains(message.Markdown.Blocks, block => block is MarkdownParagraph);
    }

    [Fact]
    public void OnDone_PlainTextKeepsMarkdownNull()
    {
        var consumer = new InProcessEventConsumer();

        consumer.OnText("普通纯文本回答");
        consumer.OnDone(usage: null, modelId: "test-model");

        var events = consumer.DrainEvents();
        var message = Assert.IsType<BridgeEvent.AssistantMessage>(events[^1]);
        Assert.Null(message.Markdown);
    }
}
