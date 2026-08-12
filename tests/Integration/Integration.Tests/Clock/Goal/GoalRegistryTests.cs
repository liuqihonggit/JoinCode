
namespace Integration.Tests.Clock.Goal;

/// <summary>
/// 多目标切换 E2E 测试 — 验证 PersistentGoalRegistry 管理多个 GoalEngine 实例：
/// RehydrateAllAsync 恢复 → ListActiveGoalsAsync 列出 → SetCurrent 切换 → GetEngine 独立获取。
/// </summary>
public sealed class GoalRegistryTests : IDisposable
{
    private readonly IO.FileSystem.PhysicalFileSystem _fs = new();
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "jcc-goal-registry-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly ServiceProvider _serviceProvider;
    private readonly GoalStateStore _store;
    private const string SessionId = "test-session-registry";

    public GoalRegistryTests()
    {
        _fs.CreateDirectory(_tempDir);
        _store = new GoalStateStore(_fs, _tempDir);

        var services = new ServiceCollection();
        var chatMock = new Mock<JoinCode.Abstractions.LLM.IChatClient>();
        chatMock.Setup(c => c.Plugins).Returns(new Mock<JoinCode.Abstractions.LLM.IToolCollection>().Object);
        services.AddSingleton(chatMock.Object);
        services.AddSingleton(new Mock<IGoalEvaluator>().Object);
        services.AddSingleton(new Mock<IGoalHeartbeat>().Object);
        services.AddSingleton<IGoalStateStore>(_store);
        _serviceProvider = services.BuildServiceProvider();
    }

    /// <summary>
    /// 预存一个活跃 GoalState 到持久化存储。
    /// </summary>
    private async Task<GoalState> SeedGoalAsync(string goalId, string objective)
    {
        var state = new GoalState
        {
            GoalId = goalId,
            Objective = objective,
            Status = GoalStatus.Pursuing,
            SessionId = SessionId,
            PersistedHistory =
            [
                new ApiMessageDocument { Role = "user", Content = objective }
            ]
        };
        await _store.SaveAsync(state, CancellationToken.None);
        return state;
    }

    /// <summary>
    /// 创建已设置 sessionId 的 PersistentGoalRegistry。
    /// </summary>
    private PersistentGoalRegistry CreateRegistry()
    {
        var registry = new PersistentGoalRegistry(_serviceProvider, _store);
        registry.SetSessionId(SessionId);
        return registry;
    }

    [Fact]
    public async Task RehydrateAllAsync_TwoActiveGoals_RestoresBoth()
    {
        await SeedGoalAsync("goal-a", "实现功能A");
        await SeedGoalAsync("goal-b", "实现功能B");

        var registry = CreateRegistry();
        await registry.RehydrateAllAsync(CancellationToken.None);

        var active = await registry.ListActiveGoalsAsync(CancellationToken.None);
        active.Should().HaveCount(2);
        active.Select(s => s.GoalId).Should().BeEquivalentTo(["goal-a", "goal-b"]);
    }

    [Fact]
    public async Task RehydrateAllAsync_SetsFirstAsCurrent()
    {
        await SeedGoalAsync("goal-first", "第一个目标");
        await SeedGoalAsync("goal-second", "第二个目标");

        var registry = CreateRegistry();
        await registry.RehydrateAllAsync(CancellationToken.None);

        registry.CurrentEngine.Should().NotBeNull();
        registry.CurrentEngine!.CurrentState.Should().NotBeNull();
    }

    [Fact]
    public async Task SetCurrent_SwitchesActiveGoal()
    {
        await SeedGoalAsync("goal-x", "目标X");
        await SeedGoalAsync("goal-y", "目标Y");

        var registry = CreateRegistry();
        await registry.RehydrateAllAsync(CancellationToken.None);

        var switched = registry.SetCurrent("goal-y");
        switched.Should().BeTrue();
        registry.CurrentEngine.Should().NotBeNull();

        var engineY = registry.GetEngine("goal-y");
        engineY.Should().NotBeNull();
        engineY!.CurrentState.Should().NotBeNull();
    }

    [Fact]
    public async Task SetCurrent_UnknownGoalId_ReturnsFalse()
    {
        await SeedGoalAsync("goal-real", "真实目标");

        var registry = CreateRegistry();
        await registry.RehydrateAllAsync(CancellationToken.None);

        registry.SetCurrent("goal-nonexistent").Should().BeFalse();
    }

    [Fact]
    public async Task GetEngine_ReturnsCorrectInstance()
    {
        await SeedGoalAsync("goal-get-1", "目标1");
        await SeedGoalAsync("goal-get-2", "目标2");

        var registry = CreateRegistry();
        await registry.RehydrateAllAsync(CancellationToken.None);

        var engine1 = registry.GetEngine("goal-get-1");
        var engine2 = registry.GetEngine("goal-get-2");

        engine1.Should().NotBeNull();
        engine2.Should().NotBeNull();
        engine1.Should().NotBeSameAs(engine2);
    }

    [Fact]
    public async Task RehydrateAllAsync_EmptyStore_NoOp()
    {
        var registry = CreateRegistry();
        await registry.RehydrateAllAsync(CancellationToken.None);

        registry.CurrentEngine.Should().BeNull();
        (await registry.ListActiveGoalsAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task RehydrateAllAsync_NoStore_NoOp()
    {
        var registry = new PersistentGoalRegistry(_serviceProvider, stateStore: null);
        registry.SetSessionId(SessionId);
        await registry.RehydrateAllAsync(CancellationToken.None);

        registry.CurrentEngine.Should().BeNull();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        if (_fs.DirectoryExists(_tempDir)) _fs.DeleteDirectory(_tempDir, true);
    }
}
