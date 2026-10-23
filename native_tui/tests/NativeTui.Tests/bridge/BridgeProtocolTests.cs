namespace NativeTui.Tests.Bridge;

/// <summary>
/// bridge 进程协议测试 — newline-delimited JSON over stdio（ADR 0141）：
/// 请求/事件 DTO 双向 roundtrip、type 判别、空行跳过、未知 type 诊断（诱导方式）、流终止。
/// </summary>
public sealed class BridgeProtocolTests
{
    [Fact]
    public void WriteRequest_Submit_SerializesWithTypeDiscriminator()
    {
        var writer = new StringWriter();
        BridgeProtocol.WriteRequest(writer, new BridgeRequest.Submit("hello world"));
        Assert.Equal("{\"type\":\"submit\",\"text\":\"hello world\"}\n", writer.ToString());
    }

    [Fact]
    public void WriteRequest_InterruptAndQuit_NoExtraFields()
    {
        var interrupt = new StringWriter();
        BridgeProtocol.WriteRequest(interrupt, new BridgeRequest.Interrupt());
        Assert.Equal("{\"type\":\"interrupt\"}\n", interrupt.ToString());

        var quit = new StringWriter();
        BridgeProtocol.WriteRequest(quit, new BridgeRequest.Quit());
        Assert.Equal("{\"type\":\"quit\"}\n", quit.ToString());
    }

    [Fact]
    public void WriteRequest_SetPermissionMode_CarriesModeField()
    {
        var writer = new StringWriter();
        BridgeProtocol.WriteRequest(writer, new BridgeRequest.SetPermissionMode("plan"));
        Assert.Equal("{\"type\":\"set_permission_mode\",\"mode\":\"plan\"}\n", writer.ToString());
    }

    /// <summary>停止后台任务请求 roundtrip（M8-8 后续 — stop_task + kind/taskId camelCase 字段，
    /// 与 jcc 侧 BridgeRequest.StopTask 逐字段镜像，BridgeJsonContractTests 锁定防漂移）。</summary>
    [Fact]
    public void WriteRequest_StopTask_CarriesKindAndTaskIdFields()
    {
        var writer = new StringWriter();
        BridgeProtocol.WriteRequest(writer, new BridgeRequest.StopTask("teammate", "teammate-9"));
        Assert.Equal("{\"type\":\"stop_task\",\"kind\":\"teammate\",\"taskId\":\"teammate-9\"}\n", writer.ToString());

        // jcc 侧产出的请求行反解析回读（双镜像 roundtrip）
        var reader = new StringReader("{\"type\":\"stop_task\",\"kind\":\"fork\",\"taskId\":\"fork-123\"}\n");
        Assert.Equal(BridgeReadResult.Event, BridgeProtocol.TryReadRequest(reader, out var request, out _));
        var stopTask = Assert.IsType<BridgeRequest.StopTask>(request);
        Assert.Equal(("fork", "fork-123"), (stopTask.Kind, stopTask.TaskId));
    }

    [Fact]
    public void ReadEvent_SessionInfo_ParsesEngineHandshake()
    {
        var reader = new StringReader("{\"type\":\"session_info\",\"version\":\"2.1.303\",\"model\":\"glm-4.7\",\"permissionMode\":\"plan\",\"bypassAvailable\":false,\"cwd\":\"D:\\\\engine\"}\n");
        var result = BridgeProtocol.TryReadEvent(reader, out var @event, out var diagnostic);
        Assert.Equal(BridgeReadResult.Event, result);
        var info = Assert.IsType<BridgeEvent.SessionInfo>(@event);
        Assert.Equal("2.1.303", info.Version);
        Assert.Equal("glm-4.7", info.Model);
        Assert.Equal("plan", info.PermissionMode);
        Assert.False(info.BypassAvailable);
        Assert.Equal(@"D:\engine", info.Cwd);
        Assert.Null(diagnostic);
    }

