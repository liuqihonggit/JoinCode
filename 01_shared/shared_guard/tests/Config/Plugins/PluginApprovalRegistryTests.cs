namespace Core.Tests.Plugins;

public sealed class PluginApprovalRegistryTests
{
    [Fact]
    public void MintId_SequentialIncrement()
    {
        var registry = new PluginApprovalRegistry();
        var id1 = registry.MintId();
        var id2 = registry.MintId();
        var id3 = registry.MintId();
        Assert.Equal(1, id1.Value);
        Assert.Equal(2, id2.Value);
        Assert.Equal(3, id3.Value);
        Assert.Equal("approval-1", id1.ToString());
    }

    [Fact]
    public void ArmRequest_CreatesPendingRequest()
    {
        var fixedTime = new DateTimeOffset(2026, 9, 11, 10, 0, 0, TimeSpan.Zero);
        var registry = new PluginApprovalRegistry(() => fixedTime);
        var req = registry.ArmRequest("plugin-foo", "需要审批");
        Assert.Equal("plugin-foo", req.PluginId);
        Assert.Equal("需要审批", req.Reason);
        Assert.Equal(ApprovalState.Pending, req.State);
        Assert.Equal(fixedTime, req.CreatedAt);
        Assert.False(req.ApproveFutureVersions);
        Assert.Null(req.Feedback);
    }

    [Fact]
    public void Approve_FirstCallerWins_SecondReturnsFalse()
    {
        var registry = new PluginApprovalRegistry();
        var req = registry.ArmRequest("plugin-a");
        var first = registry.Approve(req.RequestId);
        var second = registry.Approve(req.RequestId);
        Assert.True(first);
        Assert.False(second);
        Assert.Equal(ApprovalState.Approved, req.State);
    }

    [Fact]
    public void Decline_FirstCallerWins_SecondReturnsFalse()
    {
        var registry = new PluginApprovalRegistry();
        var req = registry.ArmRequest("plugin-b");
        var first = registry.Decline(req.RequestId, "不安全");
        var second = registry.Decline(req.RequestId);
        Assert.True(first);
        Assert.False(second);
        Assert.Equal(ApprovalState.Declined, req.State);
        Assert.Equal("不安全", req.Feedback);
    }

    [Fact]
    public void Approve_AfterDecline_ReturnsFalse()
    {
        var registry = new PluginApprovalRegistry();
        var req = registry.ArmRequest("plugin-c");
        registry.Decline(req.RequestId);
        var approve = registry.Approve(req.RequestId);
        Assert.False(approve);
        Assert.Equal(ApprovalState.Declined, req.State);
    }

    [Fact]
    public void Decline_AfterApprove_ReturnsFalse()
    {
        var registry = new PluginApprovalRegistry();
        var req = registry.ArmRequest("plugin-d");
        registry.Approve(req.RequestId);
        var decline = registry.Decline(req.RequestId);
        Assert.False(decline);
        Assert.Equal(ApprovalState.Approved, req.State);
    }

    [Fact]
    public void Approve_WithFutureVersions_FlagStored()
    {
        var registry = new PluginApprovalRegistry();
        var req = registry.ArmRequest("plugin-e");
        registry.Approve(req.RequestId, approveFutureVersions: true);
        Assert.True(req.ApproveFutureVersions);
    }

    [Fact]
    public void Approve_NonExistent_ReturnsFalse()
    {
        var registry = new PluginApprovalRegistry();
        var result = registry.Approve(new ApprovalRequestId(999));
        Assert.False(result);
    }

    [Fact]
    public void Decline_NonExistent_ReturnsFalse()
    {
        var registry = new PluginApprovalRegistry();
        var result = registry.Decline(new ApprovalRequestId(999));
        Assert.False(result);
    }

    [Fact]
    public void PeekRequest_ReturnsRequest_WithoutChangingState()
    {
        var registry = new PluginApprovalRegistry();
        var req = registry.ArmRequest("plugin-f");
        var peeked = registry.PeekRequest(req.RequestId);
        Assert.NotNull(peeked);
        Assert.Equal(req.RequestId, peeked!.RequestId);
        Assert.Equal(ApprovalState.Pending, peeked.State);
    }

    [Fact]
    public void PeekRequest_NonExistent_ReturnsNull()
    {
        var registry = new PluginApprovalRegistry();
        var peeked = registry.PeekRequest(new ApprovalRequestId(999));
        Assert.Null(peeked);
    }

    [Fact]
    public void ClaimRequest_ReturnsRequest_ForCallerToDecide()
    {
        var registry = new PluginApprovalRegistry();
        var req = registry.ArmRequest("plugin-g");
        var claimed = registry.ClaimRequest(req.RequestId);
        Assert.NotNull(claimed);
        Assert.Equal(ApprovalState.Pending, claimed!.State);
    }

    [Fact]
    public void DisarmRequest_RemovesRequest()
    {
        var registry = new PluginApprovalRegistry();
        var req = registry.ArmRequest("plugin-h");
        var removed = registry.DisarmRequest(req.RequestId);
        Assert.True(removed);
        Assert.Null(registry.PeekRequest(req.RequestId));
    }

    [Fact]
    public void DisarmRequest_NonExistent_ReturnsFalse()
    {
        var registry = new PluginApprovalRegistry();
        var removed = registry.DisarmRequest(new ApprovalRequestId(999));
        Assert.False(removed);
    }

    [Fact]
    public void PendingRequestFor_FindsPendingByPluginId()
    {
        var registry = new PluginApprovalRegistry();
        registry.ArmRequest("plugin-x");
        var req2 = registry.ArmRequest("plugin-y");
        var found = registry.PendingRequestFor("plugin-y");
        Assert.NotNull(found);
        Assert.Equal(req2.RequestId, found!.RequestId);
    }

    [Fact]
    public void PendingRequestFor_SkipsResolvedRequests()
    {
        var registry = new PluginApprovalRegistry();
        var req = registry.ArmRequest("plugin-z");
        registry.Approve(req.RequestId);
        var found = registry.PendingRequestFor("plugin-z");
        Assert.Null(found);
    }

    [Fact]
    public void PendingRequestFor_NonExistentPlugin_ReturnsNull()
    {
        var registry = new PluginApprovalRegistry();
        var found = registry.PendingRequestFor("nonexistent");
        Assert.Null(found);
    }

    [Fact]
    public void ApprovalRequestId_Equality()
    {
        var a = new ApprovalRequestId(5);
        var b = new ApprovalRequestId(5);
        var c = new ApprovalRequestId(6);
        Assert.True(a == b);
        Assert.False(a == c);
        Assert.True(a != c);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}
