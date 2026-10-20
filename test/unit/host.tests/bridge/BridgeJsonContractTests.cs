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
    public void Serialize_SetPermissionMode_CarriesModeField() {
        var json = JsonSerializer.Serialize(
            new CliBridge.BridgeRequest.SetPermissionMode("unattended"), CliBridge.BridgeJsonContext.Safe.BridgeRequest);
        Assert.Equal("{\"type\":\"set_permission_mode\",\"mode\":\"unattended\"}", json);
    }

    [Fact]
    public void Serialize_SessionInfo_CarriesCamelCaseFields() {
        var json = JsonSerializer.Serialize(
            new CliBridge.BridgeEvent.SessionInfo("2.1.303", "glm-4.7", "plan", false, "D:\\engine"),
            CliBridge.BridgeJsonContext.Safe.BridgeEvent);
        Assert.Equal(
            "{\"type\":\"session_info\",\"version\":\"2.1.303\",\"model\":\"glm-4.7\",\"permissionMode\":\"plan\",\"bypassAvailable\":false,\"cwd\":\"D:\\\\engine\"}",
            json);
    }

    [Fact]
    public void Serialize_SessionInfoWithoutModel_ModelIsNull() {
        var json = JsonSerializer.Serialize(
            new CliBridge.BridgeEvent.SessionInfo("1.0.0", null, "auto", true, "C:\\wd"),
            CliBridge.BridgeJsonContext.Safe.BridgeEvent);
        Assert.Equal(
            "{\"type\":\"session_info\",\"version\":\"1.0.0\",\"model\":null,\"permissionMode\":\"auto\",\"bypassAvailable\":true,\"cwd\":\"C:\\\\wd\"}",
            json);
    }

    [Fact]
    public void Serialize_PermissionModeChanged_CarriesMode() {
        var json = JsonSerializer.Serialize(
            new CliBridge.BridgeEvent.PermissionModeChanged("bypass"), CliBridge.BridgeJsonContext.Safe.BridgeEvent);
        Assert.Equal("{\"type\":\"permission_mode_changed\",\"mode\":\"bypass\"}", json);
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
