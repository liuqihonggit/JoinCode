namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// 目标条投影测试（TASK066 C7）：前端 GoalDock 读 <c>useProjection("goal").goal</c>，
/// 宿主必须在会话投影里给出 DSH wire 形状的 <c>goal</c> 面：无目标是 JSON <c>null</c>，
/// 有目标是 <c>{goal, roundsStarted, createdAt, updatedAt}</c>；并在 goals 变更后推
/// <c>session/control</c> 的 <c>goal</c> 整值帧，否则目标条永远不渲染。
/// 无目标写成 <c>{goal:null}</c> 会让前端 <c>projection.goal.phase</c> 抛错、整个 dock 槽位崩掉。
/// </summary>
public sealed class DshGoalProjectionTests {
    private const string SessionId = "s-goal-projection";

    /// <summary>构造带一个真实会话条目的宿主服务</summary>
    private static async Task<(DshHostServices Services, State.TranscriptService Transcripts, DshRpcDispatcher Dispatcher)> CreateAsync() {
        var fs = new InMemoryFileSystem();
        var transcripts = new State.TranscriptService(fs, "/test/sessions");
        await transcripts.AppendEntryAsync(SessionId, new TranscriptEntry {
            Role = "user",
            Content = "开始长任务",
            Timestamp = new DateTime(2026, 10, 24, 1, 0, 0, DateTimeKind.Utc),
        });
        var services = new DshHostServices(fs);
        services.Sessions.AttachTranscripts(transcripts);
        return (services, transcripts, new DshRpcDispatcher(services));
    }

    private static JsonElement Args(object payload) => JsonSerializer.SerializeToElement(payload);

    [Fact]
    public async Task Projections_ExposeNullGoalWireValueBeforeCreate() {
        var (services, transcripts, _) = await CreateAsync();
        await using var _transcripts = transcripts;

        var snapshot = await services.Sessions.ProjectionsAsync(SessionId, CancellationToken.None)
            ?? throw new Xunit.Sdk.XunitException("期望会话投影存在");

        snapshot.Values.Goal.Should().BeNull("无目标时 goal 面必须是 JSON null（前端 projection?.goal.phase 依赖）");
        var wire = JsonSerializer.SerializeToElement(snapshot.Values.Goal, DshJsonContext.Default.DshSessionGoalProjection);
        wire.ValueKind.Should().Be(JsonValueKind.Null, "无目标不能序列化成 {goal:null}，否则前端 activeRef 抛错自毁 dock");
    }

    [Fact]
    public async Task Create_PublishesGoalFaceAndControlFrame() {
        var (services, transcripts, dispatcher) = await CreateAsync();
        await using var _transcripts = transcripts;
        var frames = new List<DshSessionControlProjection>();
        using var control = services.Control.Attach(frame => {
            frames.Add(frame);
            return Task.CompletedTask;
        });

        var created = await dispatcher.HandleAsync("goals/create",
            Args(new { agentId = SessionId, request = new { objective = "把目标条接上" } }));
        created.Ok.Should().BeTrue(created.Error?.Message);

        var snapshot = await services.Sessions.ProjectionsAsync(SessionId, CancellationToken.None)
            ?? throw new Xunit.Sdk.XunitException("期望会话投影存在");
        var face = snapshot.Values.Goal
            ?? throw new Xunit.Sdk.XunitException("期望目标投影面携带目标");
        face.Goal.Objective.Should().Be("把目标条接上");
        face.Goal.Phase.Should().Be("active");
        face.RoundsStarted.Should().Be(0);
        face.CreatedAt.Should().BeGreaterThan(0);
        face.UpdatedAt.Should().BeGreaterThan(0);

        var wire = JsonSerializer.SerializeToElement(face, DshJsonContext.Default.DshSessionGoalProjection);
        wire.GetProperty("goal").GetProperty("maxGoalRounds").GetInt32().Should().BeGreaterThan(0);
        wire.GetProperty("roundsStarted").GetInt32().Should().Be(0);
        wire.GetProperty("createdAt").GetInt64().Should().Be(face.CreatedAt);
        wire.GetProperty("goal").TryGetProperty("activation", out _).Should().BeFalse(
            "activation 是进程内激活态，走 goal/activation-changed，不在投影 wire 里");

        var frame = frames.SingleOrDefault(item => item.Key == "goal")
            ?? throw new Xunit.Sdk.XunitException("期望推送 goal 投影帧");
        frame.SessionId.Should().Be(SessionId);
        frame.Value.GetProperty("goal").GetProperty("objective").GetString().Should().Be("把目标条接上");
        frame.Value.TryGetProperty("activation", out _).Should().BeFalse();
        frame.Seq.Should().BeGreaterThanOrEqualTo(snapshot.AsOfSeq, "前端只接受高于已有行水位的帧");
    }

