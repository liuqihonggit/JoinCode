namespace Core.Tests.Agents.Coordinator;

public sealed class SystemNoticeFactoryTests
{
    private const string TeamId = "team_test";
    private const string ActorId = "bot小明";

    [Fact]
    public void Create_AllKinds_ReturnSystemNoticeMessage()
    {
        foreach (var kind in Enum.GetValues<SystemNoticeKind>())
        {
            var msg = SystemNoticeFactory.Create(kind, TeamId, ActorId);

            msg.SenderId.Should().Be("system");
            msg.MessageType.Should().Be("system_notice");
            msg.TeamId.Should().Be(TeamId);
            msg.MessageId.Should().NotBeNullOrEmpty();
            msg.Content.Should().NotBeNullOrEmpty();
            msg.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData(SystemNoticeKind.MemberJoined, "bot小明 加入聊天室", MessageVisibility.System)]
    [InlineData(SystemNoticeKind.MemberLeft, "bot小明 退出聊天室", MessageVisibility.System)]
    [InlineData(SystemNoticeKind.MemberMuted, "bot小明 被禁言", MessageVisibility.AdminOnly)]
    [InlineData(SystemNoticeKind.MemberUnmuted, "bot小明 被解除禁言", MessageVisibility.AdminOnly)]
    [InlineData(SystemNoticeKind.MemberKicked, "bot小明 被踢出", MessageVisibility.AdminOnly)]
    [InlineData(SystemNoticeKind.RolePromoted, "bot小明 被设为管理员", MessageVisibility.AdminOnly)]
    [InlineData(SystemNoticeKind.RoleDemoted, "bot小明 被取消管理员", MessageVisibility.AdminOnly)]
    [InlineData(SystemNoticeKind.MessageRevoked, "bot小明 撤回了一条消息", MessageVisibility.System)]
    public void Create_Kind_ReturnsExpectedContentAndVisibility(
        SystemNoticeKind kind, string expectedContent, MessageVisibility expectedVisibility)
    {
        var msg = SystemNoticeFactory.Create(kind, TeamId, ActorId);

        msg.Content.Should().Be(expectedContent);
        msg.Visibility.Should().Be(expectedVisibility);
    }

    [Fact]
    public void Create_HostChanged_IncludesNewHostInContent()
    {
        var msg = SystemNoticeFactory.Create(SystemNoticeKind.HostChanged, TeamId, "oldHost", "newHost");

        msg.Content.Should().Be("主机切换：oldHost → newHost");
        msg.Visibility.Should().Be(MessageVisibility.System);
    }

    [Fact]
    public void Create_HostChanged_WithNullExtra_ShowsUnknown()
    {
        var msg = SystemNoticeFactory.Create(SystemNoticeKind.HostChanged, TeamId, "oldHost", null);

        msg.Content.Should().Be("主机切换：oldHost → 未知");
    }

    [Fact]
    public void Create_BuildQueueBusy_IncludesQueuePositionInContent()
    {
        var msg = SystemNoticeFactory.Create(SystemNoticeKind.BuildQueueBusy, TeamId, "agent1", "3");

        msg.Content.Should().Contain("3");
        msg.Visibility.Should().Be(MessageVisibility.System);
    }

    [Fact]
    public void Create_BuildQueueBusy_WithNullExtra_ShowsUnknown()
    {
        var msg = SystemNoticeFactory.Create(SystemNoticeKind.BuildQueueBusy, TeamId, "agent1", null);

        msg.Content.Should().Contain("未知");
    }

    [Fact]
    public void Create_GeneratesUniqueMessageIds()
    {
        var msg1 = SystemNoticeFactory.Create(SystemNoticeKind.MemberJoined, TeamId, ActorId);
        var msg2 = SystemNoticeFactory.Create(SystemNoticeKind.MemberJoined, TeamId, ActorId);

        msg1.MessageId.Should().NotBe(msg2.MessageId);
    }

    [Fact]
    public void Create_AdminOnlyKinds_AreNotPublicVisibility()
    {
        var adminOnlyKinds = new[]
        {
            SystemNoticeKind.MemberMuted,
            SystemNoticeKind.MemberUnmuted,
            SystemNoticeKind.MemberKicked,
            SystemNoticeKind.RolePromoted,
            SystemNoticeKind.RoleDemoted,
        };

        foreach (var kind in adminOnlyKinds)
        {
            var msg = SystemNoticeFactory.Create(kind, TeamId, ActorId);
            msg.Visibility.Should().Be(MessageVisibility.AdminOnly,
                $"kind {kind} should be AdminOnly so non-admin members cannot see it");
        }
    }

    [Fact]
    public void Create_PublicSystemKinds_AreSystemVisibility()
    {
        var systemKinds = new[]
        {
            SystemNoticeKind.MemberJoined,
            SystemNoticeKind.MemberLeft,
            SystemNoticeKind.HostChanged,
            SystemNoticeKind.BuildQueueBusy,
            SystemNoticeKind.MessageRevoked,
        };

        foreach (var kind in systemKinds)
        {
            var msg = SystemNoticeFactory.Create(kind, TeamId, ActorId);
            msg.Visibility.Should().Be(MessageVisibility.System,
                $"kind {kind} should be System visibility for all members to see");
        }
    }
}
