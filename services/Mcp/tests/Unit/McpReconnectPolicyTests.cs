namespace Mcp.Tests;

public sealed class McpReconnectPolicyTests
{
    [Fact]
    public void Decide_Identity_AlwaysAccepted()
    {
        var report = new ToolDriftReport { Kind = ToolDriftKind.Identity, Summary = "ok" };
        var result = McpReconnectPolicy.Decide(report, McpReconnectAcceptLevel.IdentityOnly);
        result.Accepted.Should().BeTrue();
    }

    [Fact]
    public void Decide_Append_IdentityLevel_Rejected()
    {
        var report = new ToolDriftReport { Kind = ToolDriftKind.Append, Summary = "tools added" };
        var result = McpReconnectPolicy.Decide(report, McpReconnectAcceptLevel.IdentityOnly);
        result.Accepted.Should().BeFalse();
        result.DriftKind.Should().Be(ToolDriftKind.Append);
    }

    [Fact]
    public void Decide_Append_IdentityAndAppendLevel_Accepted()
    {
        var report = new ToolDriftReport { Kind = ToolDriftKind.Append, Summary = "tools added" };
        var result = McpReconnectPolicy.Decide(report, McpReconnectAcceptLevel.IdentityAndAppend);
        result.Accepted.Should().BeTrue();
    }

    [Fact]
    public void Decide_Reorder_IdentityAndAppendLevel_Rejected()
    {
        var report = new ToolDriftReport { Kind = ToolDriftKind.Reorder, Summary = "tools reordered" };
        var result = McpReconnectPolicy.Decide(report, McpReconnectAcceptLevel.IdentityAndAppend);
        result.Accepted.Should().BeFalse();
    }

    [Fact]
    public void Decide_Reorder_FullLevel_Accepted()
    {
        var report = new ToolDriftReport { Kind = ToolDriftKind.Reorder, Summary = "tools reordered" };
        var result = McpReconnectPolicy.Decide(report, McpReconnectAcceptLevel.IdentityAppendAndReorder);
        result.Accepted.Should().BeTrue();
    }

    [Fact]
    public void Decide_Edit_AlwaysRejected()
    {
        var report = new ToolDriftReport { Kind = ToolDriftKind.Edit, Summary = "tool changed" };
        var result = McpReconnectPolicy.Decide(report, McpReconnectAcceptLevel.IdentityAppendAndReorder);
        result.Accepted.Should().BeFalse();
    }

    [Fact]
    public void Decide_Remove_AlwaysRejected()
    {
        var report = new ToolDriftReport { Kind = ToolDriftKind.Remove, Summary = "tool removed" };
        var result = McpReconnectPolicy.Decide(report, McpReconnectAcceptLevel.IdentityAppendAndReorder);
        result.Accepted.Should().BeFalse();
    }

    [Fact]
    public void Decide_NullReport_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            McpReconnectPolicy.Decide(null!, McpReconnectAcceptLevel.IdentityOnly));
    }

    [Fact]
    public void Decide_Rejected_ContainsSummary()
    {
        var report = new ToolDriftReport { Kind = ToolDriftKind.Edit, Summary = "details here" };
        var result = McpReconnectPolicy.Decide(report, McpReconnectAcceptLevel.IdentityOnly);
        result.Reason.Should().Contain("details here");
    }
}
