namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// goals 七方法测试（TASK058 P2-B5）：创建→CAS 编辑→pause/resume/complete 状态机（activation
/// 翻转事件）→clear；roundsStarted 经 DshTurnRunner 受理钩子递增；conflict/not-found 引导错误。
/// </summary>
public sealed class DshGoalsTests {
    private static (DshHostServices Services, DshRpcDispatcher Dispatcher) CreateServices() {
        var services = new DshHostServices(new InMemoryFileSystem());
        return (services, new DshRpcDispatcher(services));
    }

    private static JsonElement GoalRefArgs(string sessionId, DshGoalRef reference) =>
        JsonSerializer.SerializeToElement(new {
            sessionId,
            @ref = new { id = reference.Id, revision = reference.Revision },
        });

    private static JsonElement GoalCreateArgs(string sessionId, string objective, int? maxRounds = null) =>
        JsonSerializer.SerializeToElement(new {
            sessionId, objective, maxGoalRounds = maxRounds,
        });

    [Fact]
    public async Task Goals_Lifecycle_CreatePauseResumeCompleteClear() {
        var (_, dispatcher) = CreateServices();

        var created = await dispatcher.HandleAsync("goals/create", GoalCreateArgs("s1", "完成重构"));
        created.Ok.Should().BeTrue(created.Error?.Message);
        var reference = created.Value.GetValueOrDefault().GetProperty("ref");

        // create 后 get 可见（armed/active/revision=1）
        var got = await dispatcher.HandleAsync("goals/get", JsonSerializer.SerializeToElement(new { sessionId = "s1" }));
        var goal = got.Value.GetValueOrDefault();
        goal.GetProperty("phase").GetString().Should().Be("active");
        goal.GetProperty("activation").GetString().Should().Be("armed");
        goal.GetProperty("revision").GetInt32().Should().Be(1);
        goal.GetProperty("objective").GetString().Should().Be("完成重构");
        goal.GetProperty("maxGoalRounds").GetInt32().Should().Be(10, "默认轮次上限");

        // pause → paused/disarmed/revision=2
        var paused = await dispatcher.HandleAsync("goals/pause", GoalRefArgs("s1", ParseRef(reference)));
        paused.Value.GetValueOrDefault().GetProperty("activation").GetString().Should().Be("disarmed");

        // resume → active/armed/revision=3
        var resumed = await dispatcher.HandleAsync("goals/resume", GoalRefArgs("s1", new DshGoalRef {
            Id = reference.GetProperty("id").GetString() ?? "",
            Revision = 2,
        }));
        resumed.Value.GetValueOrDefault().GetProperty("activation").GetString().Should().Be("armed");

        // complete → complete/disarmed
        var completed = await dispatcher.HandleAsync("goals/complete", GoalRefArgs("s1", new DshGoalRef {
            Id = reference.GetProperty("id").GetString() ?? "",
            Revision = 3,
        }));
        completed.Value.GetValueOrDefault().GetProperty("phase").GetString().Should().Be("complete");

        // clear → get 返回 null
        var cleared = await dispatcher.HandleAsync("goals/clear", GoalRefArgs("s1", new DshGoalRef {
            Id = reference.GetProperty("id").GetString() ?? "",
            Revision = 4,
        }));
        cleared.Ok.Should().BeTrue(cleared.Error?.Message);
        var after = await dispatcher.HandleAsync("goals/get", JsonSerializer.SerializeToElement(new { sessionId = "s1" }));
        after.Value.Should().NotBeNull();
        after.Value.GetValueOrDefault().ValueKind.Should().Be(JsonValueKind.Null, "清除后 goals/get 返回 null 值");
    }

