namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// TASK066 C8 <c>sessionFeedback/record</c> 测试：反馈只写会话日志事件（不启动模型工作），
/// 未知会话返回契约错误联合体（而不是网关失败），分类词汇做白名单校验。
/// </summary>
public sealed class DshSessionFeedbackTests {
    private const string SessionsDir = "/test/sessions";
    private const string JournalDir = "/test/dsh_journal";

    /// <summary>建一个带真实转录与事件日志的宿主服务与分发器</summary>
    private static async Task<(DshHostServices Services, DshRpcDispatcher Dispatcher, State.TranscriptService Transcripts)> CreateHostAsync() {
        var fs = new InMemoryFileSystem();
        var transcripts = new State.TranscriptService(fs, SessionsDir);
        var services = new DshHostServices(fs, "/test/dsh-workspaces.json", JournalDir);
        services.Sessions.AttachTranscripts(transcripts);
        await services.Settings.InitializeAsync(CancellationToken.None);
        await services.Feedback.InitializeAsync(CancellationToken.None);
        await transcripts.AppendEntryAsync("s1", new TranscriptEntry { Role = "user", Content = "请排查慢查询" });
        return (services, new DshRpcDispatcher(services), transcripts);
    }

    /// <summary>按 DSH 远程契约构造 request 信封参数</summary>
    private static JsonElement Envelope(DshSessionFeedbackRecordArgs payload) =>
        JsonSerializer.SerializeToElement(
            new DshRequestEnvelope<DshSessionFeedbackRecordArgs> { Request = payload },
            DshJsonContext.Default.DshRequestEnvelopeDshSessionFeedbackRecordArgs);

    [Fact]
    public async Task SessionFeedbackRecord_AppendsLogOnlyEventWithTrimmedText() {
        var (services, dispatcher, _) = await CreateHostAsync();

        var result = await dispatcher.HandleAsync("sessionFeedback/record", Envelope(new DshSessionFeedbackRecordArgs {
            SessionId = "s1",
            Text = "  回答偏慢  ",
            Category = "service-stability",
        }));

        result.Ok.Should().BeTrue(result.Error?.Message);
        var payload = result.Value.GetValueOrDefault();
        payload.GetProperty("ok").GetBoolean().Should().BeTrue();
        payload.GetProperty("value").GetProperty("recorded").GetBoolean().Should().BeTrue();

        var events = await services.Journal.LoadAsync("s1", CancellationToken.None);
        var recorded = events.Single(sessionEvent => sessionEvent.Type == "feedback/record");
        recorded.SurfaceOp.Should().BeNull("反馈是日志事件，不是会话表面消息");
        recorded.Data.GetProperty("text").GetString().Should().Be("回答偏慢");
        recorded.Data.GetProperty("category").GetString().Should().Be("service-stability");
        events.Should().NotContain(sessionEvent =>
            sessionEvent.Type == "assistant/message" || sessionEvent.Type == "turn/start" || sessionEvent.Type == "turn/end");
    }

    [Fact]
    public async Task SessionFeedbackRecord_OmitsBlankTextAndOptionalCategory() {
        var (services, dispatcher, _) = await CreateHostAsync();

        var result = await dispatcher.HandleAsync("sessionFeedback/record", Envelope(new DshSessionFeedbackRecordArgs {
            SessionId = "s1",
            Text = "   ",
        }));

        result.Ok.Should().BeTrue(result.Error?.Message);
        var recorded = (await services.Journal.LoadAsync("s1", CancellationToken.None))
            .Single(sessionEvent => sessionEvent.Type == "feedback/record");
        recorded.Data.TryGetProperty("text", out _).Should().BeFalse("空白备注按未提供处理");
        recorded.Data.TryGetProperty("category", out _).Should().BeFalse();
    }

    [Fact]
    public async Task SessionFeedbackRecord_UnknownSessionReturnsContractError() {
        var (_, dispatcher, _) = await CreateHostAsync();

        var result = await dispatcher.HandleAsync("sessionFeedback/record", Envelope(new DshSessionFeedbackRecordArgs {
            SessionId = "missing",
            Text = "有一条反馈",
            Category = "other",
        }));

        result.Ok.Should().BeTrue(result.Error?.Message);
        var payload = result.Value.GetValueOrDefault();
        payload.GetProperty("ok").GetBoolean().Should().BeFalse();
        var error = payload.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("session-not-found");
        error.GetProperty("sessionId").GetString().Should().Be("missing");
    }

    [Fact]
    public async Task SessionFeedbackRecord_RejectsMissingSessionAndUnknownCategory() {
        var (_, dispatcher, _) = await CreateHostAsync();

        var noSession = await dispatcher.HandleAsync("sessionFeedback/record", Envelope(new DshSessionFeedbackRecordArgs {
            SessionId = " ",
            Text = "反馈",
        }));
        noSession.Ok.Should().BeFalse();
        noSession.Error?.Code.Should().Be("gateway/bad-request");
        noSession.Error?.Message.Should().Contain("sessionId");

        var badCategory = await dispatcher.HandleAsync("sessionFeedback/record", Envelope(new DshSessionFeedbackRecordArgs {
            SessionId = "s1",
            Category = "bogus",
        }));
        badCategory.Ok.Should().BeFalse();
        badCategory.Error?.Code.Should().Be("gateway/bad-request");
        badCategory.Error?.Message.Should().Contain("other");
        badCategory.Error?.Message.Should().Contain("service-stability");
        badCategory.Error?.Message.Should().Contain("security-privacy-permission");
    }
}
