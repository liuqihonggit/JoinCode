namespace Brain.Tests.Context;

/// <summary>
/// TASK135 B3 — <c>/clear</c> 用 <c>ReleaseOldBucket=false</c> 保守不回收旧桶（在途回合还要写），
/// 长会话反复翻页会让内存线性增长。SessionStore 以「最近访问」为序淘汰超出上限的非当前代。
/// </summary>
public sealed class SessionStoreEpochRetentionTests {
    [Fact]
    public void SwitchSession_BeyondLimit_KeepsAtMostLimit() {
        var store = new SessionStore("s-0");

        for (var i = 0; i < SessionStore.MaxRetainedSessions + 5; i++) {
            store.SwitchSession($"s-{i}");
            store.Append(new ApiMessage(MessageRole.User, $"问题 {i}"), null);
        }

        store.RetainedSessionCount.Should().BeLessThanOrEqualTo(SessionStore.MaxRetainedSessions,
            "旧代保守不回收时必须靠上限兜底，否则长会话内存线性增长");
    }

    [Fact]
    public void SwitchSession_NeverEvictsTheSessionBeingSwitchedInto() {
        var store = new SessionStore("s-0");

        for (var i = 1; i <= SessionStore.MaxRetainedSessions + 5; i++) {
            store.SwitchSession($"s-{i}");
            store.Append(new ApiMessage(MessageRole.User, $"问题 {i}"), null);

            store.CountOf(null).Should().Be(1,
                $"切到 s-{i} 后它就是要写入的当前代，淘汰绝不能把它当牺牲品");
        }
    }

    [Fact]
    public void SwitchSession_EvictsLeastRecentlyUsedFirst() {
        var store = new SessionStore("s-live");
        store.Append(new ApiMessage(MessageRole.User, "在途查询的内容"), null);

        // 模拟：其它代快速翻页，但在途代每次回合都 GetLog 刷新访问时间
        for (var i = 0; i < SessionStore.MaxRetainedSessions + 3; i++) {
            store.SwitchSession($"s-{i}");
            store.Append(new ApiMessage(MessageRole.User, $"问题 {i}"), null);
            store.GetLog("s-live");
        }

        store.CountOf("s-live").Should().BeGreaterThan(0, "在途查询持续访问的代次不得被淘汰");
        store.GetLog("s-0").Count.Should().Be(0, "最久未访问的非当前代应先被淘汰（重建为空桶）");
    }
}
