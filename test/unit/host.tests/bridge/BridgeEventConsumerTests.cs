namespace Host.Tests.Bridge;

/// <summary>
/// bridge 事件消费器测试 — IEventConsumer 回调映射为 ADR 0141 事件行（stdout NDJSON）：
/// OnText → token_delta + OnDone 时 assistant_message；OnThinking 首次 → thinking_started、
/// 进入产出 → thinking_ended(墙钟差)；OnToolStart/OnToolEnd → tool_call/tool_result；
/// Reset 清状态防跨回合泄漏。
/// </summary>
public sealed class BridgeEventConsumerTests {
    private static (BridgeEventConsumer Consumer, StringWriter Out) Create(Func<long>? wallClock = null) {
        var @out = new StringWriter();
        var consumer = new BridgeEventConsumer(@out, wallClock);
        return (consumer, @out);
    }

    /// <summary>子代理三回调映射（DSG041 批3）— OnSubagentStarted/OnSubagentToolUse/OnSubagentFinished
    /// → subagent_started/subagent_tool_use/subagent_finished 事件行（与 native_tui 侧字段逐一对齐）。</summary>
    [Fact]
    public void SubagentCallbacks_EmitSubagentEventLines() {
        var (consumer, @out) = Create();

        consumer.OnSubagentStarted("agent-1", "explore", "搜索渲染链");
        consumer.OnSubagentToolUse("agent-1", "Grep", "命中 3 处", 120);
        consumer.OnSubagentFinished("agent-1", isError: false, tokenTotal: 1250);

        Assert.Equal(
            "{\"type\":\"subagent_started\",\"subagentId\":\"agent-1\",\"agentType\":\"explore\",\"description\":\"搜索渲染链\"}\n" +
            "{\"type\":\"subagent_tool_use\",\"subagentId\":\"agent-1\",\"toolName\":\"Grep\",\"summary\":\"命中 3 处\",\"tokenGain\":120}\n" +
            "{\"type\":\"subagent_finished\",\"subagentId\":\"agent-1\",\"isError\":false,\"tokenTotal\":1250}\n",
            @out.ToString());
    }

    /// <summary>上下文余量回调映射（M8-7）— OnContextUsage → context_usage 事件行（与 native_tui 侧字段逐一对齐）。</summary>
    [Fact]
    public void OnContextUsage_EmitsContextUsageEventLine() {
        var (consumer, @out) = Create();

        consumer.OnContextUsage(150_000, 200_000, 13);

        Assert.Equal(
            "{\"type\":\"context_usage\",\"promptTokens\":150000,\"contextWindowTokens\":200000,\"percentLeft\":13}\n",
            @out.ToString());
    }

    /// <summary>后台任务状态更新映射（M8-8）— OnBackgroundTask → background_task 事件行
    /// （事件泵直推，四参 upsert 透传；与 native_tui 侧字段逐一对齐）。</summary>
    [Fact]
    public void OnBackgroundTask_EmitsBackgroundTaskEventLine() {
        var (consumer, @out) = Create();

        consumer.OnBackgroundTask("fork", "fork-123", "检查README", "running");
        consumer.OnBackgroundTask("teammate", "teammate-9", "调研渲染链", "completed");

        Assert.Equal(
            "{\"type\":\"background_task\",\"kind\":\"fork\",\"taskId\":\"fork-123\",\"description\":\"检查README\",\"state\":\"running\"}\n" +
            "{\"type\":\"background_task\",\"kind\":\"teammate\",\"taskId\":\"teammate-9\",\"description\":\"调研渲染链\",\"state\":\"completed\"}\n",
            @out.ToString());
    }

    /// <summary>压缩完成映射（TASK070 C6/DSG046）— OnCompactCompleted → compact_completed 事件行
    /// （四参 camelCase 直投影，IsAuto 区分手动/自动；与 native_tui 侧字段逐一对齐）。</summary>
    [Fact]
    public void OnCompactCompleted_EmitsCompactCompletedEventLine() {
        var (consumer, @out) = Create();

        consumer.OnCompactCompleted(messagesSummarized: 42, originalTokens: 150_000, compressedTokens: 3_200, isAuto: false);
        consumer.OnCompactCompleted(messagesSummarized: 30, originalTokens: 180_000, compressedTokens: 2_800, isAuto: true);

        Assert.Equal(
            "{\"type\":\"compact_completed\",\"messagesSummarized\":42,\"originalTokens\":150000,\"compressedTokens\":3200,\"isAuto\":false}\n" +
            "{\"type\":\"compact_completed\",\"messagesSummarized\":30,\"originalTokens\":180000,\"compressedTokens\":2800,\"isAuto\":true}\n",
            @out.ToString());
    }