    [Fact]
    public void ReadEvent_SessionInfoWithoutModel_ParsesNullModel()
    {
        var reader = new StringReader("{\"type\":\"session_info\",\"version\":\"1.0.0\",\"model\":null,\"permissionMode\":\"auto\",\"bypassAvailable\":true,\"cwd\":\"C:\\\\wd\"}\n");
        BridgeProtocol.TryReadEvent(reader, out var @event, out _);
        var info = Assert.IsType<BridgeEvent.SessionInfo>(@event);
        Assert.Null(info.Model);
        Assert.True(info.BypassAvailable);
    }

    [Fact]
    public void ReadEvent_PermissionModeChanged_CarriesMode()
    {
        var reader = new StringReader("{\"type\":\"permission_mode_changed\",\"mode\":\"unattended\"}\n");
        BridgeProtocol.TryReadEvent(reader, out var @event, out _);
        var changed = Assert.IsType<BridgeEvent.PermissionModeChanged>(@event);
        Assert.Equal("unattended", changed.Mode);
    }

    [Fact]
    public void WriteRequest_AppendsNewlineDelimiter()
    {
        var writer = new StringWriter();
        BridgeProtocol.WriteRequest(writer, new BridgeRequest.Submit("x"));
        BridgeProtocol.WriteRequest(writer, new BridgeRequest.Interrupt());
        Assert.Equal("{\"type\":\"submit\",\"text\":\"x\"}\n{\"type\":\"interrupt\"}\n", writer.ToString());
    }

    [Fact]
    public void ReadEvent_TurnStarted_Parses()
    {
        var reader = new StringReader("{\"type\":\"turn_started\"}\n");
        var result = BridgeProtocol.TryReadEvent(reader, out var @event, out _);
        Assert.Equal(BridgeReadResult.Event, result);
        Assert.IsType<BridgeEvent.TurnStarted>(@event);
    }

    [Theory]
    [InlineData("{\"type\":\"thinking_started\"}", typeof(BridgeEvent.ThinkingStarted))]
    [InlineData("{\"type\":\"turn_completed\"}", typeof(BridgeEvent.TurnCompleted))]
    [InlineData("{\"type\":\"turn_interrupted\"}", typeof(BridgeEvent.TurnInterrupted))]
    public void ReadEvent_MarkerEvents_ParseByType(string line, Type expected)
    {
        var reader = new StringReader(line + "\n");
        var result = BridgeProtocol.TryReadEvent(reader, out var @event, out _);
        Assert.Equal(BridgeReadResult.Event, result);
        Assert.IsType(expected, @event);
    }

    [Fact]
    public void ReadEvent_ThinkingEnded_CarriesDuration()
    {
        var reader = new StringReader("{\"type\":\"thinking_ended\",\"durationMs\":5000}\n");
        BridgeProtocol.TryReadEvent(reader, out var @event, out _);
        var ended = Assert.IsType<BridgeEvent.ThinkingEnded>(@event);
        Assert.Equal(5000L, ended.DurationMs);
    }

    [Fact]
    public void ReadEvent_ToolCall_CarriesNameAndArgs()
    {
        var reader = new StringReader("{\"type\":\"tool_call\",\"name\":\"Read\",\"args\":\"src/main.cs\"}\n");
        BridgeProtocol.TryReadEvent(reader, out var @event, out _);
        var call = Assert.IsType<BridgeEvent.ToolCall>(@event);
        Assert.Equal("Read", call.Name);
        Assert.Equal("src/main.cs", call.Args);
    }