    [Fact]
    public async Task PauseResumeClear_PublishGoalControlFrames() {
        var (services, transcripts, dispatcher) = await CreateAsync();
        await using var _transcripts = transcripts;
        var frames = new List<DshSessionControlProjection>();
        using var control = services.Control.Attach(frame => {
            frames.Add(frame);
            return Task.CompletedTask;
        });

        var created = await dispatcher.HandleAsync("goals/create",
            Args(new { agentId = SessionId, request = new { objective = "暂停恢复清除" } }));
        var reference = created.Value.GetValueOrDefault().GetProperty("ref");
        var id = reference.GetProperty("id").GetString() ?? "";
        var revision = reference.GetProperty("revision").GetInt32();

        var paused = await dispatcher.HandleAsync("goals/pause", Args(new { agentId = SessionId, @ref = new { id, revision } }));
        paused.Ok.Should().BeTrue(paused.Error?.Message);
        GoalFrame(frames).Value.GetProperty("goal").GetProperty("phase").GetString().Should().Be("paused");

        var resumed = await dispatcher.HandleAsync("goals/resume", Args(new { agentId = SessionId, @ref = new { id, revision = revision + 1 } }));
        resumed.Ok.Should().BeTrue(resumed.Error?.Message);
        GoalFrame(frames).Value.GetProperty("goal").GetProperty("phase").GetString().Should().Be("active");

        var cleared = await dispatcher.HandleAsync("goals/clear", Args(new { agentId = SessionId, @ref = new { id, revision = revision + 2 } }));
        cleared.Ok.Should().BeTrue(cleared.Error?.Message);
        GoalFrame(frames).Value.ValueKind.Should().Be(JsonValueKind.Null, "清除后前端要收到 goal 面 null 才会隐藏目标条");

        var snapshot = await services.Sessions.ProjectionsAsync(SessionId, CancellationToken.None)
            ?? throw new Xunit.Sdk.XunitException("期望会话投影存在");
        snapshot.Values.Goal.Should().BeNull();
    }

    private static DshSessionControlProjection GoalFrame(List<DshSessionControlProjection> frames) =>
        frames.LastOrDefault(item => item.Key == "goal")
        ?? throw new Xunit.Sdk.XunitException("期望推送 goal 投影帧");

    [Fact]
    public async Task GoalsRpc_AcceptsDshAgentScopedWire() {
        var (services, transcripts, dispatcher) = await CreateAsync();
        await using var _transcripts = transcripts;

        var created = await dispatcher.HandleAsync("goals/create",
            Args(new { agentId = SessionId, request = new { objective = "agent 作用域建目标" } }));
        created.Ok.Should().BeTrue(created.Error?.Message);
        var id = created.Value.GetValueOrDefault().GetProperty("ref").GetProperty("id").GetString() ?? "";

        var got = await dispatcher.HandleAsync("goals/get", Args(new { agentId = SessionId }));
        got.Ok.Should().BeTrue(got.Error?.Message);
        got.Value.GetValueOrDefault().GetProperty("objective").GetString().Should().Be("agent 作用域建目标");
        got.Value.GetValueOrDefault().GetProperty("activation").GetString().Should().Be("armed",
            "前端 useGoalActivation 读 goals/get 的 activation 来决定显示暂停/恢复");

        var edited = await dispatcher.HandleAsync("goals/edit",
            Args(new { agentId = SessionId, @ref = new { id, revision = 1 }, request = new { objective = "改名" } }));
        edited.Ok.Should().BeTrue(edited.Error?.Message);
        edited.Value.GetValueOrDefault().GetProperty("objective").GetString().Should().Be("改名");

        var cleared = await dispatcher.HandleAsync("goals/clear",
            Args(new { agentId = SessionId, @ref = new { id, revision = 2 } }));
        cleared.Ok.Should().BeTrue(cleared.Error?.Message);
        cleared.Value.GetValueOrDefault().GetProperty("id").GetString().Should().Be(id,
            "DSH goals/clear 返回裸 GoalRef {id, revision}，不再包一层 ref");
    }

    [Fact]
    public async Task GoalsGet_SerializesAbsentGoalAsUndefined() {
        var (services, transcripts, dispatcher) = await CreateAsync();
        await using var _transcripts = transcripts;

        var got = await dispatcher.HandleAsync("goals/get", Args(new { agentId = SessionId }));
        got.Ok.Should().BeTrue(got.Error?.Message);
        got.Value.HasValue.Should().BeFalse("DSH goals/get 无目标返回 undefined，wire 上省略 value 键");

        var envelope = JsonSerializer.SerializeToElement(
            new DshRpcResponse { RpcId = "r", Result = got },
            DshJsonContext.Default.DshRpcResponse);
        envelope.GetProperty("result").TryGetProperty("value", out _).Should().BeFalse(
            "写成 value:null 会让前端严格解码失败，activation 读不到就不显示暂停/恢复按钮");
    }
}
