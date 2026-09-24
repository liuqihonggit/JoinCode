namespace Core.Tests.Agents.Coordinator;

/// <summary>
/// FileChatRoomStore 单元测试 — 验证按需加载/保存/列出/删除/清理提示 — ADR 0109 决策13。
/// </summary>
public sealed class FileChatRoomStoreTests : IDisposable {
    private readonly string _tempDir;
    private readonly PhysicalFileSystem _fs;
    private readonly FileChatRoomStore _store;
    private static int _testCounter;
    private bool _disposed;

    public FileChatRoomStoreTests() {
        _tempDir = Path.Combine(Path.GetTempPath(), "jcc_chatroom_store_test", Interlocked.Increment(ref _testCounter).ToString());
        _fs = new PhysicalFileSystem();
        _store = new FileChatRoomStore(_fs, null, _tempDir);
    }

    private static ChatRoomState CreateState(string teamId, int messageCount = 0, int maxMessageCount = 1000) {
        var state = new ChatRoomState {
            Info = new TeamInfo { TeamId = teamId, TeamName = $"群_{teamId}" },
            MaxMessageCount = maxMessageCount,
        };
        for (var i = 0; i < messageCount; i++) {
            state = state.TryAddMessage(new TeamMessage {
                MessageId = $"msg_{i:D4}",
                TeamId = teamId,
                SenderId = "sender",
                Content = $"消息 {i}",
                MessageType = "text",
                Timestamp = DateTime.UtcNow.AddSeconds(i),
            }).State;
        }
        return state;
    }

    [Fact]
    public async Task SaveAsync_LoadAsync_RoundTrip() {
        var state = CreateState("team_001", messageCount: 3);
        state = state with { SessionId = "session1" };
        state = state with { Members = state.Members.Add("agent1").Add("agent2") };
        state = state with { MemberDetails = state.MemberDetails.Add("agent1", new TeamMemberInfo { AgentId = "agent1", Role = "owner", JoinedAt = DateTime.UtcNow }) };
        state = state with { AllowedPaths = state.AllowedPaths.Add("/tmp", new TeamAllowedPath { Path = "/tmp", AccessLevel = AccessLevel.Write }) };

        await _store.SaveAsync("team_001", state);
        var loaded = await _store.LoadAsync("team_001");

        loaded.Should().NotBeNull();
        loaded!.Info.TeamId.Should().Be("team_001");
        loaded.Info.TeamName.Should().Be("群_team_001");
        loaded.SessionId.Should().Be("session1");
        loaded.Members.Should().Contain("agent1", "agent2");
        loaded.Messages.Should().HaveCount(3);
        loaded.MemberDetails.Should().ContainKey("agent1");
        loaded.MemberDetails["agent1"].Role.Should().Be("owner");
        loaded.AllowedPaths.Should().ContainKey("/tmp");
        loaded.AllowedPaths["/tmp"].AccessLevel.Should().Be(AccessLevel.Write);
    }

    [Fact]
    public async Task LoadAsync_FileNotExists_ReturnsNull() {
        var loaded = await _store.LoadAsync("nonexistent_team");
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task ListRoomIdsAsync_ReturnsAllRoomIds() {
        await _store.SaveAsync("team_a", CreateState("team_a"));
        await _store.SaveAsync("team_b", CreateState("team_b"));
        await _store.SaveAsync("team_c", CreateState("team_c"));

        var ids = await _store.ListRoomIdsAsync();
        ids.Should().Contain("team_a", "team_b", "team_c");
    }

    [Fact]
    public async Task ListRoomIdsAsync_EmptyDir_ReturnsEmpty() {
        var ids = await _store.ListRoomIdsAsync();
        ids.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_RemovesRoomFile() {
        await _store.SaveAsync("team_del", CreateState("team_del"));
        (await _store.LoadAsync("team_del")).Should().NotBeNull();

        await _store.DeleteAsync("team_del");
        (await _store.LoadAsync("team_del")).Should().BeNull();
    }

    [Fact]
    public async Task GetRoomsNeedingCleanupAsync_ReturnsOnlyRoomsOverLimit() {
        var cleanRoom = CreateState("team_clean", messageCount: 10, maxMessageCount: 1000);
        var dirtyRoom = CreateState("team_dirty", messageCount: 1500, maxMessageCount: 1000);

        await _store.SaveAsync("team_clean", cleanRoom);
        await _store.SaveAsync("team_dirty", dirtyRoom);

        var needsCleanup = await _store.GetRoomsNeedingCleanupAsync();
        needsCleanup.Should().HaveCount(1);
        needsCleanup[0].TeamId.Should().Be("team_dirty");
        needsCleanup[0].MessageCount.Should().Be(1500);
        needsCleanup[0].MaxCount.Should().Be(1000);
    }

    [Fact]
    public async Task SaveAsync_OverwriteExisting() {
        var state1 = CreateState("team_overwrite", messageCount: 1);
        await _store.SaveAsync("team_overwrite", state1);

        var state2 = CreateState("team_overwrite", messageCount: 5);
        await _store.SaveAsync("team_overwrite", state2);

        var loaded = await _store.LoadAsync("team_overwrite");
        loaded!.Messages.Should().HaveCount(5);
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        if (_fs.DirectoryExists(_tempDir)) {
            _fs.DeleteDirectory(_tempDir, recursive: true);
        }
    }
}