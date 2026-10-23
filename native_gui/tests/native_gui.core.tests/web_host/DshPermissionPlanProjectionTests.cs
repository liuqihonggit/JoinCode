namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// C6 计划/权限投影测试：前端计划 chip 与输入框权限选择器读取宿主计算投影
/// （<c>plan {active,pending}</c>、<c>permissions {currentValue}</c>），
/// 权限预设变化必须同时写引擎、落设置、推 <c>plan/mode</c> 事件与 <c>session/control</c> 投影帧。
/// </summary>
public sealed class DshPermissionPlanProjectionTests {
    private const string SessionId = "s-plan";

    /// <summary>构造带一个真实会话条目的宿主服务</summary>
    private static async Task<(DshHostServices Services, State.TranscriptService Transcripts, PlaceholderChatSession Engine)> CreateAsync() {
        var fs = new InMemoryFileSystem();
        var transcripts = new State.TranscriptService(fs, "/test/sessions");
        await transcripts.AppendEntryAsync(SessionId, new TranscriptEntry {
            Role = "user",
            Content = "写个计划",
            Timestamp = new DateTime(2026, 10, 23, 10, 0, 0, DateTimeKind.Utc),
        });
        var services = new DshHostServices(fs);
        services.Sessions.AttachTranscripts(transcripts);
        var engine = new PlaceholderChatSession();
        services.AttachEngine(engine);
        return (services, transcripts, engine);
    }

    private static JsonElement ExecuteArgs(string line) =>
        JsonSerializer.SerializeToElement(
            new DshCommandsExecuteArgs { AgentId = SessionId, Line = line },
            DshJsonContext.Default.DshCommandsExecuteArgs);

    private static DshSessionControlProjection ControlFrame(string key, bool active, long seq) =>
        new() {
            SessionId = SessionId,
            Key = key,
            Value = JsonSerializer.SerializeToElement(
                new DshSessionPlanProjection { Active = active },
                DshJsonContext.Default.DshSessionPlanProjection),
            Seq = seq,
        };

    [Fact]
    public async Task Projections_DefaultToAutoPermissionsAndInactivePlan() {
        var (services, transcripts, _) = await CreateAsync();
        await using var _transcripts = transcripts;

        var snapshot = await services.Sessions.ProjectionsAsync(SessionId, CancellationToken.None)
            ?? throw new Xunit.Sdk.XunitException("期望会话投影存在");

        snapshot.Values.Permissions.CurrentValue.Should().Be("auto", "前端权限选择器要拿到引擎默认预设");
        snapshot.Values.Plan.Active.Should().BeFalse("默认不是计划模式");
        snapshot.Values.Plan.Pending.Should().BeFalse("宿主立即应用预设，没有待生效切换");
    }

    [Fact]
    public async Task ApplyPermissionPreset_Plan_UpdatesEngineSettingsProjectionAndStreams() {
        var (services, transcripts, engine) = await CreateAsync();
        await using var _transcripts = transcripts;
        var events = new List<DshSessionEvent>();
        var frames = new List<DshSessionControlProjection>();
        using var subscription = services.Streams.Subscribe(SessionId, "stream-plan", sessionEvent => {
            events.Add(sessionEvent);
            return Task.CompletedTask;
        });
        using var control = services.Control.Attach(frame => {
            frames.Add(frame);
            return Task.CompletedTask;
        });

        var (ok, message) = await services.ApplyPermissionPresetAsync("plan", CancellationToken.None);

        ok.Should().BeTrue(message);
        engine.CurrentPermissionMode.Should().Be(PermissionMode.Plan, "预设必须真正作用到引擎");
        var planEvent = events.SingleOrDefault(sessionEvent => sessionEvent.Type == "plan/mode")
            ?? throw new Xunit.Sdk.XunitException("期望推送 plan/mode 事件");
        planEvent.Data.GetProperty("active").GetBoolean().Should().BeTrue();

        frames.Should().Contain(frame => frame.Key == "plan" && frame.Value.GetProperty("active").GetBoolean(),
            "计划 chip 依赖宿主计算投影");
        frames.Should().Contain(frame => frame.Key == "permissions" && frame.Value.GetProperty("currentValue").GetString() == "plan",
            "输入框权限选择器依赖 permissions 投影");

        var snapshot = await services.Sessions.ProjectionsAsync(SessionId, CancellationToken.None)
            ?? throw new Xunit.Sdk.XunitException("期望会话投影存在");
        snapshot.Values.Plan.Active.Should().BeTrue();
        snapshot.Values.Permissions.CurrentValue.Should().Be("plan");

        var permission = services.Settings.Describe().Namespaces.Single(ns => ns.Ns == "permission");
        permission.Value.GetProperty("defaultPreset").GetString().Should().Be("plan", "权限预设必须落盘");
    }

