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

    /// <summary>M8-1 逐字流式 — OnText 发携带文本的 TokenDelta（渲染层 StreamingTextItem 消费）。</summary>
    [Fact]
    public void OnText_EmitsTokenDeltaWithTextContent()
    {
        var consumer = new InProcessEventConsumer();

        consumer.OnText("你好");

        var events = consumer.DrainEvents();
        var delta = Assert.IsType<BridgeEvent.TokenDelta>(events[0]);
        Assert.Equal("你好", delta.Text);
        Assert.Equal(2, delta.Length);
    }

    /// <summary>子代理三回调映射（DSG041 批4）— OnSubagentStarted/OnSubagentToolUse/OnSubagentFinished
    /// → SubagentStarted/SubagentToolUse/SubagentFinished 内存事件（ReplBridgeDriver 路由既有，零改动验收面）。</summary>
    [Fact]
    public void SubagentCallbacks_EnqueueSubagentEvents()
    {
        var consumer = new InProcessEventConsumer();

        consumer.OnSubagentStarted("agent-1", "explore", "搜索渲染链");
        consumer.OnSubagentToolUse("agent-1", "Grep", "命中 3 处", 120);
        consumer.OnSubagentFinished("agent-1", isError: false, tokenTotal: 350);

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
        Assert.Equal(350, finished.TokenTotal);
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
    public void OnToolEnd_WithPatch_ProjectsLineNumbers()
    {
        // TASK066 F1 行号贯通：引擎 PatchLine.OldLineNumber/NewLineNumber 真值 → Core DiffLine 投影
        //（gutter 渲染数据源 — Removed 带 old / Added 带 new / Context 双侧；对侧 null）
        var consumer = new InProcessEventConsumer();
        var patch = new[]
        {
            new StructuredPatchHunk
            {
                OldStart = 1, OldLines = 2, NewStart = 1, NewLines = 3,
                Header = "@@ -1,2 +1,3 @@",
                Lines = new PatchLine[]
                {
                    new() { Type = PatchLineType.Removed, Content = "old", OldLineNumber = 1 },
                    new() { Type = PatchLineType.Added, Content = "new", NewLineNumber = 1 },
                    new() { Type = PatchLineType.Context, Content = "keep", OldLineNumber = 2, NewLineNumber = 2 },
                },
            },
        };

        consumer.OnToolEnd("Edit", "Edited a.cs", "call-1", isError: false, patch);

        var events = consumer.DrainEvents();
        var result = Assert.IsType<BridgeEvent.ToolResult>(Assert.Single(events));
        var hunk = Assert.Single(result.Patch!);
        Assert.Equal(1, hunk.Lines[0].OldLineNumber);
        Assert.Null(hunk.Lines[0].NewLineNumber);
        Assert.Equal(1, hunk.Lines[1].NewLineNumber);
        Assert.Null(hunk.Lines[1].OldLineNumber);
        Assert.Equal(2, hunk.Lines[2].OldLineNumber);
        Assert.Equal(2, hunk.Lines[2].NewLineNumber);
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

        // OnToolProgress 已消费（TASK065 P3 bash 流式进度 — tool_progress 事件映射，见扩展映射测试）
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

    /// <summary>TASK062 结构化组装 — OnThinking 全量累积 CoT + OnDone 带 MessageId/思考时长/内容；
    /// 多段思考（think→text→think）时长累计。</summary>
    [Fact]
    public void OnDone_AssemblesStructuredFields_ThinkingAccumulated()
    {
        var clock = 0L;
        var consumer = new InProcessEventConsumer(() => clock);

        clock = 1000;
        consumer.OnThinking("第一段推理");
        clock = 2500;
        consumer.OnText("中间产出");
        consumer.OnThinking("第二段推理");
        clock = 4000;
        consumer.OnDone(null, "m");

        var events = consumer.DrainEvents();
        var message = Assert.IsType<BridgeEvent.AssistantMessage>(events[^1]);
        Assert.Equal(1, message.MessageId);
        Assert.Equal(1500 + 1500, message.ThinkingDurationMs);
        Assert.Equal("第一段推理第二段推理", message.ThinkingContent);
    }

    /// <summary>TASK062 — 消息序号跨回合递增（Reset 不清）；无思考回合字段为默认值。</summary>
    [Fact]
    public void MessageId_IncrementsAcrossTurns_NoThinkingDefaults()
    {
        var consumer = new InProcessEventConsumer();

        consumer.OnText("第一回合");
        consumer.OnDone(null, null);
        consumer.Reset();
        consumer.OnText("第二回合");
        consumer.OnDone(null, null);

        var all = consumer.DrainEvents().OfType<BridgeEvent.AssistantMessage>().ToList();
        Assert.Equal([1, 2], all.Select(m => m.MessageId));
        Assert.All(all, m => Assert.Equal(0, m.ThinkingDurationMs));
        Assert.All(all, m => Assert.Null(m.ThinkingContent));
    }

    /// <summary>M8-7 上下文余量 — OnContextUsage 三参直传 ContextUsage 内存事件
    /// （percentLeft 引擎侧算好，TUI 侧零计算直路由 footer 告警）。</summary>
    [Fact]
    public void OnContextUsage_EnqueuesContextUsageEvent()
    {
        var consumer = new InProcessEventConsumer();

        consumer.OnContextUsage(180_000, 200_000, 8);

        var events = consumer.DrainEvents();
        var usage = Assert.IsType<BridgeEvent.ContextUsage>(Assert.Single(events));
        Assert.Equal(180_000, usage.PromptTokens);
        Assert.Equal(200_000, usage.ContextWindowTokens);
        Assert.Equal(8, usage.PercentLeft);
    }

    /// <summary>M8-8 后台任务状态更新 — OnBackgroundTask 四参直传 BackgroundTask 内存事件
    /// （(kind,taskId) upsert 面板条目；接口同步由引擎侧 IEventConsumer DIM 完成）。</summary>
    [Fact]
    public void OnBackgroundTask_EnqueuesBackgroundTaskEvent()
    {
        var consumer = new InProcessEventConsumer();

        consumer.OnBackgroundTask("fork", "fork-123", "检查README", "running");

        var events = consumer.DrainEvents();
        var task = Assert.IsType<BridgeEvent.BackgroundTask>(Assert.Single(events));
        Assert.Equal("fork", task.Kind);
        Assert.Equal("fork-123", task.TaskId);
        Assert.Equal("检查README", task.Description);
        Assert.Equal("running", task.State);
    }

    /// <summary>TASK070 C6/DSG046 压缩完成 — OnCompactCompleted 四参直传 CompactCompleted 内存事件
    /// （ReplBridgeDriver 路由 TokenUsageTracker.Reset + 通知上屏；接口同步由引擎侧 IEventConsumer DIM 完成）。</summary>
    [Fact]
    public void OnCompactCompleted_EnqueuesCompactCompletedEvent()
    {
        var consumer = new InProcessEventConsumer();

        consumer.OnCompactCompleted(42, 150_000, 3_200, isAuto: true);

        var events = consumer.DrainEvents();
        var completed = Assert.IsType<BridgeEvent.CompactCompleted>(Assert.Single(events));
        Assert.Equal((42, 150_000L, 3_200L, true), (completed.MessagesSummarized, completed.OriginalTokens, completed.CompressedTokens, completed.IsAuto));
    }
}
