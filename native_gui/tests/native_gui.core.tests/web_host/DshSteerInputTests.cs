namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// session/prompt mode=steer 轮内注入测试（B11，对齐 DSH <c>agent.steer</c> +
/// <c>dsh-api-session-controller</c>：运行中受理即回 accepted、不排队不开新轮；空闲时回落正常开轮；
/// 引擎未装配注入通道时诚实拒绝 session/steer-unavailable）。
/// </summary>
public sealed class DshSteerInputTests {
    private static JsonElement PromptArgs(string sessionId, string text, string mode) =>
        JsonSerializer.SerializeToElement(new {
            request = new {
                requestId = Guid.NewGuid().ToString("N"),
                sessionId,
                mode,
                content = new[] { new { type = "text", text } },
            },
        });

    /// <summary>运行中轮内注入：受理、投递到引擎、不新开轮次。</summary>
    [Fact]
    public async Task SteerWhileRunning_DeliversToEngine_WithoutOpeningNewTurn() {
        var fs = new InMemoryFileSystem();
        var services = new DshHostServices(fs);
        await using var transcripts = new State.TranscriptService(fs, "/test/sessions");
        services.Sessions.AttachTranscripts(transcripts);
        var hold = new TaskCompletionSource();
        await using var engine = new PlaceholderChatSession {
            TurnGate = ct => hold.Task.WaitAsync(ct),
            SteerSink = (_, _) => true,
        };
        services.AttachEngine(engine);
        var dispatcher = new DshRpcDispatcher(services);
        try {
            var created = await services.Sessions.CreateAsync(null, "/test/proj", "deepseek", "deepseek-v4-pro", CancellationToken.None);
            var sessionId = (created ?? throw new Xunit.Sdk.XunitException("session/create 失败")).SessionId;
            (await dispatcher.HandleAsync("session/prompt", PromptArgs(sessionId, "第一问", "send"))).Ok.Should().BeTrue();
            services.Streams.RunningTurn(sessionId).Should().Be(1);

            var result = await dispatcher.HandleAsync("session/prompt", PromptArgs(sessionId, "轮内插一句", "steer"));

            result.Ok.Should().BeTrue(result.Error?.Message);
            engine.LastSteer.Should().Be((sessionId, "轮内插一句"));
            services.Streams.RunningTurn(sessionId).Should().Be(1, "轮内注入不得占用新轮次");
            dispatcher.ListQueue(sessionId).Should().BeEmpty("steer 不是排队语义");
        } finally {
            hold.TrySetCanceled();
        }
    }

    /// <summary>空闲时 mode=steer：回落为正常开轮（对齐「没有可注入的轮次就当作新输入」）。</summary>
    [Fact]
    public async Task SteerWhileIdle_FallsBackToNewTurn() {
        var fs = new InMemoryFileSystem();
        var services = new DshHostServices(fs);
        await using var transcripts = new State.TranscriptService(fs, "/test/sessions");
        services.Sessions.AttachTranscripts(transcripts);
        await using var engine = new PlaceholderChatSession { SteerSink = (_, _) => true };
        services.AttachEngine(engine);
        var dispatcher = new DshRpcDispatcher(services);
        var created = await services.Sessions.CreateAsync(null, "/test/proj", "deepseek", "deepseek-v4-pro", CancellationToken.None);
        var sessionId = (created ?? throw new Xunit.Sdk.XunitException("session/create 失败")).SessionId;

        var result = await dispatcher.HandleAsync("session/prompt", PromptArgs(sessionId, "空闲时插入", "steer"));

        result.Ok.Should().BeTrue(result.Error?.Message);
        engine.LastSteer.Should().BeNull("没有运行中的轮次，不该走注入通道");
        services.Streams.RunningTurn(sessionId).Should().Be(1, "空闲 steer 必须回落为正常开轮");
    }

    /// <summary>引擎未装配注入邮箱：诚实拒绝并引导，不排队也不开轮。</summary>
    [Fact]
    public async Task SteerWhileRunning_EngineWithoutMailbox_ReturnsSteerUnavailable() {
        var fs = new InMemoryFileSystem();
        var services = new DshHostServices(fs);
        await using var transcripts = new State.TranscriptService(fs, "/test/sessions");
        services.Sessions.AttachTranscripts(transcripts);
        var hold = new TaskCompletionSource();
        await using var engine = new PlaceholderChatSession { TurnGate = ct => hold.Task.WaitAsync(ct) };
        services.AttachEngine(engine);
        var dispatcher = new DshRpcDispatcher(services);
        try {
            var created = await services.Sessions.CreateAsync(null, "/test/proj", "deepseek", "deepseek-v4-pro", CancellationToken.None);
            var sessionId = (created ?? throw new Xunit.Sdk.XunitException("session/create 失败")).SessionId;
            (await dispatcher.HandleAsync("session/prompt", PromptArgs(sessionId, "第一问", "send"))).Ok.Should().BeTrue();

            var result = await dispatcher.HandleAsync("session/prompt", PromptArgs(sessionId, "轮内插一句", "steer"));

            result.Ok.Should().BeFalse();
            result.Error?.Code.Should().Be("session/steer-unavailable");
            result.Error?.Message.Should().Contain("mode=queue", "必须给出可执行的替代路径");
            dispatcher.ListQueue(sessionId).Should().BeEmpty();
        } finally {
            hold.TrySetCanceled();
        }
    }
}