    [Fact]
    public void ReadEvent_ToolResultCarriesSummary_TokenDeltaCarriesLength()
    {
        var reader = new StringReader("{\"type\":\"tool_result\",\"summary\":\"41 lines\"}\n{\"type\":\"token_delta\",\"length\":9}\n");
        BridgeProtocol.TryReadEvent(reader, out var first, out _);
        var summary = Assert.IsType<BridgeEvent.ToolResult>(first);
        Assert.Equal("41 lines", summary.Summary);

        BridgeProtocol.TryReadEvent(reader, out var second, out _);
        var delta = Assert.IsType<BridgeEvent.TokenDelta>(second);
        Assert.Equal(9, delta.Length);
    }

    /// <summary>M8-1 逐字流式 — token_delta 携带 text 增量字段（StreamingTextItem 消费）；
    /// 旧无 text 形态反序列化兼容（Text=null，Length 计数语义保留）。</summary>
    [Fact]
    public void ReadEvent_TokenDeltaCarriesText_LegacyWithoutTextStaysNull()
    {
        var reader = new StringReader("{\"type\":\"token_delta\",\"length\":5,\"text\":\"你好\"}\n{\"type\":\"token_delta\",\"length\":9}\n");
        BridgeProtocol.TryReadEvent(reader, out var first, out _);
        var withText = Assert.IsType<BridgeEvent.TokenDelta>(first);
        Assert.Equal("你好", withText.Text);
        Assert.Equal(5, withText.Length);

        BridgeProtocol.TryReadEvent(reader, out var second, out _);
        var legacy = Assert.IsType<BridgeEvent.TokenDelta>(second);
        Assert.Null(legacy.Text);
        Assert.Equal(9, legacy.Length);
    }

    [Fact]
    public void ReadEvent_AssistantMessageCarriesText_ErrorCarriesMessage()
    {
        var reader = new StringReader("{\"type\":\"assistant_message\",\"text\":\"回复内容\"}\n{\"type\":\"error\",\"message\":\"boom\"}\n");
        BridgeProtocol.TryReadEvent(reader, out var first, out _);
        Assert.Equal("回复内容", Assert.IsType<BridgeEvent.AssistantMessage>(first).Text);

        BridgeProtocol.TryReadEvent(reader, out var second, out _);
        Assert.Equal("boom", Assert.IsType<BridgeEvent.Error>(second).Message);
    }

    [Fact]
    public void ReadEvent_EofAtStreamEnd()
    {
        var reader = new StringReader("{\"type\":\"turn_started\"}\n");
        BridgeProtocol.TryReadEvent(reader, out _, out _);
        var result = BridgeProtocol.TryReadEvent(reader, out _, out _);
        Assert.Equal(BridgeReadResult.Eof, result);
    }

    [Fact]
    public void ReadEvent_EmptyLine_SkippedWithoutDiagnostic()
    {
        var reader = new StringReader("\n\n{\"type\":\"turn_completed\"}\n");
        var result = BridgeProtocol.TryReadEvent(reader, out var @event, out var diagnostic);
        Assert.Equal(BridgeReadResult.Event, result);
        Assert.IsType<BridgeEvent.TurnCompleted>(@event);
        Assert.Null(diagnostic);
    }

    [Fact]
    public void ReadEvent_UnknownType_SkippedWithInductiveDiagnostic()
    {
        var reader = new StringReader("{\"type\":\"warp_drive\"}\n{\"type\":\"turn_completed\"}\n");
        var result = BridgeProtocol.TryReadEvent(reader, out var @event, out var diagnostic);
        Assert.Equal(BridgeReadResult.Skipped, result);
        Assert.Null(@event);
        // 诊断必须解释：是什么触发 + 为什么跳过 + 支持的合法类型清单（诱导方式）
        Assert.NotNull(diagnostic);
        Assert.Contains("warp_drive", diagnostic);
        Assert.Contains("turn_started", diagnostic);

        // 跳过后继续读后续合法事件
        var next = BridgeProtocol.TryReadEvent(reader, out var resumed, out _);
        Assert.Equal(BridgeReadResult.Event, next);
        Assert.IsType<BridgeEvent.TurnCompleted>(resumed);
    }

