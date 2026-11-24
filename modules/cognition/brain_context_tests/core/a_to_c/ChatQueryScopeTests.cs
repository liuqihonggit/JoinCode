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
}
