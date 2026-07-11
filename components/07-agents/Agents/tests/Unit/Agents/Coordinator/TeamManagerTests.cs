
#pragma warning disable JCC3010, JCC3011, JCC3012
namespace Core.Tests.Agents.Coordinator;

public class TeamManagerTests
{
    private readonly ITeamManager _teamManager;

    public TeamManagerTests()
    {
        _teamManager = new TeamManager(JoinCode.Abstractions.Clock.SystemClockService.Instance);
    }

    [Fact]
    public async Task CreateTeamAsync_WithValidName_ShouldCreateTeam()
    {
        // Arrange
        var teamName = "Test Team";
        var description = "Test Description";

        // Act
        var result = await _teamManager.CreateTeamAsync(teamName, description).ConfigureAwait(true);

        // Assert
        result.Success.Should().BeTrue();
        result.Team.Should().NotBeNull();
        result.Team!.TeamName.Should().Be(teamName);
        result.Team.Description.Should().Be(description);
        result.Team.Members.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateTeamAsync_WithEmptyName_ShouldFail()
    {
        // Arrange
        var teamName = "";

        // Act
        var result = await _teamManager.CreateTeamAsync(teamName).ConfigureAwait(true);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("不能为空");
    }

    [Fact]
    public async Task CreateTeamAsync_WithInitialMembers_ShouldCreateTeamWithMembers()
    {
        // Arrange
        var teamName = "Test Team";
        var initialMembers = new List<string> { "agent1", "agent2", "agent3" };

        // Act
        var result = await _teamManager.CreateTeamAsync(teamName, initialMembers: initialMembers).ConfigureAwait(true);

        // Assert
        result.Success.Should().BeTrue();
        result.Team!.Members.Should().HaveCount(3);
        result.Team.Members.Should().Contain("agent1");
        result.Team.Members.Should().Contain("agent2");
        result.Team.Members.Should().Contain("agent3");
    }

    [Fact]
    public async Task DeleteTeamAsync_WithExistingTeam_ShouldDelete()
    {
        // Arrange
        var createResult = await _teamManager.CreateTeamAsync("Test Team").ConfigureAwait(true);
        var teamId = createResult.Team!.TeamId;

        // Act
        var deleteResult = await _teamManager.DeleteTeamAsync(teamId).ConfigureAwait(true);

        // Assert
        deleteResult.Success.Should().BeTrue();

        var team = await _teamManager.GetTeamAsync(teamId).ConfigureAwait(true);
        team.Should().BeNull();
    }

    [Fact]
    public async Task DeleteTeamAsync_WithNonExistentTeam_ShouldFail()
    {
        // Act
        var result = await _teamManager.DeleteTeamAsync("non-existent-id").ConfigureAwait(true);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("不存在");
    }

    [Fact]
    public async Task GetTeamAsync_WithExistingTeam_ShouldReturnTeam()
    {
        // Arrange
        var createResult = await _teamManager.CreateTeamAsync("Test Team").ConfigureAwait(true);
        var teamId = createResult.Team!.TeamId;

        // Act
        var team = await _teamManager.GetTeamAsync(teamId).ConfigureAwait(true);

        // Assert
        team.Should().NotBeNull();
        team!.TeamId.Should().Be(teamId);
        team.TeamName.Should().Be("Test Team");
    }

    [Fact]
    public async Task GetTeamAsync_WithNonExistentTeam_ShouldReturnNull()
    {
        // Act
        var team = await _teamManager.GetTeamAsync("non-existent-id").ConfigureAwait(true);

        // Assert
        team.Should().BeNull();
    }

    [Fact]
    public async Task ListTeamsAsync_WithMultipleTeams_ShouldReturnAll()
    {
        // Arrange
        await _teamManager.CreateTeamAsync("Team 1").ConfigureAwait(true);
        await _teamManager.CreateTeamAsync("Team 2").ConfigureAwait(true);
        await _teamManager.CreateTeamAsync("Team 3").ConfigureAwait(true);

        // Act
        var teams = await _teamManager.ListTeamsAsync().ConfigureAwait(true);

        // Assert
        teams.Should().HaveCount(3);
    }

    [Fact]
    public async Task AddTeamMemberAsync_WithValidTeam_ShouldAddMember()
    {
        // Arrange
        var createResult = await _teamManager.CreateTeamAsync("Test Team").ConfigureAwait(true);
        var teamId = createResult.Team!.TeamId;

        // Act
        var result = await _teamManager.AddTeamMemberAsync(teamId, "agent1").ConfigureAwait(true);

        // Assert
        result.Success.Should().BeTrue();

        var members = await _teamManager.GetTeamMembersAsync(teamId).ConfigureAwait(true);
        members.Should().Contain("agent1");
    }

    [Fact]
    public async Task AddTeamMemberAsync_WithNonExistentTeam_ShouldFail()
    {
        // Act
        var result = await _teamManager.AddTeamMemberAsync("non-existent-id", "agent1").ConfigureAwait(true);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("不存在");
    }

    [Fact]
    public async Task AddTeamMemberAsync_DuplicateMember_ShouldFail()
    {
        // Arrange
        var createResult = await _teamManager.CreateTeamAsync("Test Team").ConfigureAwait(true);
        var teamId = createResult.Team!.TeamId;
        await _teamManager.AddTeamMemberAsync(teamId, "agent1").ConfigureAwait(true);

        // Act
        var result = await _teamManager.AddTeamMemberAsync(teamId, "agent1").ConfigureAwait(true);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("已经是团队成员");
    }

    [Fact]
    public async Task RemoveTeamMemberAsync_WithExistingMember_ShouldRemove()
    {
        // Arrange
        var createResult = await _teamManager.CreateTeamAsync("Test Team").ConfigureAwait(true);
        var teamId = createResult.Team!.TeamId;
        await _teamManager.AddTeamMemberAsync(teamId, "agent1").ConfigureAwait(true);

        // Act
        var result = await _teamManager.RemoveTeamMemberAsync(teamId, "agent1").ConfigureAwait(true);

        // Assert
        result.Success.Should().BeTrue();

        var members = await _teamManager.GetTeamMembersAsync(teamId).ConfigureAwait(true);
        members.Should().NotContain("agent1");
    }

    [Fact]
    public async Task RemoveTeamMemberAsync_WithNonExistentMember_ShouldFail()
    {
        // Arrange
        var createResult = await _teamManager.CreateTeamAsync("Test Team").ConfigureAwait(true);
        var teamId = createResult.Team!.TeamId;

        // Act
        var result = await _teamManager.RemoveTeamMemberAsync(teamId, "non-existent-agent").ConfigureAwait(true);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("不是团队成员");
    }

    [Fact]
    public async Task SendMessageAsync_WithValidTeam_ShouldSendMessage()
    {
        // Arrange
        var createResult = await _teamManager.CreateTeamAsync("Test Team", initialMembers: new List<string> { "sender1" }).ConfigureAwait(true);
        var teamId = createResult.Team!.TeamId;

        // Act
        var result = await _teamManager.SendMessageAsync(teamId, "sender1", "Hello Team!").ConfigureAwait(true);

        // Assert
        result.Success.Should().BeTrue();

        var messages = await _teamManager.GetTeamMessagesAsync(teamId).ConfigureAwait(true);
        messages.Should().HaveCount(1);
        messages.First().Content.Should().Be("Hello Team!");
        messages.First().SenderId.Should().Be("sender1");
    }

    [Fact]
    public async Task SendMessageAsync_WithNonMemberSender_ShouldFail()
    {
        // Arrange
        var createResult = await _teamManager.CreateTeamAsync("Test Team").ConfigureAwait(true);
        var teamId = createResult.Team!.TeamId;

        // Act
        var result = await _teamManager.SendMessageAsync(teamId, "non-member", "Hello!").ConfigureAwait(true);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("不是团队成员");
    }

    [Fact]
    public async Task SendMessageToAgentAsync_WithValidAgent_ShouldSendMessage()
    {
        // Arrange
        var createResult = await _teamManager.CreateTeamAsync("Test Team", initialMembers: new List<string> { "agent1", "agent2" }).ConfigureAwait(true);
        var teamId = createResult.Team!.TeamId;

        // Act
        var result = await _teamManager.SendMessageToAgentAsync("agent2", "agent1", "Private message").ConfigureAwait(true);

        // Assert
        result.Success.Should().BeTrue();

        var messages = await _teamManager.GetTeamMessagesAsync(teamId).ConfigureAwait(true);
        messages.Should().HaveCount(1);
        messages.First().Content.Should().Contain("[私信给 agent2]");
        messages.First().Content.Should().Contain("Private message");
    }

    [Fact]
    public async Task BroadcastMessageAsync_WithValidTeam_ShouldBroadcast()
    {
        // Arrange
        var createResult = await _teamManager.CreateTeamAsync("Test Team", initialMembers: new List<string> { "sender1" }).ConfigureAwait(true);
        var teamId = createResult.Team!.TeamId;

        // Act
        var result = await _teamManager.BroadcastMessageAsync(teamId, "sender1", "Broadcast message").ConfigureAwait(true);

        // Assert
        result.Success.Should().BeTrue();

        var messages = await _teamManager.GetTeamMessagesAsync(teamId).ConfigureAwait(true);
        messages.Should().HaveCount(1);
        messages.First().Content.Should().Contain("[广播]");
    }

    [Fact]
    public async Task GetTeamMessagesAsync_WithMultipleMessages_ShouldReturnOrdered()
    {
        // Arrange
        var createResult = await _teamManager.CreateTeamAsync("Test Team", initialMembers: new List<string> { "agent1" }).ConfigureAwait(true);
        var teamId = createResult.Team!.TeamId;

        await _teamManager.SendMessageAsync(teamId, "agent1", "Message 1").ConfigureAwait(true);
        SpinWait.SpinUntil(() => true, TimeSpan.FromMilliseconds(15)); // 确保时间戳不同
        await _teamManager.SendMessageAsync(teamId, "agent1", "Message 2").ConfigureAwait(true);
        SpinWait.SpinUntil(() => true, TimeSpan.FromMilliseconds(15));
        await _teamManager.SendMessageAsync(teamId, "agent1", "Message 3").ConfigureAwait(true);

        // Act
        var messages = await _teamManager.GetTeamMessagesAsync(teamId, limit: 2).ConfigureAwait(true);

        // Assert
        messages.Should().HaveCount(2);
    }

    [Fact]
    public async Task TeamInfo_ShouldHaveCorrectTimestamps()
    {
        // Arrange
        var before = DateTime.UtcNow.AddSeconds(-1);

        // Act
        var result = await _teamManager.CreateTeamAsync("Test Team").ConfigureAwait(true);
        var after = DateTime.UtcNow.AddSeconds(1);

        // Assert
        result.Team!.CreatedAt.Should().BeOnOrAfter(before);
        result.Team.CreatedAt.Should().BeOnOrBefore(after);
        result.Team.LastActivityAt.Should().BeOnOrAfter(before);
        result.Team.LastActivityAt.Should().BeOnOrBefore(after);
    }

    [Fact]
    public async Task TeamActivity_ShouldUpdateLastActivityAt()
    {
        // Arrange
        var beforeCreate = DateTime.UtcNow.AddMilliseconds(-100);
        var createResult = await _teamManager.CreateTeamAsync("Test Team", initialMembers: new List<string> { "agent1" }).ConfigureAwait(true);
        var teamId = createResult.Team!.TeamId;

        SpinWait.SpinUntil(() => true, TimeSpan.FromMilliseconds(60)); // 等待一段时间确保时间戳不同

        // Act
        await _teamManager.SendMessageAsync(teamId, "agent1", "Test message").ConfigureAwait(true);

        // Assert
        var team = await _teamManager.GetTeamAsync(teamId).ConfigureAwait(true);
        team!.LastActivityAt.Should().BeOnOrAfter(beforeCreate);
        team.LastActivityAt.Should().BeAfter(createResult.Team.CreatedAt.AddMilliseconds(-1));
    }

    [Fact]
    public async Task TeamMessage_ShouldHaveAllProperties()
    {
        // Arrange
        var createResult = await _teamManager.CreateTeamAsync("Test Team", initialMembers: new List<string> { "agent1" }).ConfigureAwait(true);
        var teamId = createResult.Team!.TeamId;

        // Act
        await _teamManager.SendMessageAsync(teamId, "agent1", "Test content", "custom-type").ConfigureAwait(true);
        var messages = await _teamManager.GetTeamMessagesAsync(teamId).ConfigureAwait(true);

        // Assert
        var message = messages.First();
        message.MessageId.Should().NotBeNullOrEmpty();
        message.TeamId.Should().Be(teamId);
        message.SenderId.Should().Be("agent1");
        message.Content.Should().Be("Test content");
        message.MessageType.Should().Be("custom-type");
        message.Timestamp.Should().BeBefore(DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task ComplexScenario_CreateTeamAddMembersSendMessages()
    {
        // Arrange & Act - 创建团队
        var teamResult = await _teamManager.CreateTeamAsync("Development Team", "Team for development tasks").ConfigureAwait(true);
        teamResult.Success.Should().BeTrue();
        var teamId = teamResult.Team!.TeamId;

        // 添加成员
        await _teamManager.AddTeamMemberAsync(teamId, "lead").ConfigureAwait(true);
        await _teamManager.AddTeamMemberAsync(teamId, "dev1").ConfigureAwait(true);
        await _teamManager.AddTeamMemberAsync(teamId, "dev2").ConfigureAwait(true);

        // 发送消息
        await _teamManager.SendMessageAsync(teamId, "lead", "Welcome to the team!").ConfigureAwait(true);
        await _teamManager.SendMessageAsync(teamId, "dev1", "Thanks!").ConfigureAwait(true);
        await _teamManager.BroadcastMessageAsync(teamId, "lead", "Meeting at 3pm").ConfigureAwait(true);

        // 获取团队信息
        var team = await _teamManager.GetTeamAsync(teamId).ConfigureAwait(true);
        var members = await _teamManager.GetTeamMembersAsync(teamId).ConfigureAwait(true);
        var messages = await _teamManager.GetTeamMessagesAsync(teamId).ConfigureAwait(true);

        // Assert
        team.Should().NotBeNull();
        team!.TeamName.Should().Be("Development Team");
        members.Should().HaveCount(3);
        messages.Should().HaveCount(3);
    }
}
#pragma warning restore JCC3010, JCC3011, JCC3012