    [Fact]
    public void ReadEvent_InvalidJson_SkippedWithDiagnostic()
    {
        var reader = new StringReader("not json at all\n");
        var result = BridgeProtocol.TryReadEvent(reader, out _, out var diagnostic);
        Assert.Equal(BridgeReadResult.Skipped, result);
        Assert.NotNull(diagnostic);
    }

    [Fact]
    public void ReadRequest_Submit_ParsesText()
    {
        var reader = new StringReader("{\"type\":\"submit\",\"text\":\"你好 world\"}\n");
        var result = BridgeProtocol.TryReadRequest(reader, out var request, out var diagnostic);
        Assert.Equal(BridgeReadResult.Event, result);
        var submit = Assert.IsType<BridgeRequest.Submit>(request);
        Assert.Equal("你好 world", submit.Text);
        Assert.Null(diagnostic);
    }

    /// <summary>submit 带图片附件 JSON 形态（TASK066 板块 G G4 — images 数组 camelCase
    /// base64/mediaType/filename，与 jcc 侧 BridgeRequest.Submit 逐字段镜像，
    /// 引擎侧 BridgeJsonContractTests 锁定防漂移；Default 上下文转义非 ASCII — 形态断言用 ASCII）。</summary>
    [Fact]
    public void WriteRequest_SubmitWithImages_CarriesImagesArray()
    {
        var writer = new StringWriter();
        BridgeProtocol.WriteRequest(writer, new BridgeRequest.Submit("look at this",
            [new BridgeImageAttachment("aGk=", "image/png", "shot.png")]));
        Assert.Equal(
            "{\"type\":\"submit\",\"text\":\"look at this\",\"images\":[{\"base64\":\"aGk=\",\"mediaType\":\"image/png\",\"filename\":\"shot.png\"}]}\n",
            writer.ToString());
    }

    /// <summary>submit 显式 null 附件 — 与旧 JSON 字节等价（Images=null 默认 + WhenWritingNull
    /// 不序列化，旧对端零破坏 — 对齐 DiffLine 行号字段扩展先例）。</summary>
    [Fact]
    public void WriteRequest_SubmitWithNullImages_MatchesLegacyJsonByteForByte()
    {
        var writer = new StringWriter();
        BridgeProtocol.WriteRequest(writer, new BridgeRequest.Submit("hello world", Images: null));
        Assert.Equal("{\"type\":\"submit\",\"text\":\"hello world\"}\n", writer.ToString());
    }

    /// <summary>带图 submit roundtrip — 序列化行反解析全字段回读（record 数组字段引用相等，逐项断言；
    /// 中文经 Default 上下文转义后值等价回读）。</summary>
    [Fact]
    public void Roundtrip_SubmitWithImages_SurvivesSerializeDeserialize()
    {
        var line = BridgeProtocol.SerializeRequest(new BridgeRequest.Submit("两张图",
            [
                new BridgeImageAttachment("aGk=", "image/png", "a.png"),
                new BridgeImageAttachment("eQ==", "image/jpeg", "b.jpg"),
            ]));
        var reader = new StringReader(line + "\n");

        var result = BridgeProtocol.TryReadRequest(reader, out var request, out var diagnostic);

        Assert.Equal(BridgeReadResult.Event, result);
        Assert.Null(diagnostic);
        var submit = Assert.IsType<BridgeRequest.Submit>(request);
        Assert.Equal("两张图", submit.Text);
        Assert.NotNull(submit.Images);
        Assert.Equal(2, submit.Images.Length);
        Assert.Equal(new BridgeImageAttachment("aGk=", "image/png", "a.png"), submit.Images[0]);
        Assert.Equal(new BridgeImageAttachment("eQ==", "image/jpeg", "b.jpg"), submit.Images[1]);
    }

