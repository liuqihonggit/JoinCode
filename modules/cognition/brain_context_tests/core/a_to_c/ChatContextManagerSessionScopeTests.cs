namespace Core.Tests.Context;

/// <summary>
/// ADR 0167 红测试 — 会话代次：`/clear` 换 sessionId 后，在途查询必须继续读写它出生时的会话桶。
/// 现状（红）：ChatContextManager 的桶按「当前指针」解析，命令不带 sessionId，
/// 换指针后在途查询的消息与上下文都会跟到新代 —— /clear 会连带清掉正在跑的查询。
/// </summary>
public sealed class ChatContextManagerSessionScopeTests {
    private static ChatContextManager CreateSut(string sessionId) =>
        new(new Mock<IStateService>().Object, NullLogger<ChatContextManager>.Instance,
            new ChatContextOptions { SessionId = sessionId });

    /// <summary>scope 内的写入落到 scope 的会话，不落到「当前指针」。</summary>
    [Fact]
    public async Task AddMessage_InsideScope_WritesToScopedSession() {
        await using var sut = CreateSut("s-old");
        sut.SwitchSession("s-new"); // 模拟 /clear 换代（在途查询出生在 s-old）

        using (ChatQueryScope.Enter("q-1", sessionId: "s-old")) {
            await sut.AddUserMessageAsync("在途查询的输入").ConfigureAwait(true);
        }

        sut.SwitchSession("s-old");
        var oldContents = (await sut.GetMessageListAsync().ConfigureAwait(true)).Select(m => m.Content).ToList();
        oldContents.Should().Contain("在途查询的输入");

        sut.SwitchSession("s-new");
        var newContents = (await sut.GetMessageListAsync().ConfigureAwait(true)).Select(m => m.Content).ToList();
        newContents.Should().NotContain("在途查询的输入");
    }

    /// <summary>换代发生在回合中途：回合后续消息仍落旧会话（洞 2/3 的核心断言）。</summary>
    [Fact]
    public async Task SwitchSession_DuringInFlightScope_KeepsItsWritesOnOriginalSession() {
        await using var sut = CreateSut("s-old");
        sut.SwitchSession("s-old");

        using (ChatQueryScope.Enter("q-1", sessionId: "s-old")) {
            await sut.AddUserMessageAsync("清空前提交的问题").ConfigureAwait(true);

            sut.SwitchSession("s-new"); // 回合跑到一半，用户按了 /clear

            await sut.AddAssistantMessageAsync("清空后才返回的回答").ConfigureAwait(true);
        }

        sut.SwitchSession("s-old");
        var oldContents = (await sut.GetMessageListAsync().ConfigureAwait(true)).Select(m => m.Content).ToList();
        oldContents.Should().BeEquivalentTo(["清空前提交的问题", "清空后才返回的回答"]);

        sut.SwitchSession("s-new");
        var newContents = (await sut.GetMessageListAsync().ConfigureAwait(true)).Select(m => m.Content).ToList();
        newContents.Should().BeEmpty();
    }

    /// <summary>在途查询读上下文也必须读自己的会话 — 否则 /clear 会让它「失忆」。</summary>
    [Fact]
    public async Task GetMessageList_InsideScope_ReadsScopedSession() {
        await using var sut = CreateSut("s-old");
        sut.SwitchSession("s-old");
        await sut.AddUserMessageAsync("旧上下文里的问题").ConfigureAwait(true);

        sut.SwitchSession("s-new");
        await sut.AddUserMessageAsync("新上下文里的问题").ConfigureAwait(true);

        using (ChatQueryScope.Enter("q-1", sessionId: "s-old")) {
            var scoped = (await sut.GetMessageListAsync().ConfigureAwait(true)).Select(m => m.Content).ToList();
            scoped.Should().Contain("旧上下文里的问题");
            scoped.Should().NotContain("新上下文里的问题");
        }
    }

    /// <summary>无 scope 时保持原语义 — 用当前指针（CLI 单查询路径与既有调用方零回归）。</summary>
    [Fact]
    public async Task AddMessage_WithoutScope_UsesCurrentPointer() {
        await using var sut = CreateSut("s-a");

        await sut.AddUserMessageAsync("无作用域的写入").ConfigureAwait(true);

        (await sut.GetMessageListAsync().ConfigureAwait(true)).Select(m => m.Content)
            .Should().Contain("无作用域的写入");
    }
}