    [Fact]
    public void OnText_EmitsTokenDelta_AndOnDoneEmitsFullAssistantMessage() {
        var (consumer, @out) = Create();

        consumer.OnText("Hello ");
        consumer.OnText("World");
        consumer.OnDone(null, null);

        Assert.Equal(
            "{\"type\":\"token_delta\",\"length\":6,\"text\":\"Hello \"}\n" +
            "{\"type\":\"token_delta\",\"length\":5,\"text\":\"World\"}\n" +
            "{\"type\":\"assistant_message\",\"text\":\"Hello World\"}\n",
            @out.ToString());
    }

    [Fact]
    public void OnThinking_FirstEmitsThinkingStarted_ThenTextEmitsThinkingEndedWithDuration() {
        var clock = 0L;
        var (consumer, @out) = Create(() => clock);

        clock = 1_000;
        consumer.OnThinking("思考中");
        clock = 3_500;
        consumer.OnText("产出");
        consumer.OnDone(null, null);

        Assert.Equal(
            "{\"type\":\"thinking_started\"}\n" +
            "{\"type\":\"thinking_ended\",\"durationMs\":2500}\n" +
            "{\"type\":\"token_delta\",\"length\":2,\"text\":\"产出\"}\n" +
            "{\"type\":\"assistant_message\",\"text\":\"产出\"}\n",
            @out.ToString());
    }

    [Fact]
    public void OnToolStart_EmitsToolCall_AndEndsThinking() {
        var clock = 0L;
        var (consumer, @out) = Create(() => clock);

        clock = 1_000;
        consumer.OnThinking("想");
        clock = 2_000;
        consumer.OnToolStart("Read", "call-1", "{\"path\":\"a.cs\"}");
        consumer.OnToolEnd("Read", "41 lines", "call-1", isError: false, patch: null);
        consumer.OnDone(null, null);

        Assert.Equal(
            "{\"type\":\"thinking_started\"}\n" +
            "{\"type\":\"thinking_ended\",\"durationMs\":1000}\n" +
            "{\"type\":\"tool_call\",\"name\":\"Read\",\"args\":\"{\\\"path\\\":\\\"a.cs\\\"}\"}\n" +
            "{\"type\":\"tool_result\",\"summary\":\"41 lines\"}\n",
            @out.ToString());
    }

    [Fact]
    public void OnToolEnd_ErrorResult_SummaryCarriesErrorMarker() {
        var (consumer, @out) = Create();

        consumer.OnToolEnd("Bash", "command failed: exit 1", "call-2", isError: true, patch: null);

        Assert.Equal(
            "{\"type\":\"tool_result\",\"summary\":\"[error] command failed: exit 1\"}\n",
            @out.ToString());
    }

    [Fact]
    public void OnToolEnd_LongResult_TruncatedToLimit() {
        var (consumer, @out) = Create();
        var longResult = new string('x', 500);

        consumer.OnToolEnd("Read", longResult, "call-3", isError: false, patch: null);

        var line = @out.ToString().TrimEnd('\n');
        Assert.DoesNotContain(new string('x', 300), line);
        Assert.Contains("...", line);
    }

    [Fact]
    public void OnToolEnd_WithPatch_EmitsToolResultWithStructuredDiff() {
        var (consumer, @out) = Create();
        var patch = new[] {
            new StructuredPatchHunk {
                OldStart = 1, OldLines = 3, NewStart = 1, NewLines = 4,
                Header = "@@ -1,3 +1,4 @@",
                Lines = new PatchLine[] {
                    new() { Type = PatchLineType.Context, Content = "keep" },
                    new() { Type = PatchLineType.Added, Content = "add" },
                    new() { Type = PatchLineType.Removed, Content = "drop" },
                },
            },
        };

        consumer.OnToolEnd("Edit", "Edited a.cs", "call-1", isError: false, patch);

        // patch 投影为 hunk 渲染子集（header + lines[kind,content]，kind 数值 0/1/2 对齐 native_tui DiffLineKind）
        Assert.Equal(
            "{\"type\":\"tool_result\",\"summary\":\"Edited a.cs\",\"patch\":[" +
            "{\"header\":\"@@ -1,3 +1,4 @@\",\"lines\":[" +
            "{\"kind\":0,\"content\":\"keep\"}," +
            "{\"kind\":1,\"content\":\"add\"}," +
            "{\"kind\":2,\"content\":\"drop\"}]}]}\n",
            @out.ToString());
    }