    /// <summary>jcc 侧产出的带图行反解析回读（双镜像 roundtrip — 对齐 StopTask 测试形态）。</summary>
    [Fact]
    public void ReadRequest_SubmitWithImagesLine_ParsesAllFields()
    {
        var reader = new StringReader(
            "{\"type\":\"submit\",\"text\":\"hi\",\"images\":[{\"base64\":\"aGk=\",\"mediaType\":\"image/png\",\"filename\":\"shot.png\"}]}\n");

        var result = BridgeProtocol.TryReadRequest(reader, out var request, out var diagnostic);

        Assert.Equal(BridgeReadResult.Event, result);
        var submit = Assert.IsType<BridgeRequest.Submit>(request);
        Assert.Equal("hi", submit.Text);
        var image = Assert.Single(submit.Images!);
        Assert.Equal(new BridgeImageAttachment("aGk=", "image/png", "shot.png"), image);
        Assert.Null(diagnostic);
    }

    /// <summary>旧引擎（G4 前）不输出 images 字段 → 反序列化 Images=null 不报坏行（旧协议兼容面）。</summary>
    [Fact]
    public void ReadRequest_SubmitLegacyWithoutImages_ImagesStaysNull()
    {
        var reader = new StringReader("{\"type\":\"submit\",\"text\":\"legacy\"}\n");

        var result = BridgeProtocol.TryReadRequest(reader, out var request, out var diagnostic);

        Assert.Equal(BridgeReadResult.Event, result);
        var submit = Assert.IsType<BridgeRequest.Submit>(request);
        Assert.Equal("legacy", submit.Text);
        Assert.Null(submit.Images);
        Assert.Null(diagnostic);
    }

    [Fact]
    public void ReadRequest_InterruptAndQuit_ParseByType()
    {
        var reader = new StringReader("{\"type\":\"interrupt\"}\n{\"type\":\"quit\"}\n");
        Assert.Equal(BridgeReadResult.Event, BridgeProtocol.TryReadRequest(reader, out var first, out _));
        Assert.IsType<BridgeRequest.Interrupt>(first);
        Assert.Equal(BridgeReadResult.Event, BridgeProtocol.TryReadRequest(reader, out var second, out _));
        Assert.IsType<BridgeRequest.Quit>(second);
    }

    [Fact]
    public void ReadRequest_UnknownType_SkippedWithInductiveDiagnostic()
    {
        var reader = new StringReader("{\"type\":\"teleport\"}\n{\"type\":\"quit\"}\n");
        var result = BridgeProtocol.TryReadRequest(reader, out var request, out var diagnostic);
        Assert.Equal(BridgeReadResult.Skipped, result);
        Assert.Null(request);
        Assert.NotNull(diagnostic);
        Assert.Contains("teleport", diagnostic);
        Assert.Contains("submit", diagnostic);

        Assert.Equal(BridgeReadResult.Event, BridgeProtocol.TryReadRequest(reader, out var resumed, out _));
        Assert.IsType<BridgeRequest.Quit>(resumed);
    }

    [Fact]
    public void ReadRequest_EmptyLineSkipped_EofAtStreamEnd()
    {
        var reader = new StringReader("\n{\"type\":\"interrupt\"}\n");
        Assert.Equal(BridgeReadResult.Event, BridgeProtocol.TryReadRequest(reader, out _, out _));
        Assert.Equal(BridgeReadResult.Eof, BridgeProtocol.TryReadRequest(reader, out _, out _));
    }