    [Fact]
    public async Task Goals_Edit_StaleRevision_ReturnsConflict() {
        var (_, dispatcher) = CreateServices();
        var created = await dispatcher.HandleAsync("goals/create", GoalCreateArgs("s2", "目标"));
        var reference = ParseRef(created.Value.GetValueOrDefault().GetProperty("ref"));

        // 正确 revision 编辑成功（revision+1）
        var edited = await dispatcher.HandleAsync("goals/edit", JsonSerializer.SerializeToElement(new {
            sessionId = "s2",
            @ref = new { id = reference.Id, revision = reference.Revision },
            changes = new { objective = "新目标" },
        }));
        edited.Ok.Should().BeTrue(edited.Error?.Message);
        edited.Value.GetValueOrDefault().GetProperty("objective").GetString().Should().Be("新目标");

        // 旧 revision 再编辑 → goals/conflict
        var stale = await dispatcher.HandleAsync("goals/edit", JsonSerializer.SerializeToElement(new {
            sessionId = "s2",
            @ref = new { id = reference.Id, revision = reference.Revision },
            changes = new { objective = "又一次" },
        }));
        stale.Ok.Should().BeFalse();
        stale.Error.Should().NotBeNull();
        if (stale.Error is { } err) {
            err.Code.Should().Be("goals/conflict");
        }
    }

    [Fact]
    public async Task Goals_Create_Duplicate_ReturnsConflictGuidance() {
        var (_, dispatcher) = CreateServices();
        await dispatcher.HandleAsync("goals/create", GoalCreateArgs("s3", "第一"));
        var second = await dispatcher.HandleAsync("goals/create", GoalCreateArgs("s3", "第二"));
        second.Ok.Should().BeFalse();
        if (second.Error is { } err) {
            err.Code.Should().Be("goals/conflict");
            err.Message.Should().Contain("clear", "引导用户先清除再创建");
        }
    }

    [Fact]
    public async Task Goals_RoundsStarted_IncrementedByTurnAcceptance() {
        var services = new DshHostServices(new InMemoryFileSystem());
        var dispatcher = new DshRpcDispatcher(services);
        await using var transcripts = new State.TranscriptService(new InMemoryFileSystem(), "/test/sessions");
        services.Sessions.AttachTranscripts(transcripts);
        var created = await services.Sessions.CreateAsync(null, "/test/proj", "deepseek", "deepseek-v4-pro", CancellationToken.None);
        var sessionId = (created ?? throw new Xunit.Sdk.XunitException("session/create 失败")).SessionId;
        await dispatcher.HandleAsync("goals/create", GoalCreateArgs(sessionId, "轮次目标"));
        var hold = new TaskCompletionSource();
        await using var engine = new PlaceholderChatSession { TurnGate = ct => hold.Task.WaitAsync(ct) };
        services.AttachEngine(engine);
        try {
            // 两轮受理（TurnGate 挂住 StreamAsync，受理即返回）
            (await dispatcher.HandleAsync("session/prompt", PromptArgs(sessionId, "第一轮"))).Ok.Should().BeTrue();
            hold.TrySetCanceled();
            hold = new TaskCompletionSource();
            (await dispatcher.HandleAsync("session/prompt", PromptArgs(sessionId, "第二轮"))).Ok.Should().BeTrue();
        } finally {
            hold.TrySetCanceled();
        }

        var got = await dispatcher.HandleAsync("goals/get", JsonSerializer.SerializeToElement(new { sessionId }));
        got.Value.GetValueOrDefault().GetProperty("roundsStarted").GetInt32().Should().Be(2, "每轮受理递增");
    }

    private static DshGoalRef ParseRef(JsonElement reference) => new() {
        Id = reference.GetProperty("id").GetString() ?? "",
        Revision = reference.GetProperty("revision").GetInt32(),
    };

    private static JsonElement PromptArgs(string sessionId, string text) =>
        JsonSerializer.SerializeToElement(new {
            request = new {
                requestId = Guid.NewGuid().ToString("N"),
                sessionId,
                mode = "send",
                content = new[] { new { type = "text", text } },
            },
        });
}
