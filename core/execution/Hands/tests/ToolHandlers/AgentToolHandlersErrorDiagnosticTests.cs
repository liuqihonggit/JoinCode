namespace Hands.Tests.ToolHandlers;

/// <summary>
/// AgentToolHandlers 错误诊断方法单元测试
/// </summary>
public class AgentToolHandlersErrorDiagnosticTests
{
    [Fact]
    public void BuildAgentIdEmptyDiagnostic_ReturnsCorrectStructure()
    {
        var diag = AgentToolHandlers.BuildAgentIdEmptyDiagnostic();
        diag.Reason.Should().Be("AgentIdEmpty");
        diag.FormattedMessage.Should().Be("agent_id cannot be empty");
        diag.Details.Should().Contain(d => d.Key == "Param" && d.Value == "agent_id");
        diag.Suggestions.Should().NotBeEmpty();
    }

    [Fact]
    public void BuildAgentNotFoundDiagnostic_ReturnsCorrectStructure()
    {
        var diag = AgentToolHandlers.BuildAgentNotFoundDiagnostic("agent-123");
        diag.Reason.Should().Be("AgentNotFound");
        diag.FormattedMessage.Should().Be("Agent not found: agent-123");
        diag.Details.Should().Contain(d => d.Key == "AgentId" && d.Value == "agent-123");
        diag.Suggestions.Should().NotBeEmpty();
    }

    [Fact]
    public void BuildStopAgentFailedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = AgentToolHandlers.BuildStopAgentFailedDiagnostic("agent-456");
        diag.Reason.Should().Be("AgentStopFailed");
        diag.FormattedMessage.Should().Be("Failed to stop agent or agent not found: agent-456");
        diag.Details.Should().Contain(d => d.Key == "AgentId" && d.Value == "agent-456");
        diag.Suggestions.Should().NotBeEmpty();
    }

    [Fact]
    public void BuildCoordinatorNotInitializedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = AgentToolHandlers.BuildCoordinatorNotInitializedDiagnostic();
        diag.Reason.Should().Be("AgentCoordinatorNotInitialized");
        diag.FormattedMessage.Should().Be(L.T(StringKey.AgentCoordinatorNotInitialized));
        diag.Details.Should().Contain(d => d.Key == "Component" && d.Value == "IAgentService");
        diag.Suggestions.Should().NotBeEmpty();
    }

    [Fact]
    public void BuildRecipientEmptyDiagnostic_ReturnsCorrectStructure()
    {
        var diag = AgentToolHandlers.BuildRecipientEmptyDiagnostic();
        diag.Reason.Should().Be("AgentRecipientEmpty");
        diag.FormattedMessage.Should().Be("Recipient (to) cannot be empty");
        diag.Details.Should().Contain(d => d.Key == "Param" && d.Value == "to");
        diag.Suggestions.Should().NotBeEmpty();
    }

    [Fact]
    public void BuildMessageEmptyDiagnostic_ReturnsCorrectStructure()
    {
        var diag = AgentToolHandlers.BuildMessageEmptyDiagnostic();
        diag.Reason.Should().Be("AgentMessageEmpty");
        diag.FormattedMessage.Should().Be("message cannot be empty");
        diag.Details.Should().Contain(d => d.Key == "Param" && d.Value == "message");
        diag.Suggestions.Should().NotBeEmpty();
    }

    [Fact]
    public void BuildBroadcastStructuredMessageDiagnostic_ReturnsCorrectStructure()
    {
        var diag = AgentToolHandlers.BuildBroadcastStructuredMessageDiagnostic("shutdown_request");
        diag.Reason.Should().Be("AgentBroadcastStructuredMessage");
        diag.FormattedMessage.Should().Be("Cannot broadcast structured message (type: shutdown_request). Send to a specific teammate instead.");
        diag.Details.Should().Contain(d => d.Key == "MessageType" && d.Value == "shutdown_request");
        diag.Details.Should().Contain(d => d.Key == "Recipient" && d.Value == "*");
        diag.Suggestions.Should().NotBeEmpty();
    }

    [Fact]
    public void BuildSendMessageFailedDiagnostic_ReturnsCorrectStructure()
    {
        var diag = AgentToolHandlers.BuildSendMessageFailedDiagnostic("teammate-1");
        diag.Reason.Should().Be("AgentSendMessageFailed");
        diag.FormattedMessage.Should().Be("Failed to send message: agent 'teammate-1' not found or messaging service unavailable");
        diag.Details.Should().Contain(d => d.Key == "Recipient" && d.Value == "teammate-1");
        diag.Suggestions.Should().NotBeEmpty();
    }

    [Fact]
    public void BuildBroadcastServiceUnavailableDiagnostic_ReturnsCorrectStructure()
    {
        var diag = AgentToolHandlers.BuildBroadcastServiceUnavailableDiagnostic();
        diag.Reason.Should().Be("AgentBroadcastServiceUnavailable");
        diag.FormattedMessage.Should().Be("Broadcast failed: team service not available");
        diag.Details.Should().Contain(d => d.Key == "Component" && d.Value == "ITeamManager");
        diag.Suggestions.Should().NotBeEmpty();
    }

    [Fact]
    public void BuildBroadcastNoTeamsDiagnostic_ReturnsCorrectStructure()
    {
        var diag = AgentToolHandlers.BuildBroadcastNoTeamsDiagnostic();
        diag.Reason.Should().Be("AgentBroadcastNoTeams");
        diag.FormattedMessage.Should().Be("Broadcast failed: no teams exist");
        diag.Suggestions.Should().NotBeEmpty();
    }
}
