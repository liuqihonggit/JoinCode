namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// 引擎转录 → DSH 会话事件投影测试（ADR 0143 M2-c）：历史消息要按 DSH 的 turn/step 脚手架落成事件，
/// 前端才能把打开快照渲染成聊天记录（`surfaceOp='append'` 标记消息属于会话表面）。
/// </summary>
public sealed class DshTranscriptProjectorTests {
    private static readonly DateTime T0 = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>按引擎转录形状造一条消息条目</summary>
    private static TranscriptEntry Message(string role, string content, int seconds = 0) =>
        new() { SessionId = "s1", Role = role, Content = content, Timestamp = T0.AddSeconds(seconds) };

    [Fact]
    public void Project_EmptyTranscript_ReturnsEmpty() {
        DshTranscriptProjector.Project("s1", []).Should().BeEmpty();
    }

    [Fact]
    public void Project_UserAndAssistant_EmitsTurnScaffold() {
        var records = DshTranscriptProjector.Project("s1", [
            Message("user", "之前的问题"),
            Message("assistant", "之前的回答", 5),
        ]);

        records.Select(record => record.Event.Type).Should().Equal(
            "turn/start", "step/start", "user/message", "assistant/message", "step/end", "turn/end");
        records.Select(record => record.Event.Seq).Should().Equal(0L, 1L, 2L, 3L, 4L, 5L);
        records.Should().OnlyContain(record => record.Type == "event");

        var turnStart = records[0].Event.Data;
        turnStart.GetProperty("turn").GetInt32().Should().Be(1);
        var stepStart = records[1].Event.Data;
        stepStart.GetProperty("turn").GetInt32().Should().Be(1);
        stepStart.GetProperty("step").GetInt32().Should().Be(1);

        var user = records[2];
        user.Event.SurfaceOp.Should().Be("append");
        user.Event.Time.Should().Be(new DateTimeOffset(T0).ToUnixTimeMilliseconds());
        user.Event.Data.GetProperty("role").GetString().Should().Be("user");
        user.Event.Data.GetProperty("content")[0].GetProperty("text").GetString().Should().Be("之前的问题");
        user.Event.Data.GetProperty("source").GetProperty("kind").GetString().Should().Be("user");

        var assistant = records[3];
        assistant.Event.SurfaceOp.Should().Be("append");
        var assistantData = assistant.Event.Data;
        assistantData.GetProperty("turn").GetInt32().Should().Be(1);
        assistantData.GetProperty("step").GetInt32().Should().Be(1);
        assistantData.GetProperty("message").GetProperty("role").GetString().Should().Be("assistant");
        assistantData.GetProperty("message").GetProperty("content")[0].GetProperty("text").GetString().Should().Be("之前的回答");
        assistantData.GetProperty("stream").GetArrayLength().Should().Be(0);

        records[4].Event.Data.GetProperty("step").GetInt32().Should().Be(1);
        records[5].Event.Data.GetProperty("reason").GetProperty("kind").GetString().Should().Be("completed");
        records[0].Event.SurfaceOp.Should().BeNull();
    }

    [Fact]
    public void Project_SkipsMetadataAndSidechainEntries() {
        var records = DshTranscriptProjector.Project("s1", [
            new TranscriptEntry { SessionId = "s1", Role = "system", Content = "Session renamed to: x", Type = "custom-title", CustomTitle = "x", Timestamp = T0 },
            new TranscriptEntry { SessionId = "s1", Role = "user", Content = "侧链消息", IsSidechain = true, Timestamp = T0 },
            Message("user", "真消息", 1),
        ]);

        records.Select(record => record.Event.Type).Should().Equal(
            "turn/start", "step/start", "user/message", "step/end", "turn/end");
        records[2].Event.Data.GetProperty("content")[0].GetProperty("text").GetString().Should().Be("真消息");
    }

    [Fact]
    public void Project_EachUserMessage_StartsNextTurn() {
        var records = DshTranscriptProjector.Project("s1", [
            Message("user", "一"),
            Message("assistant", "答一", 1),
            Message("user", "二", 2),
            Message("assistant", "答二", 3),
        ]);

        records.Select(record => record.Event.Type).Should().Equal(
            "turn/start", "step/start", "user/message", "assistant/message", "step/end", "turn/end",
            "turn/start", "step/start", "user/message", "assistant/message", "step/end", "turn/end");
        records.Where(record => record.Event.Type == "turn/start")
            .Select(record => record.Event.Data.GetProperty("turn").GetInt32())
            .Should().Equal(1, 2);
        records.Select(record => record.Event.Seq).Should().Equal(Enumerable.Range(0, 12).Select(value => (long)value));
    }

    [Fact]
    public void Project_AssistantOnly_StillOpensOneTurn() {
        var records = DshTranscriptProjector.Project("s1", [Message("assistant", "只有回答")]);

        records.Select(record => record.Event.Type).Should().Equal(
            "turn/start", "step/start", "assistant/message", "step/end", "turn/end");
        records[2].Event.Data.GetProperty("turn").GetInt32().Should().Be(1);
    }
}
