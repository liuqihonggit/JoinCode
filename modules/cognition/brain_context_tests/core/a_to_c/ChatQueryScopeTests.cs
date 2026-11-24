namespace Brain.Tests.Context;

/// <summary>
/// ADR 0165 阶段 0 红测试 — ChatService 的会话级 Singleton 状态改为 per-query AsyncLocal scope。
/// 红点：ChatQueryScope 类型尚不存在，编译失败即证明状态隔离地基未实现。
/// </summary>
public sealed class ChatQueryScopeTests {
    [Fact]
    public async Task Enter_DifferentQueries_IsolateToolUseContext() {
        using (var outer = Core.Context.ChatQueryScope.Enter("q-outer")) {
            var outerContext = Core.Context.ChatQueryScope.Current.ToolUseContext;
            await Task.Yield();

            using (var inner = Core.Context.ChatQueryScope.Enter("q-inner")) {
                Assert.Equal("q-inner", Core.Context.ChatQueryScope.Current.QueryId);
                Assert.NotSame(outerContext, Core.Context.ChatQueryScope.Current.ToolUseContext);
            }

            Assert.Equal("q-outer", Core.Context.ChatQueryScope.Current.QueryId);
            Assert.Same(outerContext, Core.Context.ChatQueryScope.Current.ToolUseContext);
        }
    }

    [Fact]
    public async Task Enter_ConcurrentFlows_KeepIndependentScopes() {
        static async Task<(string QueryId, ToolUseContext Context)> RunAsync(string queryId) {
            using var scope = Core.Context.ChatQueryScope.Enter(queryId);
            await Task.Delay(10);
            return (
                Core.Context.ChatQueryScope.Current.QueryId,
                Core.Context.ChatQueryScope.Current.ToolUseContext);
        }

        var results = await Task.WhenAll(RunAsync("q-a"), RunAsync("q-b"));

        Assert.Equal("q-a", results[0].QueryId);
        Assert.Equal("q-b", results[1].QueryId);
        Assert.NotSame(results[0].Context, results[1].Context);
    }

    [Fact]
    public void Enter_WithoutQueryId_GeneratesNonEmptyId() {
        using var scope = Core.Context.ChatQueryScope.Enter();

        Assert.False(string.IsNullOrWhiteSpace(Core.Context.ChatQueryScope.Current.QueryId));
    }

    /// <summary>ADR 0167 — 会话代次必须随查询钉住：scope 内读到的 sessionId 不随外部切换而变化。</summary>
    [Fact]
    public async Task Enter_WithSessionId_PinsItAcrossExternalSwitch() {
        using var scope = Core.Context.ChatQueryScope.Enter("q-1", sessionId: "s-old");

        await Task.Yield();

        Assert.Equal("s-old", Core.Context.ChatQueryScope.Current.SessionId);
    }

    /// <summary>ADR 0167 — 并发查询各持自己的代次，外层切换不影响内层。</summary>
    [Fact]
    public async Task Enter_NestedScopes_KeepIndependentSessionIds() {
        using (Core.Context.ChatQueryScope.Enter("q-outer", sessionId: "s-old")) {
            await Task.Yield();

            using (Core.Context.ChatQueryScope.Enter("q-inner", sessionId: "s-new")) {
                Assert.Equal("s-new", Core.Context.ChatQueryScope.Current.SessionId);
            }

            Assert.Equal("s-old", Core.Context.ChatQueryScope.Current.SessionId);
        }
    }
}
