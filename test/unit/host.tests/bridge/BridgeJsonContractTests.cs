// JCC1018 豁免: 本测试以 Windows 路径字面量为被测数据（Windows 形态行为/守护判定/端点解析等），
// 被测对象即 Windows 路径语义，非跨平台误用 — 新增跨平台匹配测试请用 Path.Combine(GetTempPath()) 构造。
#pragma warning disable JCC1018
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

    /// <summary>停止后台任务请求 JSON 形状（M8-8 后续 — 与 native_tui 侧 BridgeRequest.StopTask 逐字段镜像：
    /// stop_task + kind/taskId camelCase 字段，协议契约快照）。</summary>
    [Fact]
    public void Serialize_StopTask_CarriesCamelCaseFields() {
        var json = JsonSerializer.Serialize(
            new CliBridge.BridgeRequest.StopTask("teammate", "teammate-9"), CliBridge.BridgeJsonContext.Safe.BridgeRequest);
        Assert.Equal(
            "{\"type\":\"stop_task\",\"kind\":\"teammate\",\"taskId\":\"teammate-9\"}", json);

        // native_tui BridgeProtocol.WriteRequest 产出的行反解析回读（双镜像 roundtrip）
        var request = JsonSerializer.Deserialize(
            "{\"type\":\"stop_task\",\"kind\":\"fork\",\"taskId\":\"fork-123\"}", CliBridge.BridgeJsonContext.Safe.BridgeRequest);
        var stopTask = Assert.IsType<CliBridge.BridgeRequest.StopTask>(request);
        Assert.Equal(("fork", "fork-123"), (stopTask.Kind, stopTask.TaskId));
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

    /// <summary>submit 带图片附件 JSON 形状（TASK066 板块 G G4 — 与 native_tui 侧
    /// BridgeRequest.Submit 逐字段镜像：images 数组 camelCase base64/mediaType/filename，
    /// 协议契约快照；Safe 上下文不转义中文 — 断言原文）。</summary>
    [Fact]
    public void Serialize_SubmitWithImages_MatchesTuiContract() {
        var json = JsonSerializer.Serialize(
            new CliBridge.BridgeRequest.Submit("看这张图",
                [new CliBridge.BridgeImageAttachment("aGk=", "image/png", "shot.png")]),
            CliBridge.BridgeJsonContext.Safe.BridgeRequest);
        Assert.Equal(
            "{\"type\":\"submit\",\"text\":\"看这张图\",\"images\":[{\"base64\":\"aGk=\",\"mediaType\":\"image/png\",\"filename\":\"shot.png\"}]}",
            json);
    }

    /// <summary>submit 显式 null 附件 — Images 不序列化字段，与旧契约逐字节一致
    /// （WhenWritingNull — 旧 TUI 零破坏，对齐 BridgeDiffLine 行号字段扩展先例）。</summary>
    [Fact]
    public void Serialize_SubmitWithoutImages_OmitsImagesField() {
        var json = JsonSerializer.Serialize(
            new CliBridge.BridgeRequest.Submit("hello world", Images: null),
            CliBridge.BridgeJsonContext.Safe.BridgeRequest);
        Assert.Equal("{\"type\":\"submit\",\"text\":\"hello world\"}", json);
    }

    /// <summary>native_tui 产出的带图 submit 行反解析回读（双镜像 roundtrip — 全字段回读）。</summary>
    [Fact]
    public void Deserialize_TuiEmittedSubmitWithImages_ParsesBack() {
        var request = JsonSerializer.Deserialize(
            "{\"type\":\"submit\",\"text\":\"hi\",\"images\":[{\"base64\":\"aGk=\",\"mediaType\":\"image/png\",\"filename\":\"shot.png\"}]}",
            CliBridge.BridgeJsonContext.Safe.BridgeRequest);
        var submit = Assert.IsType<CliBridge.BridgeRequest.Submit>(request);
        Assert.Equal("hi", submit.Text);
        Assert.NotNull(submit.Images);
        var image = Assert.Single(submit.Images);
        Assert.Equal(new CliBridge.BridgeImageAttachment("aGk=", "image/png", "shot.png"), image);
    }

    /// <summary>旧 TUI（G4 前）不输出 images 字段 → 反序列化 Images=null 不报错（旧客户端兼容面）。</summary>
    [Fact]
    public void Deserialize_LegacySubmitWithoutImages_ImagesStaysNull() {
        var request = JsonSerializer.Deserialize(
            "{\"type\":\"submit\",\"text\":\"legacy\"}", CliBridge.BridgeJsonContext.Safe.BridgeRequest);
        var submit = Assert.IsType<CliBridge.BridgeRequest.Submit>(request);
        Assert.Equal("legacy", submit.Text);
        Assert.Null(submit.Images);
    }

    /// <summary>上下文余量事件 JSON 形状（M8-7 — 与 native_tui 侧 BridgeEvent.ContextUsage 逐字段镜像：
    /// context_usage + promptTokens/contextWindowTokens/percentLeft camelCase 字段）。</summary>
    [Fact]
    public void Serialize_ContextUsage_CarriesCamelCaseFields() {
        var json = JsonSerializer.Serialize(
            new CliBridge.BridgeEvent.ContextUsage(150_000, 200_000, 13), CliBridge.BridgeJsonContext.Safe.BridgeEvent);
        Assert.Equal(
            "{\"type\":\"context_usage\",\"promptTokens\":150000,\"contextWindowTokens\":200000,\"percentLeft\":13}",
            json);
    }

    /// <summary>后台任务事件 JSON 形状（M8-8 — 与 native_tui 侧 BridgeEvent.BackgroundTask 逐字段镜像：
    /// background_task + kind/taskId/description/state camelCase 字段，协议契约快照）。</summary>
    [Fact]
    public void Serialize_BackgroundTask_CarriesCamelCaseFields() {
        var json = JsonSerializer.Serialize(
            new CliBridge.BridgeEvent.BackgroundTask("fork", "fork-123", "检查README", "running"),
            CliBridge.BridgeJsonContext.Safe.BridgeEvent);
        Assert.Equal(
            "{\"type\":\"background_task\",\"kind\":\"fork\",\"taskId\":\"fork-123\",\"description\":\"检查README\",\"state\":\"running\"}",
            json);
    }

    /// <summary>子代理三事件 JSON 形状（DSG041 批3 — 与 native_tui 侧 BridgeEvent 逐字段镜像：
    /// subagent_started/subagent_tool_use/subagent_finished，camelCase 字段）。</summary>
    [Fact]
    public void Serialize_SubagentEvents_CarryCamelCaseFields() {
        var events = new CliBridge.BridgeEvent[] {
            new CliBridge.BridgeEvent.SubagentStarted("agent-1", "explore", "搜索渲染链"),
            new CliBridge.BridgeEvent.SubagentToolUse("agent-1", "Grep", "命中 3 处", 120),
            new CliBridge.BridgeEvent.SubagentFinished("agent-1", IsError: true, TokenTotal: 1250),
        };
        var expected = new[] {
            "{\"type\":\"subagent_started\",\"subagentId\":\"agent-1\",\"agentType\":\"explore\",\"description\":\"搜索渲染链\"}",
            "{\"type\":\"subagent_tool_use\",\"subagentId\":\"agent-1\",\"toolName\":\"Grep\",\"summary\":\"命中 3 处\",\"tokenGain\":120}",
            "{\"type\":\"subagent_finished\",\"subagentId\":\"agent-1\",\"isError\":true,\"tokenTotal\":1250}",
        };
        for (var i = 0; i < events.Length; i++) {
            Assert.Equal(expected[i], JsonSerializer.Serialize(events[i], CliBridge.BridgeJsonContext.Safe.BridgeEvent));
        }
    }

    /// <summary>工具进度事件 JSON 形状（TASK065 P3 — 与 native_tui 侧 BridgeEvent.ToolProgress
    /// 逐字段镜像：tool_progress + toolName/progressType/message camelCase 字段；
    /// message 多行文本经 JSON 转义 \n 保真 — Bash 执行中输出快照流式上屏数据源）。</summary>
    [Fact]
    public void Serialize_ToolProgress_CarriesCamelCaseFields() {
        var json = JsonSerializer.Serialize(
            new CliBridge.BridgeEvent.ToolProgress("Bash", "bash_progress", "line1\nline2"),
            CliBridge.BridgeJsonContext.Safe.BridgeEvent);
        Assert.Equal(
            "{\"type\":\"tool_progress\",\"toolName\":\"Bash\",\"progressType\":\"bash_progress\",\"message\":\"line1\\nline2\"}",
            json);

        // native_tui 侧引擎产出的行反解析回读（双镜像 roundtrip）
        var request = JsonSerializer.Deserialize(
            "{\"type\":\"tool_progress\",\"toolName\":\"PowerShell\",\"progressType\":\"ps_progress\",\"message\":\"ok\"}",
            CliBridge.BridgeJsonContext.Safe.BridgeEvent);
        var progress = Assert.IsType<CliBridge.BridgeEvent.ToolProgress>(request);
        Assert.Equal(("PowerShell", "ps_progress", "ok"), (progress.ToolName, progress.ProgressType, progress.Message));
    }

    // === ADR 0150: seq/ackSeq 流水号 JSON 契约 ===

    /// <summary>submit 带 seq → JSON 含 seq 字段（ADR 0150 流水号）。</summary>
    [Fact]
    public void Serialize_SubmitWithSeq_CarriesSeqField() {
        var json = JsonSerializer.Serialize(
            new CliBridge.BridgeRequest.Submit("hello") { Seq = 42 }, CliBridge.BridgeJsonContext.Safe.BridgeRequest);
        Assert.Contains("\"seq\":42", json);
    }

    /// <summary>turn_completed 带 ackSeq → JSON 含 ackSeq 字段（ADR 0150 回函确认）。</summary>
    [Fact]
    public void Serialize_TurnCompletedWithAckSeq_CarriesAckSeqField() {
        var json = JsonSerializer.Serialize(
            new CliBridge.BridgeEvent.TurnCompleted() { AckSeq = 42 }, CliBridge.BridgeJsonContext.Safe.BridgeEvent);
        Assert.Contains("\"ackSeq\":42", json);
    }

    /// <summary>反解析带 seq 的 submit → Seq 字段正确（roundtrip）。</summary>
    [Fact]
    public void Deserialize_SubmitWithSeq_ParsesSeqBack() {
        var request = JsonSerializer.Deserialize(
            "{\"type\":\"submit\",\"text\":\"hi\",\"seq\":99}", CliBridge.BridgeJsonContext.Safe.BridgeRequest);
        var submit = Assert.IsType<CliBridge.BridgeRequest.Submit>(request);
        Assert.Equal(99UL, submit.Seq);
    }

    /// <summary>反解析带 ackSeq 的 turn_completed → AckSeq 字段正确（roundtrip）。</summary>
    [Fact]
    public void Deserialize_TurnCompletedWithAckSeq_ParsesAckSeqBack() {
        var evt = JsonSerializer.Deserialize(
            "{\"type\":\"turn_completed\",\"ackSeq\":99}", CliBridge.BridgeJsonContext.Safe.BridgeEvent);
        var completed = Assert.IsType<CliBridge.BridgeEvent.TurnCompleted>(evt);
        Assert.Equal(99UL, completed.AckSeq);
    }

    /// <summary>旧客户端不带 seq → 反序列化 Seq=null 兼容。</summary>
    [Fact]
    public void Deserialize_LegacySubmitWithoutSeq_SeqStaysNull() {
        var request = JsonSerializer.Deserialize(
            "{\"type\":\"submit\",\"text\":\"legacy\"}", CliBridge.BridgeJsonContext.Safe.BridgeRequest);
        var submit = Assert.IsType<CliBridge.BridgeRequest.Submit>(request);
        Assert.Null(submit.Seq);
    }
}
