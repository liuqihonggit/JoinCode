
#pragma warning disable JCC3010, JCC3011, JCC3012
namespace Core.Tests.Agents.Coordinator;

public class TeamManagerChatRoomTests : IAsyncLifetime {
    private readonly TeamManager _teamManager;
    private readonly Mock<ITeammateMailboxService> _mailboxServiceMock = new();

    public TeamManagerChatRoomTests() {
        _teamManager = new TeamManager(
            JoinCode.Abstractions.Clock.SystemClockService.Instance,
            mailboxService: _mailboxServiceMock.Object);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetChatRoomInfoAsync_ReturnsChatRoomIdEqualToTeamId() {
        var createResult = await _teamManager.CreateTeamAsync("测试群", initialMembers: new List<string> { "agent1" });
        var teamId = createResult.Data!.TeamId;

        var chatRoom = await _teamManager.GetChatRoomInfoAsync(teamId);

        chatRoom.Should().NotBeNull();
        chatRoom!.ChatRoomId.Should().Be(teamId);
        chatRoom.RoomName.Should().Be("测试群");
    }

    [Fact]
    public async Task GetChatRoomInfoAsync_ReturnsMembersWithRoles() {
        var createResult = await _teamManager.CreateTeamAsync("测试群", initialMembers: new List<string> { "owner", "member1" });
        var teamId = createResult.Data!.TeamId;

        var chatRoom = await _teamManager.GetChatRoomInfoAsync(teamId);

        chatRoom!.Members.Should().HaveCount(2);
        chatRoom.Members.Should().Contain(m => m.AgentId == "owner");
        chatRoom.Members.Should().Contain(m => m.AgentId == "member1");
    }

    [Fact]
    public async Task BroadcastMessageAsync_DuplicateMessageId_OnlyKeepsOne() {
        var createResult = await _teamManager.CreateTeamAsync("测试群", initialMembers: new List<string> { "sender", "receiver" });
        var teamId = createResult.Data!.TeamId;

        await _teamManager.BroadcastMessageAsync(teamId, "sender", "第一条消息");
        var messagesAfterFirst = await _teamManager.GetTeamMessagesAsync(teamId);
        messagesAfterFirst.Should().HaveCount(1);

        var firstMsg = messagesAfterFirst[0];
        var registryField = typeof(TeamManager).GetField("_registry",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var registry = (TeamRegistry)registryField!.GetValue(_teamManager)!;
        var rooms = registry.SnapshotRooms();
        var innerDict = rooms[teamId].Messages;

        var duplicateMsg = firstMsg with { };
        innerDict.TryAdd(firstMsg.MessageId, firstMsg with { Content = "不应插入" });

        var messagesAfterDuplicate = await _teamManager.GetTeamMessagesAsync(teamId);
        messagesAfterDuplicate.Should().HaveCount(1);
        messagesAfterDuplicate[0].Content.Should().Be("[广播] 第一条消息");
    }

    [Fact]
    public async Task RevokeMessageAsync_Within2Minutes_Succeeds() {
        var createResult = await _teamManager.CreateTeamAsync("测试群", initialMembers: new List<string> { "sender", "receiver" });
        var teamId = createResult.Data!.TeamId;

        await _teamManager.BroadcastMessageAsync(teamId, "sender", "待撤回消息");
        var messages = await _teamManager.GetTeamMessagesAsync(teamId);
        var messageId = messages[0].MessageId;

        var revokeResult = await _teamManager.RevokeMessageAsync(teamId, messageId, "sender", "发错了");

        revokeResult.Success.Should().BeTrue();
        var messagesAfterRevoke = await _teamManager.GetTeamMessagesAsync(teamId);
        var revokedMsg = messagesAfterRevoke.FirstOrDefault(m => m.MessageId == messageId);
        revokedMsg.Should().NotBeNull();
        revokedMsg!.Visibility.Should().Be(MessageVisibility.Hidden);
        revokedMsg.RevokeReason.Should().Be("发错了");
    }

    [Fact]
    public async Task RevokeMessageAsync_ByNonSenderNonAdmin_Fails() {
        var createResult = await _teamManager.CreateTeamAsync("测试群", initialMembers: new List<string> { "sender", "receiver" });
        var teamId = createResult.Data!.TeamId;

        await _teamManager.BroadcastMessageAsync(teamId, "sender", "消息");
        var messages = await _teamManager.GetTeamMessagesAsync(teamId);
        var messageId = messages[0].MessageId;

        var revokeResult = await _teamManager.RevokeMessageAsync(teamId, messageId, "receiver");

        revokeResult.Success.Should().BeFalse();
        revokeResult.ErrorMessage.Should().Contain("无权限");
    }

    [Fact]
    public async Task RevokeMessageAsync_NonExistentMessage_Fails() {
        var createResult = await _teamManager.CreateTeamAsync("测试群", initialMembers: new List<string> { "sender" });
        var teamId = createResult.Data!.TeamId;

        var revokeResult = await _teamManager.RevokeMessageAsync(teamId, "nonexistent_msg_id", "sender");

        revokeResult.Success.Should().BeFalse();
        revokeResult.ErrorMessage.Should().Contain("不存在");
    }

    [Fact]
    public async Task RevokeMessageAsync_BroadcastsSystemNotice() {
        var createResult = await _teamManager.CreateTeamAsync("测试群", initialMembers: new List<string> { "sender", "receiver" });
        var teamId = createResult.Data!.TeamId;

        await _teamManager.BroadcastMessageAsync(teamId, "sender", "消息");
        var messages = await _teamManager.GetTeamMessagesAsync(teamId);
        var messageId = messages[0].MessageId;

        await _teamManager.RevokeMessageAsync(teamId, messageId, "sender");

        var messagesAfterRevoke = await _teamManager.GetTeamMessagesAsync(teamId);
        messagesAfterRevoke.Should().Contain(m =>
            m.MessageType == "system_notice" &&
            m.Visibility == MessageVisibility.System &&
            m.Content.Contains("撤回了一条消息"));
    }

    [Fact]
    public async Task SystemNoticeFactory_MemberMuted_IsAdminOnlyVisibility() {
        var notice = SystemNoticeFactory.Create(SystemNoticeKind.MemberMuted, "team1", "agent1");

        notice.Visibility.Should().Be(MessageVisibility.AdminOnly);
        notice.Content.Should().Be("agent1 被禁言");
        notice.SenderId.Should().Be("system");
        notice.MessageType.Should().Be("system_notice");
    }

    [Fact]
    public async Task SystemNoticeFactory_MemberJoined_IsSystemVisibility() {
        var notice = SystemNoticeFactory.Create(SystemNoticeKind.MemberJoined, "team1", "agent1");

        notice.Visibility.Should().Be(MessageVisibility.System);
        notice.Content.Should().Be("agent1 加入聊天室");
    }

    [Fact]
    public async Task PersistTeamMessage_AdminOnly_OnlyDeliversToAdmins() {
        var createResult = await _teamManager.CreateTeamAsync("测试群", initialMembers: new List<string> { "owner", "admin1", "member1" });
        var teamId = createResult.Data!.TeamId;
        SetTeamSession(teamId, "session1");

        await _teamManager.AddTeamMemberAsync(teamId, "admin1");
        SetMemberRole(teamId, "admin1", "admin");
        var sentAgents = new List<string>();
        _mailboxServiceMock
            .Setup(m => m.SendAsync(It.IsAny<MailboxSendRequest>(), It.IsAny<CancellationToken>()))
            .Callback<MailboxSendRequest, CancellationToken>((req, _) => sentAgents.Add(req.ToAgentId))
            .Returns(() => ValueTask.FromResult<CoordinatorMessage>(null!));

        var notice = SystemNoticeFactory.Create(SystemNoticeKind.MemberMuted, teamId, "member1");
        var registryField = typeof(TeamManager).GetField("_registry",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var registry = (TeamRegistry)registryField!.GetValue(_teamManager)!;
        var rooms = registry.SnapshotRooms();
        var room = rooms[teamId];
        registry.UpdateRoom(teamId, room.TryAddMessage(notice).State);

        await InvokePersistTeamMessageAsync(teamId, notice, CancellationToken.None);

        sentAgents.Should().NotContain("member1");
        sentAgents.Should().Contain("admin1");
    }

    [Fact]
    public async Task PersistTeamMessage_Private_OnlyDeliversToToAgentId() {
        var createResult = await _teamManager.CreateTeamAsync("测试群", initialMembers: new List<string> { "sender", "target", "bystander" });
        var teamId = createResult.Data!.TeamId;
        SetTeamSession(teamId, "session1");

        var sentAgents = new List<string>();
        _mailboxServiceMock
            .Setup(m => m.SendAsync(It.IsAny<MailboxSendRequest>(), It.IsAny<CancellationToken>()))
            .Callback<MailboxSendRequest, CancellationToken>((req, _) => sentAgents.Add(req.ToAgentId))
            .Returns(() => ValueTask.FromResult<CoordinatorMessage>(null!));

        var privateMsg = new TeamMessage {
            MessageId = Guid.NewGuid().ToString("N"),
            TeamId = teamId,
            SenderId = "sender",
            Content = "私信",
            MessageType = "direct",
            Visibility = MessageVisibility.Private,
            ToAgentId = "target",
        };

        await InvokePersistTeamMessageAsync(teamId, privateMsg, CancellationToken.None);

        sentAgents.Should().ContainSingle();
        sentAgents[0].Should().Be("target");
    }

    [Fact]
    public async Task PersistTeamMessage_Hidden_DoesNotDeliver() {
        var createResult = await _teamManager.CreateTeamAsync("测试群", initialMembers: new List<string> { "sender", "receiver" });
        var teamId = createResult.Data!.TeamId;
        SetTeamSession(teamId, "session1");

        var hiddenMsg = new TeamMessage {
            MessageId = Guid.NewGuid().ToString("N"),
            TeamId = teamId,
            SenderId = "sender",
            Content = "已撤回",
            MessageType = "text",
            Visibility = MessageVisibility.Hidden,
        };

        await InvokePersistTeamMessageAsync(teamId, hiddenMsg, CancellationToken.None);

        _mailboxServiceMock.Verify(m => m.SendAsync(It.IsAny<MailboxSendRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void SetTeamSession(string teamId, string sessionId) {
        var registryField = typeof(TeamManager).GetField("_registry",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var registry = (TeamRegistry)registryField!.GetValue(_teamManager)!;
        var rooms = registry.SnapshotRooms();
        if (rooms.TryGetValue(teamId, out var room)) {
            registry.UpdateRoom(teamId, room with { SessionId = sessionId });
        }
    }

    private void SetMemberRole(string teamId, string agentId, string role) {
        var registryField = typeof(TeamManager).GetField("_registry",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var registry = (TeamRegistry)registryField!.GetValue(_teamManager)!;
        var rooms = registry.SnapshotRooms();
        if (rooms.TryGetValue(teamId, out var room) && room.MemberDetails.TryGetValue(agentId, out var info)) {
            registry.UpdateRoom(teamId, room with { MemberDetails = room.MemberDetails.SetItem(agentId, info with { Role = role }) });
        }
    }

    private async Task InvokePersistTeamMessageAsync(string teamId, TeamMessage message, CancellationToken cancellationToken) {
        var dispatcherField = typeof(TeamManager).GetField("_messageDispatcher",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var dispatcher = dispatcherField!.GetValue(_teamManager)!;
        var persistMethod = typeof(TeamMessageDispatcher).GetMethod("PersistTeamMessageToMailboxAsync");
        await (Task)persistMethod!.Invoke(dispatcher, new object[] { teamId, message, cancellationToken })!;
    }
}