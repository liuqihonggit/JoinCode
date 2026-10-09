// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Core.Tests.Hooks.Session;

/// <summary>
/// SessionHookStore 确定性测试 — 单线程下 CAS 操作确定性验证
/// <para>不依赖并发时序,仅验证单线程下 Add/Remove/Get/Clear 的语义正确性。</para>
/// </summary>
public sealed class SessionHookStoreTest {

    private static SessionHookEntry CreateEntry(string command = "echo test", string? matcher = null) {
        return new SessionHookEntry {
            Hook = new BashCommandHook { Command = command },
            Matcher = matcher
        };
    }

    [Fact]
    public void AddHook_新增事件_可GetHooks取回() {
        var store = new SessionHookStore();
        var entry = CreateEntry();

        store.AddHook(HookEvent.PreToolUse, entry);

        var hooks = store.GetHooks(HookEvent.PreToolUse);
        hooks.Should().HaveCount(1);
        hooks[0].Should().BeSameAs(entry);
    }

    [Fact]
    public void AddHook_同事件多钩子_按添加顺序排列() {
        var store = new SessionHookStore();
        var entry1 = CreateEntry("cmd1");
        var entry2 = CreateEntry("cmd2");
        var entry3 = CreateEntry("cmd3");

        store.AddHook(HookEvent.PreToolUse, entry1);
        store.AddHook(HookEvent.PreToolUse, entry2);
        store.AddHook(HookEvent.PreToolUse, entry3);

        var hooks = store.GetHooks(HookEvent.PreToolUse);
        hooks.Should().HaveCount(3);
        hooks[0].Should().BeSameAs(entry1);
        hooks[1].Should().BeSameAs(entry2);
        hooks[2].Should().BeSameAs(entry3);
    }

    [Fact]
    public void AddHook_不同事件_互不影响() {
        var store = new SessionHookStore();
        var preEntry = CreateEntry("pre");
        var postEntry = CreateEntry("post");

        store.AddHook(HookEvent.PreToolUse, preEntry);
        store.AddHook(HookEvent.PostToolUse, postEntry);

        store.GetHooks(HookEvent.PreToolUse).Should().HaveCount(1);
        store.GetHooks(HookEvent.PostToolUse).Should().HaveCount(1);
        store.GetHooks(HookEvent.SessionStart).Should().BeEmpty();
    }

    [Fact]
    public void GetHooks_未添加事件_返回空列表() {
        var store = new SessionHookStore();
        store.GetHooks(HookEvent.PreToolUse).Should().BeEmpty();
    }

    [Fact]
    public void RemoveHook_匹配谓词_移除对应钩子() {
        var store = new SessionHookStore();
        var entry1 = CreateEntry("keep");
        var entry2 = CreateEntry("remove");

        store.AddHook(HookEvent.PreToolUse, entry1);
        store.AddHook(HookEvent.PreToolUse, entry2);

        store.RemoveHook(HookEvent.PreToolUse, e => e.Hook is BashCommandHook bash && bash.Command == "remove");

        var hooks = store.GetHooks(HookEvent.PreToolUse);
        hooks.Should().HaveCount(1);
        hooks[0].Should().BeSameAs(entry1);
    }

    [Fact]
    public void RemoveHook_事件不存在_无操作() {
        var store = new SessionHookStore();
        store.AddHook(HookEvent.PreToolUse, CreateEntry());

        // 移除不存在的事件
        store.RemoveHook(HookEvent.PostToolUse, _ => true);

        store.GetHooks(HookEvent.PreToolUse).Should().HaveCount(1);
    }

    [Fact]
    public void RemoveHook_移除全部后_事件键也被清除() {
        var store = new SessionHookStore();
        store.AddHook(HookEvent.PreToolUse, CreateEntry("cmd"));

        store.RemoveHook(HookEvent.PreToolUse, _ => true);

        store.GetHooks(HookEvent.PreToolUse).Should().BeEmpty();
        store.GetAllHooks().Should().NotContainKey(HookEvent.PreToolUse);
    }