    [Fact]
    public void Roundtrip_ToolResultWithPatch_SurvivesSerializeDeserialize()
    {
        var withPatch = new BridgeEvent.ToolResult("Edited a.cs", [
            new DiffHunk
            {
                Header = "@@ -1,3 +1,4 @@",
                Lines =
                [
                    new DiffLine { Kind = DiffLineKind.Context, Content = "keep" },
                    new DiffLine { Kind = DiffLineKind.Removed, Content = "drop" },
                    new DiffLine { Kind = DiffLineKind.Added, Content = "add" },
                ],
            },
        ]);

        var line = BridgeProtocol.SerializeEvent(withPatch);
        var reader = new StringReader(line + "\n");

        var result = BridgeProtocol.TryReadEvent(reader, out var actual, out var diagnostic);
        Assert.Equal(BridgeReadResult.Event, result);
        Assert.Null(diagnostic);
        // record 含数组字段为引用相等 — 逐字段断言（hunk/行/种类/内容全量回读）
        var roundTripped = Assert.IsType<BridgeEvent.ToolResult>(actual);
        Assert.Equal("Edited a.cs", roundTripped.Summary);
        var hunk = Assert.Single(roundTripped.Patch!);
        Assert.Equal("@@ -1,3 +1,4 @@", hunk.Header);
        Assert.Equal(3, hunk.Lines.Length);
        Assert.Equal(DiffLineKind.Context, hunk.Lines[0].Kind);
        Assert.Equal("keep", hunk.Lines[0].Content);
        Assert.Equal(DiffLineKind.Removed, hunk.Lines[1].Kind);
        Assert.Equal("drop", hunk.Lines[1].Content);
        Assert.Equal(DiffLineKind.Added, hunk.Lines[2].Kind);
        Assert.Equal("add", hunk.Lines[2].Content);
    }

    [Fact]
    public void Roundtrip_ToolResultWithPatchLineNumbers_SurvivesSerializeDeserialize()
    {
        // TASK066 F1 行号贯通：oldLineNumber/newLineNumber camelCase 序列化 + 反序列化回读
        //（对侧 null 不输出 — 与引擎侧 BridgeEventConsumer 投影逐字节镜像）
        var withPatch = new BridgeEvent.ToolResult("Edited a.cs", [
            new DiffHunk
            {
                Header = "@@ -1,2 +1,3 @@",
                Lines =
                [
                    new DiffLine { Kind = DiffLineKind.Removed, Content = "old", OldLineNumber = 1 },
                    new DiffLine { Kind = DiffLineKind.Added, Content = "new", NewLineNumber = 1 },
                    new DiffLine { Kind = DiffLineKind.Context, Content = "keep", OldLineNumber = 2, NewLineNumber = 2 },
                ],
            },
        ]);

        var line = BridgeProtocol.SerializeEvent(withPatch);
        Assert.Contains("\"oldLineNumber\":1", line);
        Assert.Contains("\"newLineNumber\":1", line);
        Assert.DoesNotContain("oldLineNumber\":null", line);

        var reader = new StringReader(line + "\n");
        var result = BridgeProtocol.TryReadEvent(reader, out var actual, out var diagnostic);
        Assert.Equal(BridgeReadResult.Event, result);
        Assert.Null(diagnostic);
        var roundTripped = Assert.IsType<BridgeEvent.ToolResult>(actual);
        var hunk = Assert.Single(roundTripped.Patch!);
        Assert.Equal(1, hunk.Lines[0].OldLineNumber);
        Assert.Null(hunk.Lines[0].NewLineNumber);
        Assert.Equal(1, hunk.Lines[1].NewLineNumber);
        Assert.Null(hunk.Lines[1].OldLineNumber);
        Assert.Equal(2, hunk.Lines[2].OldLineNumber);
        Assert.Equal(2, hunk.Lines[2].NewLineNumber);
    }

    [Fact]
    public void SerializeEvent_ToolResultWithoutPatch_OmitsPatchField()
    {
        // null 补丁不输出 patch 字段（WhenWritingNull — 与引擎侧契约逐字节一致）
        var line = BridgeProtocol.SerializeEvent(new BridgeEvent.ToolResult("41 lines"));
        Assert.Equal("{\"type\":\"tool_result\",\"summary\":\"41 lines\"}", line);
    }

