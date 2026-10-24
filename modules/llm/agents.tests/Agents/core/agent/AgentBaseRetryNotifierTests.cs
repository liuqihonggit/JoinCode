namespace Core.Agents.Tests.Unit.Agents;

/// <summary>
/// TASK070 E2/DSG046 — AgentBase.RetryNotifier → QueryOptions.OnRetry 透传测试：
/// BuildChatOptions 是 options 组装唯一点（protected — 派生类暴露测试面）；
/// 主代理路径 SessionController 绑定 RetryNotifier 后，每次查询的 QueryOptions 携带回调。
/// </summary>
public sealed class AgentBaseRetryNotifierTests {
    /// <summary>BuildChatOptions 暴露子类（protected 测试面 — InternalsVisibleTo 不覆盖 protected）。</summary>
    private sealed class ExposedAgent : AgentBase {
        public ExposedAgent(Mock<IQueryEngine> queryEngine) : base("task", null, queryEngine.Object, null) { }
        public QueryOptions? ExposeBuildChatOptions() => BuildChatOptions();
    }

    [Fact]
    public async Task BuildChatOptions_WithRetryNotifier_CarriesOnRetryCallback() {
        var queryEngine = new Mock<IQueryEngine>();
        await using var agent = new ExposedAgent(queryEngine);
        Action<QueryRetryNotification> notifier = _ => { };
        agent.RetryNotifier = notifier;

        var options = agent.ExposeBuildChatOptions();

        Assert.NotNull(options);
        Assert.Same(notifier, options.OnRetry);
    }

    [Fact]
    public async Task BuildChatOptions_WithoutRetryNotifier_ReturnsNull() {
        var queryEngine = new Mock<IQueryEngine>();
        await using var agent = new ExposedAgent(queryEngine);

        var options = agent.ExposeBuildChatOptions();

        Assert.Null(options);
    }
}

