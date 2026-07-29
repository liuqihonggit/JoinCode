
namespace Bridge.Tests.Phase7C;

public sealed class BridgeStatusUtilTests
{
    [Fact]
    public void Timestamp_ReturnsFormat()
    {
        var ts = BridgeStatusUtil.Timestamp();
        // HH:mm:ss 格式
        Assert.Matches(@"^\d{2}:\d{2}:\d{2}$", ts);
    }

    [Fact]
    public void AbbreviateActivity_ShortString_ReturnsAsIs()
    {
        var result = BridgeStatusUtil.AbbreviateActivity("short");
        Assert.Equal("short", result);
    }

    [Fact]
    public void AbbreviateActivity_LongString_Truncates()
    {
        var longStr = new string('a', 50);
        var result = BridgeStatusUtil.AbbreviateActivity(longStr);
        Assert.True(result.Length <= 31); // 30 + 省略号
        Assert.EndsWith("…", result);
    }

    [Fact]
    public void BuildBridgeConnectUrl_ReturnsCorrectUrl()
    {
        var url = BridgeStatusUtil.BuildBridgeConnectUrl("env123");
        Assert.Equal("https://claude.ai/bridge/env123", url);
    }

    [Fact]
    public void BuildBridgeConnectUrl_WithCustomIngress_ReturnsCorrectUrl()
    {
        var url = BridgeStatusUtil.BuildBridgeConnectUrl("env123", "https://custom.api.com");
        Assert.Equal("https://custom.api.com/bridge/env123", url);
    }

    [Fact]
    public void BuildBridgeSessionUrl_ReturnsCorrectUrl()
    {
        var url = BridgeStatusUtil.BuildBridgeSessionUrl("sess1", "env123");
        Assert.Equal("https://claude.ai/bridge/env123/sess1", url);
    }

    [Fact]
    public void ComputeGlimmerIndex_ReturnsValidIndex()
    {
        var idx = BridgeStatusUtil.ComputeGlimmerIndex(0, 10);
        Assert.InRange(idx, 0, 9);
    }

    [Fact]
    public void ComputeShimmerSegments_SplitsText()
    {
        var (before, shimmer, after) = BridgeStatusUtil.ComputeShimmerSegments("hello", 2);
        Assert.Equal("he", before);
        Assert.Equal("l", shimmer);
        Assert.Equal("lo", after);
    }

    [Fact]
    public void GetBridgeStatus_Error_ReturnsErrorStatus()
    {
        var status = BridgeStatusUtil.GetBridgeStatus(error: true, connected: false, sessionActive: false, reconnecting: false);
        Assert.Equal("error", status.Color);
    }

    [Fact]
    public void GetBridgeStatus_Reconnecting_ReturnsWarningStatus()
    {
        var status = BridgeStatusUtil.GetBridgeStatus(error: false, connected: true, sessionActive: false, reconnecting: true);
        Assert.Equal("warning", status.Color);
    }

    [Fact]
    public void GetBridgeStatus_ActiveSession_ReturnsSuccessStatus()
    {
        var status = BridgeStatusUtil.GetBridgeStatus(error: false, connected: true, sessionActive: true, reconnecting: false);
        Assert.Equal("success", status.Color);
    }

    [Fact]
    public void WrapWithOsc8Link_ContainsEscapes()
    {
        var result = BridgeStatusUtil.WrapWithOsc8Link("click me", "https://example.com");
        Assert.Contains("\x1b]8;;", result);
        Assert.Contains("https://example.com", result);
        Assert.Contains("click me", result);
    }

    [Fact]
    public void BridgeStatusState_EnumValues()
    {
        Assert.Equal("idle", BridgeStatusState.Idle.ToValue());
        Assert.Equal("attached", BridgeStatusState.Attached.ToValue());
        Assert.Equal("titled", BridgeStatusState.Titled.ToValue());
        Assert.Equal("reconnecting", BridgeStatusState.Reconnecting.ToValue());
        Assert.Equal("failed", BridgeStatusState.Failed.ToValue());
    }
}
