namespace NativeTui.Tests.Bridge;

/// <summary>
/// ADR 0165 阶段 0 红测试 — bridge 请求/事件必须携带可选 queryId（万物皆后台的查询身份）。
/// 红点：BridgeRequest/BridgeEvent 当前没有 QueryId 属性，编译失败即证明地基未实现。
/// </summary>
public sealed class BridgeQueryIdContractTests {
    [Fact]
    public void WriteRequest_SubmitWithQueryId_CarriesField() {
        var writer = new StringWriter();
        BridgeProtocol.WriteRequest(writer, new BridgeRequest.Submit("hello") { QueryId = "q-1" });

        var line = writer.ToString();
        Assert.Contains("\"type\":\"submit\"", line);
        Assert.Contains("\"queryId\":\"q-1\"", line);
    }

    [Fact]
    public void ReadEvent_TurnStartedWithQueryId_ParsesBack() {
        var reader = new StringReader("{\"type\":\"turn_started\",\"queryId\":\"q-2\"}\n");

        var result = BridgeProtocol.TryReadEvent(reader, out var @event, out var diagnostic);

        Assert.Equal(BridgeReadResult.Event, result);
        Assert.Null(diagnostic);
        var started = Assert.IsType<BridgeEvent.TurnStarted>(@event);
        Assert.Equal("q-2", started.QueryId);
    }

    [Fact]
    public void ReadEvent_LegacyWithoutQueryId_StaysNull() {
        var reader = new StringReader("{\"type\":\"turn_completed\"}\n");

        Assert.Equal(BridgeReadResult.Event, BridgeProtocol.TryReadEvent(reader, out var @event, out _));

        var completed = Assert.IsType<BridgeEvent.TurnCompleted>(@event);
        Assert.Null(completed.QueryId);
    }
}