    [Fact]
    public void OnToolEnd_WithEmptyPatch_OmitsPatchField() {
        var (consumer, @out) = Create();

        consumer.OnToolEnd("Read", "41 lines", "call-1", isError: false, patch: []);

        // 空/ null 补丁不输出 patch 字段（WhenWritingNull — 旧 golden 契约不变）
        Assert.Equal("{\"type\":\"tool_result\",\"summary\":\"41 lines\"}\n", @out.ToString());
    }

    [Fact]
    public void OnToolEnd_WithPatchLineNumbers_EmitsCamelCaseLineNumbers() {
        var (consumer, @out) = Create();
        var patch = new[] {
            new StructuredPatchHunk {
                OldStart = 1, OldLines = 2, NewStart = 1, NewLines = 3,
                Header = "@@ -1,2 +1,3 @@",
                Lines = new PatchLine[] {
                    new() { Type = PatchLineType.Removed, Content = "old", OldLineNumber = 1 },
                    new() { Type = PatchLineType.Added, Content = "new", NewLineNumber = 1 },
                    new() { Type = PatchLineType.Context, Content = "keep", OldLineNumber = 2, NewLineNumber = 2 },
                },
            },
        };

        consumer.OnToolEnd("Edit", "Edited a.cs", "call-1", isError: false, patch);

        // TASK066 F1 行号贯通：oldLineNumber/newLineNumber camelCase 序列化；对侧 null 不输出（WhenWritingNull）
        Assert.Equal(
            "{\"type\":\"tool_result\",\"summary\":\"Edited a.cs\",\"patch\":[" +
            "{\"header\":\"@@ -1,2 +1,3 @@\",\"lines\":[" +
            "{\"kind\":2,\"content\":\"old\",\"oldLineNumber\":1}," +
            "{\"kind\":1,\"content\":\"new\",\"newLineNumber\":1}," +
            "{\"kind\":0,\"content\":\"keep\",\"oldLineNumber\":2,\"newLineNumber\":2}]}]}\n",
            @out.ToString());
    }

    [Fact]
    public void OnDone_WithoutText_EmitsNoAssistantMessage() {
        var (consumer, @out) = Create();

        consumer.OnDone(null, null);

        Assert.Equal(string.Empty, @out.ToString());
    }

    [Fact]
    public void Reset_ClearsState_NoLeakAcrossTurns() {
        var clock = 0L;
        var (consumer, @out) = Create(() => clock);

        clock = 1_000;
        consumer.OnThinking("第一轮思考");
        clock = 2_000;
        consumer.OnText("第一轮");
        consumer.OnDone(null, null);
        consumer.Reset();

        clock = 10_000;
        consumer.OnText("第二轮");
        consumer.OnDone(null, null);

        // 第二轮无 thinking_started（已 Reset），文本独立
        Assert.Equal(
            "{\"type\":\"thinking_started\"}\n" +
            "{\"type\":\"thinking_ended\",\"durationMs\":1000}\n" +
            "{\"type\":\"token_delta\",\"length\":3,\"text\":\"第一轮\"}\n" +
            "{\"type\":\"assistant_message\",\"text\":\"第一轮\"}\n" +
            "{\"type\":\"token_delta\",\"length\":3,\"text\":\"第二轮\"}\n" +
            "{\"type\":\"assistant_message\",\"text\":\"第二轮\"}\n",
            @out.ToString());
    }

    [Fact]
    public void OnThinking_AgainAfterEnded_DoesNotRestartSignal() {
        var clock = 0L;
        var (consumer, @out) = Create(() => clock);

        clock = 1_000;
        consumer.OnThinking("a");
        clock = 2_000;
        consumer.OnText("t");
        consumer.OnThinking("b");
        consumer.OnDone(null, null);

        // 首个 OnThinking 发 started；思考结束后再来 thinking 不重启（回合内语义）
        Assert.Equal(
            "{\"type\":\"thinking_started\"}\n" +
            "{\"type\":\"thinking_ended\",\"durationMs\":1000}\n" +
            "{\"type\":\"token_delta\",\"length\":1,\"text\":\"t\"}\n" +
            "{\"type\":\"assistant_message\",\"text\":\"t\"}\n",
            @out.ToString());
    }
}