    [Fact]
    public void GetAllHooks_返回所有事件视图() {
        var store = new SessionHookStore();
        store.AddHook(HookEvent.PreToolUse, CreateEntry("pre"));
        store.AddHook(HookEvent.PostToolUse, CreateEntry("post"));

        var all = store.GetAllHooks();
        all.Should().HaveCount(2);
        all[HookEvent.PreToolUse].Should().HaveCount(1);
        all[HookEvent.PostToolUse].Should().HaveCount(1);
    }

    [Fact]
    public void Clear_清空所有钩子() {
        var store = new SessionHookStore();
        store.AddHook(HookEvent.PreToolUse, CreateEntry("a"));
        store.AddHook(HookEvent.PostToolUse, CreateEntry("b"));
        store.AddHook(HookEvent.SessionStart, CreateEntry("c"));

        store.Clear();

        store.GetHooks(HookEvent.PreToolUse).Should().BeEmpty();
        store.GetHooks(HookEvent.PostToolUse).Should().BeEmpty();
        store.GetHooks(HookEvent.SessionStart).Should().BeEmpty();
        store.GetAllHooks().Should().BeEmpty();
    }

    [Fact]
    public void Clear_空store_无操作() {
        var store = new SessionHookStore();
        store.Clear();
        store.GetAllHooks().Should().BeEmpty();
    }
}

/// <summary>
/// SessionHookManager 确定性测试 — 单线程下会话钩子管理确定性验证
/// <para>不依赖并发时序,验证 Add/Get/Remove/Clear 的会话隔离语义。</para>
/// </summary>
public sealed class SessionHookManagerTest {

    private static HookCommand CreateHook(string command = "echo test") {
        return new BashCommandHook { Command = command };
    }

    [Fact]
    public async Task AddSessionHookAsync_新会话_可GetSessionHooksAsync取回() {
        await using var manager = new SessionHookManager();
        var hook = CreateHook();

        await manager.AddSessionHookAsync("session-1", HookEvent.PreToolUse, "Bash", hook);

        var hooks = await manager.GetSessionHooksAsync("session-1", HookEvent.PreToolUse);
        hooks.Should().HaveCount(1);
        hooks[0].Matcher.Should().Be("Bash");
        hooks[0].Source.Should().Be(HookSource.SessionHook);
    }

    [Fact]
    public async Task AddSessionHookAsync_不同会话_互不隔离() {
        await using var manager = new SessionHookManager();

        await manager.AddSessionHookAsync("session-1", HookEvent.PreToolUse, null, CreateHook("cmd1"));
        await manager.AddSessionHookAsync("session-2", HookEvent.PreToolUse, null, CreateHook("cmd2"));

        var hooks1 = await manager.GetSessionHooksAsync("session-1", HookEvent.PreToolUse);
        var hooks2 = await manager.GetSessionHooksAsync("session-2", HookEvent.PreToolUse);

        hooks1.Should().HaveCount(1);
        hooks2.Should().HaveCount(1);
        ((BashCommandHook)hooks1[0].Command).Command.Should().Be("cmd1");
        ((BashCommandHook)hooks2[0].Command).Command.Should().Be("cmd2");
    }

