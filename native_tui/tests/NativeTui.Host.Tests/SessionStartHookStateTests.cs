namespace NativeTui.Host.Tests;

/// <summary>
/// TASK135 B2 — SessionStart hook 的执行标记必须按**会话代次**判定：
/// 同代只跑一次；/clear 等换代后新代要能再跑一次；显式跑过 hook 的入口（/clear 的 clear 版）
/// 标记该代已跑，避免新代首个查询重复注入 hook 输出。
/// </summary>
public sealed class SessionStartHookStateTests {
    [Fact]
    public void TryBegin_SameSession_OnlyFirstWins() {
        var state = new JoinCode.Adapters.SessionStartHookState();

        Assert.True(state.TryBegin("s-1"));
        Assert.False(state.TryBegin("s-1"), "同一代 hook 只跑一次，失败/取消也不重试");
    }

    [Fact]
    public void TryBegin_AfterEpochSwitch_AllowsNewEpoch() {
        var state = new JoinCode.Adapters.SessionStartHookState();

        Assert.True(state.TryBegin("s-old"));
        Assert.True(state.TryBegin("s-new"), "/clear 换代后新会话必须重新触发 SessionStart");
        Assert.False(state.TryBegin("s-new"));
    }

    [Fact]
    public void MarkRan_SuppressesSameEpochStartupRun() {
        var state = new JoinCode.Adapters.SessionStartHookState();

        // /clear 显式跑完 SessionStart(clear) 后标记本代
        state.MarkRan("s-cleared");

        Assert.False(state.TryBegin("s-cleared"),
            "同一代已由 /clear 显式跑过，新代首个查询不得重复注入 hook 输出");
    }

    [Fact]
    public void MarkRan_DoesNotBlockNextEpoch() {
        var state = new JoinCode.Adapters.SessionStartHookState();

        state.MarkRan("s-cleared");
        Assert.True(state.TryBegin("s-next"));
    }
}
