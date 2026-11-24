namespace Host.Tests.Bridge;

/// <summary>
/// ADR 0165 阶段 0 红测试 — jcc 侧 bridge DTO 必须与 native_tui 双侧镜像携带 queryId。
/// 红点：CliBridge.BridgeRequest/BridgeEvent 当前没有 QueryId 属性，编译失败即证明双端契约未扩展。
/// </summary>
public sealed class BridgeQueryIdContractTests {
    [Fact]
    public void Serialize_SubmitWithQueryId_CarriesField() {
        var json = JsonSerializer.Serialize(
            new CliBridge.BridgeRequest.Submit("hello") { QueryId = "q-1" },
            CliBridge.BridgeJsonContext.Safe.BridgeRequest);

        Assert.Contains("\"type\":\"submit\"", json);
        Assert.Contains("\"queryId\":\"q-1\"", json);
    }

    [Fact]
    public void Deserialize_SubmitWithQueryId_ParsesBack() {
        var request = JsonSerializer.Deserialize(
            "{\"type\":\"submit\",\"text\":\"hi\",\"queryId\":\"q-2\"}",
            CliBridge.BridgeJsonContext.Safe.BridgeRequest);

        var submit = Assert.IsType<CliBridge.BridgeRequest.Submit>(request);
        Assert.Equal("q-2", submit.QueryId);
    }

    [Fact]
    public void Deserialize_LegacySubmitWithoutQueryId_QueryIdStaysNull() {
        var request = JsonSerializer.Deserialize(
            "{\"type\":\"submit\",\"text\":\"legacy\"}",
            CliBridge.BridgeJsonContext.Safe.BridgeRequest);

        var submit = Assert.IsType<CliBridge.BridgeRequest.Submit>(request);
        Assert.Null(submit.QueryId);
    }

    [Fact]
    public void Deserialize_TurnStartedWithQueryId_ParsesBack() {
        var evt = JsonSerializer.Deserialize(
            "{\"type\":\"turn_started\",\"queryId\":\"q-3\"}",
            CliBridge.BridgeJsonContext.Safe.BridgeEvent);

        var started = Assert.IsType<CliBridge.BridgeEvent.TurnStarted>(evt);
        Assert.Equal("q-3", started.QueryId);
    }
}
