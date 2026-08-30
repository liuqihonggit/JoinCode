namespace JoinCode.Gui.Tests.Persistence;

/// <summary>
/// GuiSessionStore 持久化测试 — 验证会话写入/读取/列表/删除，
/// 与 CLI SessionData JSON 形状兼容（PascalCase 字段），使用 InMemoryFileSystem 无磁盘 IO。
/// </summary>
public class GuiSessionStoreTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task SaveThenLoad_RoundTripsMessages()
    {
        var fs = new InMemoryFileSystem();
        var store = new GuiSessionStore(fs, fs.CombinePath("mem", "sessions"));

        var saved = new GuiSessionData
        {
            Id = "sess-001",
            CustomTitle = "斐波那契",
            CreatedAt = DateTime.UtcNow,
            Messages =
            [
                new GuiSessionMessage { Role = "user", Content = "写个斐波那契", Timestamp = DateTime.UtcNow },
                new GuiSessionMessage { Role = "assistant", Content = "def fib(n): ...", Timestamp = DateTime.UtcNow }
            ]
        };

        store.Save(saved);
        var loaded = await Task.Run(() => store.Load("sess-001")).WaitAsync(Timeout);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be("sess-001");
        loaded.CustomTitle.Should().Be("斐波那契");
        loaded.Messages.Should().HaveCount(2);
        loaded.Messages[0].Role.Should().Be("user");
        loaded.Messages[1].Content.Should().Contain("fib");
    }

    [Fact]
    public async Task ListSessions_ReturnsSummariesSortedByLastModified()
    {
        var fs = new InMemoryFileSystem();
        var store = new GuiSessionStore(fs, fs.CombinePath("mem", "sessions"));

        store.Save(new GuiSessionData { Id = "a", CustomTitle = "A 会话", Messages = [new GuiSessionMessage { Role = "user", Content = "1" }] });
        store.Save(new GuiSessionData { Id = "b", CustomTitle = "B 会话", Messages = [new GuiSessionMessage { Role = "user", Content = "1" }] });

        var list = await Task.Run(() => store.ListSessions()).WaitAsync(Timeout);

        list.Should().HaveCount(2);
        list.Select(s => s.Id).Should().BeEquivalentTo(["a", "b"]);
        list.Should().BeInDescendingOrder(s => s.LastModified);
        list.Should().OnlyContain(s => s.MessageCount == 1);
    }

    [Fact]
    public void Delete_RemovesSessionFile()
    {
        var fs = new InMemoryFileSystem();
        var store = new GuiSessionStore(fs, fs.CombinePath("mem", "sessions"));

        store.Save(new GuiSessionData { Id = "to-delete", Messages = [new GuiSessionMessage { Role = "user", Content = "x" }] });
        store.Load("to-delete").Should().NotBeNull();

        store.Delete("to-delete").Should().BeTrue();
        store.Load("to-delete").Should().BeNull();
    }

    [Fact]
    public void Save_WithoutId_Throws()
    {
        var fs = new InMemoryFileSystem();
        var store = new GuiSessionStore(fs, fs.CombinePath("mem", "sessions"));

        var act = () => store.Save(new GuiSessionData { Messages = [] });

        act.Should().Throw<ArgumentException>();
    }

    // === T8：统一入口收敛 — Save 不再覆盖消息（消息落盘由引擎 TranscriptPersistMiddleware 负责） ===

    [Fact]
    public void TranscriptBacked_Save_PersistsMetaWithoutTouchingMessages()
    {
        var transcript = new Moq.Mock<JoinCode.Abstractions.Interfaces.ITranscriptService>();
        var store = new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions", transcript.Object);

        var ok = store.Save(new GuiSessionData
        {
            Id = "engine-session",
            CustomTitle = "重命名的标题",
            CreatedAt = DateTime.UtcNow,
            Messages = [new GuiSessionMessage { Role = "user", Content = "不应被写入" }]
        });

        ok.Should().BeTrue();
        transcript.Verify(t => t.DeleteTranscriptAsync(It.IsAny<string>()), Moq.Times.Never,
            "T8 收敛后 Save 不得清空引擎增量写入的消息");
        transcript.Verify(t => t.AppendEntriesAsync(It.IsAny<string>(), It.IsAny<System.Collections.Generic.IReadOnlyList<JoinCode.Abstractions.LLM.Chat.TranscriptEntry>>(), It.IsAny<System.Threading.CancellationToken>()),
            Moq.Times.Never, "T8 收敛后 Save 不再写消息条目（双写根因）");
        transcript.Verify(t => t.SaveSessionInfoAsync(
            "engine-session",
            It.Is<JoinCode.Abstractions.Interfaces.SessionInfo>(i => i.Id == "engine-session"),
            It.IsAny<System.Threading.CancellationToken>()), Moq.Times.Once);
        transcript.Verify(t => t.SaveCustomTitleAsync("engine-session", "重命名的标题", It.IsAny<System.Threading.CancellationToken>()),
            Moq.Times.Once);
    }
}