    [Fact]
    public async Task GetSessionHooksAsync_不存在的会话_返回空列表() {
        await using var manager = new SessionHookManager();
        var hooks = await manager.GetSessionHooksAsync("nonexistent", HookEvent.PreToolUse);
        hooks.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSessionHooksAsync_不指定事件_返回所有事件钩子() {
        await using var manager = new SessionHookManager();
        await manager.AddSessionHookAsync("s1", HookEvent.PreToolUse, null, CreateHook());
        await manager.AddSessionHookAsync("s1", HookEvent.PostToolUse, null, CreateHook());

        var hooks = await manager.GetSessionHooksAsync("s1");
        hooks.Should().HaveCount(2);
    }

    [Fact]
    public async Task RemoveSessionHookAsync_匹配钩子_移除() {
        await using var manager = new SessionHookManager();
        var hook = CreateHook("cmd");
        await manager.AddSessionHookAsync("s1", HookEvent.PreToolUse, "Bash", hook);

        await manager.RemoveSessionHookAsync("s1", HookEvent.PreToolUse, "Bash", hook);

        var hooks = await manager.GetSessionHooksAsync("s1", HookEvent.PreToolUse);
        hooks.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveSessionHookAsync_不存在的会话_无操作() {
        await using var manager = new SessionHookManager();
        await manager.RemoveSessionHookAsync("nonexistent", HookEvent.PreToolUse, null, CreateHook());
        // 不抛异常即通过
    }

    [Fact]
    public async Task ClearSessionHooksAsync_清除指定会话所有钩子() {
        await using var manager = new SessionHookManager();
        await manager.AddSessionHookAsync("s1", HookEvent.PreToolUse, null, CreateHook());
        await manager.AddSessionHookAsync("s1", HookEvent.PostToolUse, null, CreateHook());
        await manager.AddSessionHookAsync("s2", HookEvent.PreToolUse, null, CreateHook());

        await manager.ClearSessionHooksAsync("s1");

        var s1Hooks = await manager.GetSessionHooksAsync("s1");
        var s2Hooks = await manager.GetSessionHooksAsync("s2");
        s1Hooks.Should().BeEmpty();
        s2Hooks.Should().HaveCount(1);
    }

    [Fact]
    public async Task ClearSessionHooksAsync_不存在的会话_无操作() {
        await using var manager = new SessionHookManager();
        await manager.ClearSessionHooksAsync("nonexistent");
        // 不抛异常即通过
    }

    [Fact]
    public async Task AddFunctionHookAsync_返回唯一hookId() {
        await using var manager = new SessionHookManager();
        Func<HookInput, CancellationToken, Task<HookResult>> callback = (_, _) => Task.FromResult(new HookResult { Outcome = HookOutcome.Success });

        var id1 = await manager.AddFunctionHookAsync("s1", HookEvent.PreToolUse, null, callback);
        var id2 = await manager.AddFunctionHookAsync("s1", HookEvent.PreToolUse, null, callback);

        id1.Should().NotBe(id2);
        id1.Should().StartWith("function-hook-");
    }

    [Fact]
    public async Task GetSessionFunctionHooksAsync_返回函数钩子_排除命令钩子() {
        await using var manager = new SessionHookManager();
        Func<HookInput, CancellationToken, Task<HookResult>> callback = (_, _) => Task.FromResult(new HookResult { Outcome = HookOutcome.Success });

        await manager.AddFunctionHookAsync("s1", HookEvent.PreToolUse, null, callback);
        await manager.AddSessionHookAsync("s1", HookEvent.PreToolUse, null, CreateHook());

        var functionHooks = await manager.GetSessionFunctionHooksAsync("s1", HookEvent.PreToolUse);
        var commandHooks = await manager.GetSessionHooksAsync("s1", HookEvent.PreToolUse);

        // GetSessionFunctionHooksAsync 只返回函数钩子
        functionHooks.Should().HaveCount(1);
        // GetSessionHooksAsync 排除函数钩子,只返回命令钩子
        commandHooks.Should().HaveCount(1);
        commandHooks[0].Command.Should().BeOfType<BashCommandHook>();
    }

    [Fact]
    public async Task RemoveFunctionHookAsync_按hookId移除() {
        await using var manager = new SessionHookManager();
        Func<HookInput, CancellationToken, Task<HookResult>> callback = (_, _) => Task.FromResult(new HookResult { Outcome = HookOutcome.Success });

        var hookId = await manager.AddFunctionHookAsync("s1", HookEvent.PreToolUse, null, callback);
        await manager.RemoveFunctionHookAsync("s1", HookEvent.PreToolUse, hookId);

        var hooks = await manager.GetSessionFunctionHooksAsync("s1", HookEvent.PreToolUse);
        hooks.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllSessionIds_返回所有会话ID快照() {
        await using var manager = new SessionHookManager();
        await manager.AddSessionHookAsync("s1", HookEvent.PreToolUse, null, CreateHook());
        await manager.AddSessionHookAsync("s2", HookEvent.PreToolUse, null, CreateHook());

        var ids = manager.GetAllSessionIds();
        ids.Should().HaveCount(2);
        ids.Should().Contain("s1").And.Contain("s2");
    }

    [Fact]
    public async Task ClearAllSessions_清除所有会话钩子() {
        await using var manager = new SessionHookManager();
        await manager.AddSessionHookAsync("s1", HookEvent.PreToolUse, null, CreateHook());
        await manager.AddSessionHookAsync("s2", HookEvent.PreToolUse, null, CreateHook());

        manager.ClearAllSessions();

        manager.GetAllSessionIds().Should().BeEmpty();
        (await manager.GetSessionHooksAsync("s1")).Should().BeEmpty();
        (await manager.GetSessionHooksAsync("s2")).Should().BeEmpty();
    }

    #region null 守卫 — ArgumentNullException.ThrowIfNull

    [Fact]
    public async Task AddSessionHookAsync_NullSessionId_应抛ArgumentNullException() {
        await using var manager = new SessionHookManager();
        var act = () => manager.AddSessionHookAsync(null!, HookEvent.PreToolUse, null, CreateHook());
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("sessionId");
    }

    [Fact]
    public async Task AddSessionHookAsync_NullHook_应抛ArgumentNullException() {
        await using var manager = new SessionHookManager();
        var act = () => manager.AddSessionHookAsync("s1", HookEvent.PreToolUse, null, null!);
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("hook");
    }

    [Fact]
    public async Task AddFunctionHookAsync_NullSessionId_应抛ArgumentNullException() {
        await using var manager = new SessionHookManager();
        Func<HookInput, CancellationToken, Task<HookResult>> callback = (_, _) => Task.FromResult(new HookResult { Outcome = HookOutcome.Success });
        var act = () => manager.AddFunctionHookAsync(null!, HookEvent.PreToolUse, null, callback);
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("sessionId");
    }

    [Fact]
    public async Task AddFunctionHookAsync_NullCallback_应抛ArgumentNullException() {
        await using var manager = new SessionHookManager();
        var act = () => manager.AddFunctionHookAsync("s1", HookEvent.PreToolUse, null, null!);
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("callback");
    }

    #endregion

    #region 默认超时常量

    [Fact]
    public async Task AddFunctionHookAsync_未指定timeout_应使用默认5秒() {
        await using var manager = new SessionHookManager();
        Func<HookInput, CancellationToken, Task<HookResult>> callback = (_, _) => Task.FromResult(new HookResult { Outcome = HookOutcome.Success });

        await manager.AddFunctionHookAsync("s1", HookEvent.PreToolUse, null, callback);

        var hooks = await manager.GetSessionFunctionHooksAsync("s1", HookEvent.PreToolUse);
        hooks.Should().HaveCount(1);
        hooks[0].Timeout.Should().Be(SessionHookManager.DefaultFunctionHookTimeoutSeconds);
        SessionHookManager.DefaultFunctionHookTimeoutSeconds.Should().Be(5);
    }

    [Fact]
    public async Task AddFunctionHookAsync_指定timeout_应使用指定值() {
        await using var manager = new SessionHookManager();
        Func<HookInput, CancellationToken, Task<HookResult>> callback = (_, _) => Task.FromResult(new HookResult { Outcome = HookOutcome.Success });

        await manager.AddFunctionHookAsync("s1", HookEvent.PreToolUse, null, callback, timeout: 30);

        var hooks = await manager.GetSessionFunctionHooksAsync("s1", HookEvent.PreToolUse);
        hooks.Should().HaveCount(1);
        hooks[0].Timeout.Should().Be(30);
    }

    #endregion
}