    [Fact]
    public void ReadEvent_ToolResultWithoutPatchField_ParsesLegacyJson()
    {
        // 旧引擎（M7-2 投影同步前）不输出 patch 字段 → 反序列化为 null，不报坏行
        var reader = new StringReader("{\"type\":\"tool_result\",\"summary\":\"41 lines\"}\n");

        var result = BridgeProtocol.TryReadEvent(reader, out var actual, out var diagnostic);

        Assert.Equal(BridgeReadResult.Event, result);
        var toolResult = Assert.IsType<BridgeEvent.ToolResult>(actual);
        Assert.Equal("41 lines", toolResult.Summary);
        Assert.Null(toolResult.Patch);
        Assert.Null(diagnostic);
    }

    [Fact]
    public void Roundtrip_AllEvents_SurviveSerializeDeserialize()
    {
        BridgeEvent[] events =
        [
            new BridgeEvent.SessionInfo("2.1.303", null, "auto", false, @"D:\engine"),
            new BridgeEvent.PermissionModeChanged("bypass"),
            new BridgeEvent.TurnStarted(),
            new BridgeEvent.ThinkingStarted(),
            new BridgeEvent.ThinkingEnded(4321),
            new BridgeEvent.ToolCall("Bash", "ls -la"),
            new BridgeEvent.ToolProgress("Bash", "bash_progress", "line1\nline2"),
            new BridgeEvent.ToolResult("3 files"),
            new BridgeEvent.TokenDelta(12),
            new BridgeEvent.AssistantMessage("完整回复 — 包含中文与 \"引号\""),
            new BridgeEvent.TurnCompleted(),
            new BridgeEvent.TurnInterrupted(),
            new BridgeEvent.Error("出错了：参数缺失"),
        ];

        var writer = new StringWriter();
        foreach (var bridgeEvent in events)
        {
            writer.WriteLine(BridgeProtocol.SerializeEvent(bridgeEvent));
        }

        var reader = new StringReader(writer.ToString());
        foreach (var expected in events)
        {
            var result = BridgeProtocol.TryReadEvent(reader, out var actual, out var diagnostic);
            Assert.Equal(BridgeReadResult.Event, result);
            Assert.NotNull(actual);
            Assert.Equal(expected, actual);
            Assert.Null(diagnostic);
        }
    }

    /// <summary>工具进度事件 JSON 形状（TASK065 P3 — 与引擎侧 BridgeEvent.ToolProgress 逐字段镜像：
    /// tool_progress + toolName/progressType/message camelCase 字段；message 多行经 \n 转义保真）。</summary>
    [Fact]
    public void SerializeEvent_ToolProgress_CarriesCamelCaseFields()
    {
        var line = BridgeProtocol.SerializeEvent(new BridgeEvent.ToolProgress("Bash", "bash_progress", "line1\nline2"));

        Assert.Equal(
            "{\"type\":\"tool_progress\",\"toolName\":\"Bash\",\"progressType\":\"bash_progress\",\"message\":\"line1\\nline2\"}",
            line);
    }

    /// <summary>旧引擎（tool_progress 前）不发射该类型 — 新 TUI 反序列化兼容面不受影响；
    /// 引擎产出的进度行反解析回读（record 位置字段全量校验）。</summary>
    [Fact]
    public void ReadEvent_ToolProgressLine_ParsesAllFields()
    {
        var reader = new StringReader("{\"type\":\"tool_progress\",\"toolName\":\"PowerShell\",\"progressType\":\"ps_progress\",\"message\":\"ok\"}\n");

        var result = BridgeProtocol.TryReadEvent(reader, out var actual, out var diagnostic);

        Assert.Equal(BridgeReadResult.Event, result);
        var progress = Assert.IsType<BridgeEvent.ToolProgress>(actual);
        Assert.Equal(("PowerShell", "ps_progress", "ok"), (progress.ToolName, progress.ProgressType, progress.Message));
        Assert.Null(diagnostic);
    }
}
