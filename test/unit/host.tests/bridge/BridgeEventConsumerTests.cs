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

    [Fact]
    public void OnText_EmitsTokenDelta_AndOnDoneEmitsFullAssistantMessage() {
        var (consumer, @out) = Create();

        consumer.OnText("Hello ");
        consumer.OnText("World");
        consumer.OnDone(null, null);

        Assert.Equal(
            "{\"type\":\"token_delta\",\"length\":6}\n" +
            "{\"type\":\"token_delta\",\"length\":5}\n" +
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
            "{\"type\":\"token_delta\",\"length\":2}\n" +
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
            "{\"type\":\"token_delta\",\"length\":3}\n" +
            "{\"type\":\"assistant_message\",\"text\":\"第一轮\"}\n" +
            "{\"type\":\"token_delta\",\"length\":3}\n" +
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
            "{\"type\":\"token_delta\",\"length\":1}\n" +
            "{\"type\":\"assistant_message\",\"text\":\"t\"}\n",
            @out.ToString());
    }
}
