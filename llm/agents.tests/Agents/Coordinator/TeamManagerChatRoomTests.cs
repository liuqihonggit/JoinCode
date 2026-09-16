
#pragma warning disable JCC3010, JCC3011, JCC3012
namespace Core.Tests.Agents.Coordinator;

public class TeamManagerChatRoomTests : IAsyncLifetime
{
    private readonly TeamManager _teamManager;
    private readonly Mock<ITeammateMailboxService> _mailboxServiceMock = new();

    public TeamManagerChatRoomTests()
    {
        _teamManager = new TeamManager(
            JoinCode.Abstractions.Clock.SystemClockService.Instance,
            mailboxService: _mailboxServiceMock.Object);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetChatRoomInfoAsync_ReturnsChatRoomIdEqualToTeamId()
    {
        var createResult = await _teamManager.CreateTeamAsync("测试群", initialMembers: new List<string> { "agent1" });
        var teamId = createResult.Data!.TeamId;

        var chatRoom = await _teamManager.GetChatRoomInfoAsync(teamId);

        chatRoom.Should().NotBeNull();
        chatRoom!.ChatRoomId.Should().Be(teamId);
        chatRoom.RoomName.Should().Be("测试群");
    }

    [Fact]
    public async Task GetChatRoomInfoAsync_ReturnsMembersWithRoles()
    {
        var createResult = await _teamManager.CreateTeamAsync("测试群", initialMembers: new List<string> { "owner", "member1" });
        var teamId = createResult.Data!.TeamId;

        var chatRoom = await _teamManager.GetChatRoomInfoAsync(teamId);

        chatRoom!.Members.Should().HaveCount(2);
        chatRoom.Members.Should().Contain(m => m.AgentId == "owner");
        chatRoom.Members.Should().Contain(m => m.AgentId == "member1");
    }

    [Fact]
    public async Task BroadcastMessageAsync_DuplicateMessageId_OnlyKeepsOne()
    {
        var createResult = await _teamManager.CreateTeamAsync("测试群", initialMembers: new List<string> { "sender", "receiver" });
        var teamId = createResult.Data!.TeamId;

        await _teamManager.BroadcastMessageAsync(teamId, "sender", "第一条消息");
        var messagesAfterFirst = await _teamManager.GetTeamMessagesAsync(teamId);
        messagesAfterFirst.Should().HaveCount(1);

        var firstMsg = messagesAfterFirst[0];
        var msgDictField = typeof(TeamManager).GetField("_teamMessages",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var msgDict = (System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentDictionary<string, TeamMessage>>)msgDictField!.GetValue(_teamManager)!;
        var innerDict = msgDict[teamId];

        var duplicateMsg = firstMsg with { };
        innerDict.TryAdd(firstMsg.MessageId, firstMsg with { Content = "不应插入" });

        var messagesAfterDuplicate = await _teamManager.GetTeamMessagesAsync(teamId);
        messagesAfterDuplicate.Should().HaveCount(1);
        messagesAfterDuplicate[0].Content.Should().Be("[广播] 第一条消息");
    }

    [Fact]
    public async Task RevokeMessageAsync_Within2Minutes_Succeeds()
    {
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
    public async Task RevokeMessageAsync_ByNonSenderNonAdmin_Fails()
    {
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
    public async Task RevokeMessageAsync_NonExistentMessage_Fails()
    {
        var createResult = await _teamManager.CreateTeamAsync("测试群", initialMembers: new List<string> { "sender" });
        var teamId = createResult.Data!.TeamId;

        var revokeResult = await _teamManager.RevokeMessageAsync(teamId, "nonexistent_msg_id", "sender");

        revokeResult.Success.Should().BeFalse();
        revokeResult.ErrorMessage.Should().Contain("不存在");
    }

    [Fact]
    public async Task RevokeMessageAsync_BroadcastsSystemNotice()
    {
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
    public async Task SystemNoticeFactory_MemberMuted_IsAdminOnlyVisibility()
    {
        var notice = SystemNoticeFactory.Create(SystemNoticeKind.MemberMuted, "team1", "agent1");

        notice.Visibility.Should().Be(MessageVisibility.AdminOnly);
        notice.Content.Should().Be("agent1 被禁言");
        notice.SenderId.Should().Be("system");
        notice.MessageType.Should().Be("system_notice");
    }

    [Fact]
    public async Task SystemNoticeFactory_MemberJoined_IsSystemVisibility()
    {
        var notice = SystemNoticeFactory.Create(SystemNoticeKind.MemberJoined, "team1", "agent1");

        notice.Visibility.Should().Be(MessageVisibility.System);
        notice.Content.Should().Be("agent1 加入聊天室");
    }
}