    [Fact]
    public async Task ApplyPermissionPreset_UnknownPreset_RejectsWithGuidanceWithoutTouchingEngine() {
        var (services, transcripts, engine) = await CreateAsync();
        await using var _transcripts = transcripts;

        var (ok, message) = await services.ApplyPermissionPresetAsync("danger-full-access", CancellationToken.None);

        ok.Should().BeFalse();
        message.Should().Contain("plan/auto/ask/bypass/unattended");
        engine.LastSetPermissionMode.Should().BeNull("非法预设不得作用到引擎");
    }

    [Fact]
    public async Task ApplyPermissionPreset_WithoutEngine_ReturnsNotReady() {
        var services = new DshHostServices(new InMemoryFileSystem());

        var (ok, message) = await services.ApplyPermissionPresetAsync("plan", CancellationToken.None);

        ok.Should().BeFalse();
        message.Should().Contain("引擎");
    }

    [Fact]
    public async Task CommandsExecute_PlanOff_ExitsPlanModeAndBroadcastsInactive() {
        var (services, transcripts, engine) = await CreateAsync();
        await using var _transcripts = transcripts;
        await services.ApplyPermissionPresetAsync("plan", CancellationToken.None);
        var events = new List<DshSessionEvent>();
        var frames = new List<DshSessionControlProjection>();
        using var subscription = services.Streams.Subscribe(SessionId, "stream-off", sessionEvent => {
            events.Add(sessionEvent);
            return Task.CompletedTask;
        });
        using var control = services.Control.Attach(frame => {
            frames.Add(frame);
            return Task.CompletedTask;
        });

        var result = await new DshRpcDispatcher(services).HandleAsync("commands/execute", ExecuteArgs("/plan off"));

        result.Ok.Should().BeTrue(result.Error?.Message);
        engine.CurrentPermissionMode.Should().Be(PermissionMode.Auto, "chip 的 /plan off 必须退出引擎计划模式");
        events.LastOrDefault(sessionEvent => sessionEvent.Type == "plan/mode")
            ?.Data.GetProperty("active").GetBoolean().Should().BeFalse();
        frames.Should().Contain(frame => frame.Key == "plan" && !frame.Value.GetProperty("active").GetBoolean());
        services.Settings.Describe().Namespaces.Single(ns => ns.Ns == "permission")
            .Value.GetProperty("defaultPreset").GetString().Should().Be("auto");
    }

    [Fact]
    public async Task CommandsExecute_Plan_EntersPlanMode() {
        var (services, transcripts, engine) = await CreateAsync();
        await using var _transcripts = transcripts;

        var result = await new DshRpcDispatcher(services).HandleAsync("commands/execute", ExecuteArgs("/plan"));

        result.Ok.Should().BeTrue(result.Error?.Message);
        engine.CurrentPermissionMode.Should().Be(PermissionMode.Plan);
        services.Settings.Describe().Namespaces.Single(ns => ns.Ns == "permission")
            .Value.GetProperty("defaultPreset").GetString().Should().Be("plan");
    }

    [Fact]
    public async Task ControlHub_Publish_DeliversToAttachedListeners_AndDetachStops() {
        var hub = new DshSessionControlHub();
        var received = new List<DshSessionControlProjection>();

        using (hub.Attach(frame => {
            received.Add(frame);
            return Task.CompletedTask;
        })) {
            await hub.PublishAsync(ControlFrame("plan", active: true, seq: 1));
        }
        await hub.PublishAsync(ControlFrame("plan", active: false, seq: 2));

        received.Should().ContainSingle();
        received[0].Value.GetProperty("active").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ControlHub_FailingListener_DoesNotBlockOtherListeners() {
        var hub = new DshSessionControlHub();
        var received = new List<DshSessionControlProjection>();
        using var bad = hub.Attach(_ => throw new InvalidOperationException("boom"));
        using var good = hub.Attach(frame => {
            received.Add(frame);
            return Task.CompletedTask;
        });

        await hub.PublishAsync(ControlFrame("plan", active: true, seq: 1));

        received.Should().ContainSingle("单个订阅者异常不得中断广播");
    }
}
