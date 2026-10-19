namespace NativeTui.Tests.Bridge;

/// <summary>
/// bridge 事件泵测试 — 后台 reader 线程把 TextReader 行分格 JSON 转并发队列
/// （Actor 邮箱模型：读线程生产，主循环消费，无共享锁）：
/// 事件收集 / 未知 type 进诊断队列 / Eof 完成 / 排空幂等。
/// </summary>
public sealed class BridgeEventPumpTests
{
    [Fact]
    public void Pump_CollectsAllEvents_UntilEof()
    {
        var reader = new StringReader(string.Join('\n',
            BridgeProtocol.SerializeEvent(new BridgeEvent.TurnStarted()),
            string.Empty,
            BridgeProtocol.SerializeEvent(new BridgeEvent.TokenDelta(24))));
        using var pump = new BridgeEventPump(reader);
        pump.Start();

        pump.Wait();

        var events = pump.DrainEvents();
        Assert.Equal(2, events.Count);
        Assert.IsType<BridgeEvent.TurnStarted>(events[0]);
        Assert.IsType<BridgeEvent.TokenDelta>(events[1]);
        Assert.True(pump.IsCompleted);
        Assert.Empty(pump.DrainDiagnostics());
    }

    [Fact]
    public void Pump_UnknownTypeLine_GoesToDiagnosticsNotEvents()
    {
        var reader = new StringReader(string.Join('\n',
            BridgeProtocol.SerializeEvent(new BridgeEvent.TurnStarted()),
            "{\"type\":\"future_event\",\"x\":1}"));
        using var pump = new BridgeEventPump(reader);
        pump.Start();

        pump.Wait();

        var events = pump.DrainEvents();
        var diagnostics = pump.DrainDiagnostics();
        Assert.Single(events);
        Assert.IsType<BridgeEvent.TurnStarted>(events[0]);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("future_event", diagnostic);
    }

    [Fact]
    public void Pump_BadJsonLine_GoesToDiagnostics()
    {
        var reader = new StringReader("not json at all\n");
        using var pump = new BridgeEventPump(reader);
        pump.Start();

        pump.Wait();

        Assert.Empty(pump.DrainEvents());
        Assert.Single(pump.DrainDiagnostics());
    }

    [Fact]
    public void DrainEvents_SecondCallAfterDrain_ReturnsEmpty()
    {
        var reader = new StringReader(BridgeProtocol.SerializeEvent(new BridgeEvent.TurnCompleted()) + "\n");
        using var pump = new BridgeEventPump(reader);
        pump.Start();

        pump.Wait();

        Assert.Single(pump.DrainEvents());
        Assert.Empty(pump.DrainEvents());
        Assert.Empty(pump.DrainDiagnostics());
    }

    [Fact]
    public void IsCompleted_BeforeStart_IsFalse()
    {
        using var pump = new BridgeEventPump(new StringReader(string.Empty));
        Assert.False(pump.IsCompleted);
    }
}
