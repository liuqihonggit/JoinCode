namespace Host.Tests.Bridge;

/// <summary>
/// bridge JSON 契约测试 — jcc 侧 DTO 必须与 native_tui ADR 0141 契约逐字节一致：
/// type 判别 + camelCase 字段 + 多态序列化（AOT 源生成）。
/// 两端各自定义 DTO（进程协议常态），本测试锁定 JSON 形状防漂移。
/// </summary>
public sealed class BridgeJsonContractTests {
    [Fact]
    public void Serialize_Submit_MatchesTuiContract() {
        var json = JsonSerializer.Serialize(
            new CliBridge.BridgeRequest.Submit("hello world"), CliBridge.BridgeJsonContext.Safe.BridgeRequest);
        Assert.Equal("{\"type\":\"submit\",\"text\":\"hello world\"}", json);
    }

    [Fact]
    public void Serialize_InterruptAndQuit_HaveTypeOnly() {
        var interrupt = JsonSerializer.Serialize(
            new CliBridge.BridgeRequest.Interrupt(), CliBridge.BridgeJsonContext.Safe.BridgeRequest);
        Assert.Equal("{\"type\":\"interrupt\"}", interrupt);

        var quit = JsonSerializer.Serialize(
            new CliBridge.BridgeRequest.Quit(), CliBridge.BridgeJsonContext.Safe.BridgeRequest);
        Assert.Equal("{\"type\":\"quit\"}", quit);
    }

    [Fact]
    public void Serialize_MarkerEvents_HaveTypeOnly() {
        var cases = new (CliBridge.BridgeEvent Event, string Expected)[] {
            (new CliBridge.BridgeEvent.TurnStarted(), "{\"type\":\"turn_started\"}"),
            (new CliBridge.BridgeEvent.ThinkingStarted(), "{\"type\":\"thinking_started\"}"),
            (new CliBridge.BridgeEvent.TurnCompleted(), "{\"type\":\"turn_completed\"}"),
            (new CliBridge.BridgeEvent.TurnInterrupted(), "{\"type\":\"turn_interrupted\"}"),
        };
        foreach (var (evt, expected) in cases) {
            Assert.Equal(expected, JsonSerializer.Serialize(evt, CliBridge.BridgeJsonContext.Safe.BridgeEvent));
        }
    }

    [Fact]
    public void Serialize_PayloadEvents_CarryCamelCaseFields() {
        var events = new CliBridge.BridgeEvent[] {
            new CliBridge.BridgeEvent.ThinkingEnded(5000),
            new CliBridge.BridgeEvent.ToolCall("Read", "src/main.cs"),
            new CliBridge.BridgeEvent.ToolResult("41 lines"),
            new CliBridge.BridgeEvent.TokenDelta(24),
            new CliBridge.BridgeEvent.AssistantMessage("回复内容"),
            new CliBridge.BridgeEvent.Error("出错了：参数缺失"),
        };
        var expected = new[] {
            "{\"type\":\"thinking_ended\",\"durationMs\":5000}",
            "{\"type\":\"tool_call\",\"name\":\"Read\",\"args\":\"src/main.cs\"}",
            "{\"type\":\"tool_result\",\"summary\":\"41 lines\"}",
            "{\"type\":\"token_delta\",\"length\":24}",
            "{\"type\":\"assistant_message\",\"text\":\"回复内容\"}",
            "{\"type\":\"error\",\"message\":\"出错了：参数缺失\"}",
        };
        for (var i = 0; i < events.Length; i++) {
            Assert.Equal(expected[i], JsonSerializer.Serialize(events[i], CliBridge.BridgeJsonContext.Safe.BridgeEvent));
        }
    }

    [Fact]
    public void Deserialize_TuiEmittedRequestLine_ParsesBack() {
        // native_tui BridgeProtocol.WriteRequest 产出的行（契约快照）
        var request = JsonSerializer.Deserialize(
            "{\"type\":\"submit\",\"text\":\"你好 world\"}", CliBridge.BridgeJsonContext.Safe.BridgeRequest);
        var submit = Assert.IsType<CliBridge.BridgeRequest.Submit>(request);
        Assert.Equal("你好 world", submit.Text);
    }
}
