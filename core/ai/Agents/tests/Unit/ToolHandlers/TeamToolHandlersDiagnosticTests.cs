namespace Agents.Tests.ToolHandlers;

/// <summary>
/// TeamToolHandlers 诊断方法单元测试
/// </summary>
public class TeamToolHandlersDiagnosticTests
{
    [Fact]
    public void BuildValidationDiagnostic_TeamCreate_ReturnsCorrectStructure()
    {
        var diag = TeamToolHandlers.BuildValidationDiagnostic("team_create", "Team name cannot be empty");
        diag.Reason.Should().Be("Validation failed for team_create");
        diag.FormattedMessage.Should().Be("Team name cannot be empty");
        diag.Details.Should().Contain(d => d.Key == "ToolName" && d.Value == "team_create");
        diag.Details.Should().Contain(d => d.Key == "ValidationError" && d.Value == "Team name cannot be empty");
        diag.Suggestions.Should().NotBeEmpty();
    }

    [Fact]
    public void BuildValidationDiagnostic_TeamDelete_ReturnsCorrectStructure()
    {
        var diag = TeamToolHandlers.BuildValidationDiagnostic("team_delete", "Team ID cannot be empty");
        diag.Reason.Should().Be("Validation failed for team_delete");
        diag.FormattedMessage.Should().Be("Team ID cannot be empty");
        diag.Details.Should().Contain(d => d.Key == "ToolName" && d.Value == "team_delete");
    }

    [Fact]
    public void BuildValidationDiagnostic_TeamGet_ReturnsCorrectStructure()
    {
        var diag = TeamToolHandlers.BuildValidationDiagnostic("team_get", "Team ID cannot be empty");
        diag.Reason.Should().Be("Validation failed for team_get");
        diag.Details.Should().Contain(d => d.Key == "ToolName" && d.Value == "team_get");
    }

    [Fact]
    public void BuildValidationDiagnostic_TeamAddMember_ReturnsCorrectStructure()
    {
        var diag = TeamToolHandlers.BuildValidationDiagnostic("team_add_member", "Agent ID cannot be empty");
        diag.Reason.Should().Be("Validation failed for team_add_member");
        diag.Details.Should().Contain(d => d.Key == "ToolName" && d.Value == "team_add_member");
    }

    [Fact]
    public void BuildValidationDiagnostic_TeamSendMessage_ReturnsCorrectStructure()
    {
        var diag = TeamToolHandlers.BuildValidationDiagnostic("team_send_message", "Content cannot be empty");
        diag.Reason.Should().Be("Validation failed for team_send_message");
        diag.Details.Should().Contain(d => d.Key == "ToolName" && d.Value == "team_send_message");
    }

    [Fact]
    public void BuildOperationFailedDiagnostic_TeamCreate_ReturnsCorrectStructure()
    {
        var diag = TeamToolHandlers.BuildOperationFailedDiagnostic("team_create", "Team already exists");
        diag.Reason.Should().Be("team_create operation failed");
        diag.FormattedMessage.Should().Be("Team already exists");
        diag.Details.Should().Contain(d => d.Key == "ToolName" && d.Value == "team_create");
        diag.Details.Should().Contain(d => d.Key == "ErrorMessage" && d.Value == "Team already exists");
        diag.Suggestions.Should().NotBeEmpty();
    }

    [Fact]
    public void BuildOperationFailedDiagnostic_TeamDelete_ReturnsCorrectStructure()
    {
        var diag = TeamToolHandlers.BuildOperationFailedDiagnostic("team_delete", "Team not found");
        diag.Reason.Should().Be("team_delete operation failed");
        diag.Details.Should().Contain(d => d.Key == "ToolName" && d.Value == "team_delete");
    }

    [Fact]
    public void BuildOperationFailedDiagnostic_TeamAddMember_ReturnsCorrectStructure()
    {
        var diag = TeamToolHandlers.BuildOperationFailedDiagnostic("team_add_member", "Agent already in team");
        diag.Reason.Should().Be("team_add_member operation failed");
        diag.Details.Should().Contain(d => d.Key == "ToolName" && d.Value == "team_add_member");
    }

    [Fact]
    public void BuildOperationFailedDiagnostic_TeamBroadcast_ReturnsCorrectStructure()
    {
        var diag = TeamToolHandlers.BuildOperationFailedDiagnostic("team_broadcast", "No active members");
        diag.Reason.Should().Be("team_broadcast operation failed");
        diag.Details.Should().Contain(d => d.Key == "ToolName" && d.Value == "team_broadcast");
    }

    [Fact]
    public void BuildTeamNotFoundDiagnostic_ReturnsCorrectStructure()
    {
        var diag = TeamToolHandlers.BuildTeamNotFoundDiagnostic("team-123");
        diag.Reason.Should().Be("Team not found");
        diag.FormattedMessage.Should().Be(L.T(StringKey.TeamNotFound, "team-123"));
        diag.Details.Should().Contain(d => d.Key == "TeamId" && d.Value == "team-123");
        diag.Suggestions.Should().NotBeEmpty();
    }
}
