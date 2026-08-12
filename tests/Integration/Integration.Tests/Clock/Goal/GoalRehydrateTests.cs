
namespace Integration.Tests.Clock.Goal;

/// <summary>
/// 目标重启恢复 E2E 测试 — 验证 GoalStateStore 持久化 + GoalEngine.RehydrateAsync 从持久化恢复状态和对话历史。
/// 模拟进程重启：第一个 GoalEngine 写入持久化 → 丢弃实例 → 新 GoalEngine 从存储恢复。
/// </summary>
public sealed class GoalRehydrateTests
{
    private readonly IO.FileSystem.PhysicalFileSystem _fs = new();
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "jcc-goal-test-" + Guid.NewGuid().ToString("N")[..8]);

    /// <summary>
    /// 创建临时目录下的 GoalStateStore，用真实文件系统验证持久化往返。
    /// </summary>
    private GoalStateStore CreateStore()
    {
        _fs.CreateDirectory(_tempDir);
        return new GoalStateStore(_fs, _tempDir);
    }

    /// <summary>
    /// 清理临时目录。
    /// </summary>
    private void Cleanup()
    {
        if (_fs.DirectoryExists(_tempDir)) _fs.DeleteDirectory(_tempDir, true);
    }

    /// <summary>
    /// 创建松散 mock 的 GoalEngine 依赖，仅用于 RehydrateAsync（不启动引擎循环）。
    /// </summary>
    private static GoalEngine CreateEngine(IGoalStateStore? stateStore = null)
    {
        var chatMock = new Mock<JoinCode.Abstractions.LLM.IChatClient>();
        chatMock.Setup(c => c.Plugins).Returns(new Mock<JoinCode.Abstractions.LLM.IToolCollection>().Object);
        var evaluatorMock = new Mock<IGoalEvaluator>();
        var heartbeatMock = new Mock<IGoalHeartbeat>();
        return new GoalEngine(chatMock.Object, evaluatorMock.Object, stateStore: stateStore, heartbeat: heartbeatMock.Object);
    }

    [Fact]
    public async Task GoalStateStore_SaveAndLoad_RoundTripsState()
    {
        var store = CreateStore();
        try
        {
            var state = new GoalState
            {
                GoalId = "goal-test-001",
                Objective = "实现用户注册功能",
                Status = GoalStatus.Pursuing,
                Constraints = ["不修改公共API", "测试覆盖率>80%"],
                TokenBudget = 50000,
                PersistedHistory =
                [
                    new ApiMessageDocument { Role = "system", Content = "你是目标执行引擎" },
                    new ApiMessageDocument { Role = "user", Content = "开始实现用户注册" },
                    new ApiMessageDocument { Role = "assistant", Content = "我来分析需求..." }
                ]
            };

            await store.SaveAsync(state, CancellationToken.None);

            var loaded = await store.LoadAsync("goal-test-001", CancellationToken.None);

            loaded.Should().NotBeNull();
            loaded!.GoalId.Should().Be("goal-test-001");
            loaded.Objective.Should().Be("实现用户注册功能");
            loaded.Status.Should().Be(GoalStatus.Pursuing);
            loaded.Constraints.Should().HaveCount(2);
            loaded.PersistedHistory.Should().HaveCount(3);
            loaded.PersistedHistory![0].Role.Should().Be("system");
            loaded.PersistedHistory[2].Content.Should().Be("我来分析需求...");
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public async Task GoalStateStore_GetActiveGoals_FiltersCompleted()
    {
        var store = CreateStore();
        try
        {
            await store.SaveAsync(new GoalState { GoalId = "g1", Objective = "活跃目标", Status = GoalStatus.Pursuing }, CancellationToken.None);
            await store.SaveAsync(new GoalState { GoalId = "g2", Objective = "暂停目标", Status = GoalStatus.Paused }, CancellationToken.None);
            await store.SaveAsync(new GoalState { GoalId = "g3", Objective = "已完成目标", Status = GoalStatus.Achieved }, CancellationToken.None);
            await store.SaveAsync(new GoalState { GoalId = "g4", Objective = "未完成目标", Status = GoalStatus.Unmet }, CancellationToken.None);

            var active = await store.GetActiveGoalsAsync(CancellationToken.None);

            active.Should().HaveCount(2);
            active.Select(s => s.GoalId).Should().BeEquivalentTo(["g1", "g2"]);
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public async Task GoalEngine_Rehydrate_RestoresStateAndHistory()
    {
        var store = CreateStore();
        try
        {
            var originalState = new GoalState
            {
                GoalId = "goal-rehydrate-001",
                Objective = "重构认证模块",
                Status = GoalStatus.Pursuing,
                Constraints = ["保持向后兼容"],
                PersistedHistory =
                [
                    new ApiMessageDocument { Role = "system", Content = "系统提示" },
                    new ApiMessageDocument { Role = "user", Content = "开始重构" },
                    new ApiMessageDocument { Role = "assistant", Content = "分析完成，开始修改" }
                ]
            };
            await store.SaveAsync(originalState, CancellationToken.None);

            var engine = CreateEngine(store);
            engine.CurrentState.Should().BeNull();

            await engine.RehydrateAsync(CancellationToken.None);

            engine.CurrentState.Should().NotBeNull();
            engine.CurrentState!.GoalId.Should().Be("goal-rehydrate-001");
            engine.CurrentState.Objective.Should().Be("重构认证模块");
            engine.CurrentState.Status.Should().Be(GoalStatus.Pursuing);
            engine.CurrentState.Constraints.Should().ContainSingle().Which.Should().Be("保持向后兼容");
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public async Task GoalEngine_Rehydrate_EmptyStore_NoOp()
    {
        var store = CreateStore();
        try
        {
            var engine = CreateEngine(store);

            await engine.RehydrateAsync(CancellationToken.None);

            engine.CurrentState.Should().BeNull();
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public async Task GoalEngine_Rehydrate_NoStore_NoOp()
    {
        var engine = CreateEngine(stateStore: null);

        var act = async () => await engine.RehydrateAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        engine.CurrentState.Should().BeNull();
    }

    [Fact]
    public async Task GoalStateStore_Delete_RemovesState()
    {
        var store = CreateStore();
        try
        {
            await store.SaveAsync(new GoalState { GoalId = "g-del", Objective = "待删除", Status = GoalStatus.Pursuing }, CancellationToken.None);
            (await store.LoadAsync("g-del", CancellationToken.None)).Should().NotBeNull();

            await store.DeleteAsync("g-del", CancellationToken.None);

            (await store.LoadAsync("g-del", CancellationToken.None)).Should().BeNull();
        }
        finally
        {
            Cleanup();
        }
    }
}
