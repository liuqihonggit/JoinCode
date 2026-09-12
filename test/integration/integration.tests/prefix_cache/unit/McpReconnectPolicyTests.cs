namespace JoinCode.Abstractions.LLM.Chat;

public sealed class McpReconnectPolicyTests
{
    [Fact]
    public void Decide_Identity_Accepted()
    {
        var report = new ToolDriftReport { Kind = ToolDriftKind.Identity, Summary = "No change" };
        var result = McpReconnectPolicy.Decide(report, McpReconnectAcceptLevel.IdentityOnly);
        result.Accepted.Should().BeTrue();
        result.DriftKind.Should().Be(ToolDriftKind.Identity);
    }

    [Fact]
    public void Decide_Append_IdentityAndAppend_Accepted()
    {
        var report = new ToolDriftReport { Kind = ToolDriftKind.Append, Summary = "Appended: tool_b" };
        var result = McpReconnectPolicy.Decide(report, McpReconnectAcceptLevel.IdentityAndAppend);
        result.Accepted.Should().BeTrue();
        result.DriftKind.Should().Be(ToolDriftKind.Append);
    }

    [Fact]
    public void Decide_Append_IdentityOnly_Rejected()
    {
        var report = new ToolDriftReport { Kind = ToolDriftKind.Append, Summary = "Appended: tool_b" };
        var result = McpReconnectPolicy.Decide(report, McpReconnectAcceptLevel.IdentityOnly);
        result.Accepted.Should().BeFalse();
        result.DriftKind.Should().Be(ToolDriftKind.Append);
        result.Reason.Should().Contain("IdentityOnly");
    }

    [Fact]
    public void Decide_Edit_Rejected()
    {
        var report = new ToolDriftReport { Kind = ToolDriftKind.Edit, Summary = "Edited: tool_a" };
        var result = McpReconnectPolicy.Decide(report, McpReconnectAcceptLevel.IdentityAndAppend);
        result.Accepted.Should().BeFalse();
        result.DriftKind.Should().Be(ToolDriftKind.Edit);
        result.Reason.Should().Contain("Edit drift rejected");
    }

    [Fact]
    public void Decide_Reorder_IdentityAndAppend_Rejected()
    {
        var report = new ToolDriftReport { Kind = ToolDriftKind.Reorder, Summary = "Reordered: a→b" };
        var result = McpReconnectPolicy.Decide(report, McpReconnectAcceptLevel.IdentityAndAppend);
        result.Accepted.Should().BeFalse();
        result.DriftKind.Should().Be(ToolDriftKind.Reorder);
        result.Reason.Should().Contain("Reorder drift rejected");
    }

    [Fact]
    public void Decide_Reorder_IdentityAppendAndReorder_Accepted()
    {
        var report = new ToolDriftReport { Kind = ToolDriftKind.Reorder, Summary = "Reordered: a→b" };
        var result = McpReconnectPolicy.Decide(report, McpReconnectAcceptLevel.IdentityAppendAndReorder);
        result.Accepted.Should().BeTrue();
        result.DriftKind.Should().Be(ToolDriftKind.Reorder);
        result.Reason.Should().Contain("stable sorting");
    }

    [Fact]
    public void Decide_Remove_Rejected()
    {
        var report = new ToolDriftReport { Kind = ToolDriftKind.Remove, Summary = "Removed: tool_a" };
        var result = McpReconnectPolicy.Decide(report, McpReconnectAcceptLevel.IdentityAppendAndReorder);
        result.Accepted.Should().BeFalse();
        result.DriftKind.Should().Be(ToolDriftKind.Remove);
        result.Reason.Should().Contain("Remove drift rejected");
    }

    [Fact]
    public void Decide_Identity_AllAcceptLevels_Accepted()
    {
        var report = new ToolDriftReport { Kind = ToolDriftKind.Identity, Summary = "No change" };
        var result1 = McpReconnectPolicy.Decide(report, McpReconnectAcceptLevel.IdentityOnly);
        var result2 = McpReconnectPolicy.Decide(report, McpReconnectAcceptLevel.IdentityAndAppend);
        var result3 = McpReconnectPolicy.Decide(report, McpReconnectAcceptLevel.IdentityAppendAndReorder);
        result1.Accepted.Should().BeTrue();
        result2.Accepted.Should().BeTrue();
        result3.Accepted.Should().BeTrue();
    }

    [Fact]
    public void Decide_NullReport_Throws()
    {
        var act = () => McpReconnectPolicy.Decide(null!, McpReconnectAcceptLevel.IdentityOnly);
        act.Should().Throw<ArgumentNullException>();
    }
}
