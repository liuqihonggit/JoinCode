
namespace Bridge.Tests.Phase7D;

public sealed class BridgeFlushGateTests
{
    [Fact]
    public void Start_SetsActive()
    {
        var gate = new BridgeFlushGate<string>();
        Assert.False(gate.Active);

        gate.Start();
        Assert.True(gate.Active);
    }

    [Fact]
    public void Enqueue_WhenActive_QueuesAndReturnsTrue()
    {
        var gate = new BridgeFlushGate<string>();
        gate.Start();

        var result = gate.Enqueue("msg1", "msg2");
        Assert.True(result);
        Assert.Equal(2, gate.PendingCount);
    }

    [Fact]
    public void Enqueue_WhenInactive_ReturnsFalse()
    {
        var gate = new BridgeFlushGate<string>();
        // 未 start，默认 inactive

        var result = gate.Enqueue("msg1");
        Assert.False(result);
        Assert.Equal(0, gate.PendingCount);
    }

    [Fact]
    public void End_ReturnsQueuedItemsAndClears()
    {
        var gate = new BridgeFlushGate<string>();
        gate.Start();
        gate.Enqueue("msg1", "msg2");

        var items = gate.End();
        Assert.False(gate.Active);
        Assert.Equal(2, items.Length);
        Assert.Equal("msg1", items[0]);
        Assert.Equal("msg2", items[1]);
        Assert.Equal(0, gate.PendingCount);
    }

    [Fact]
    public void Drop_DiscardsItemsAndReturnsCount()
    {
        var gate = new BridgeFlushGate<string>();
        gate.Start();
        gate.Enqueue("msg1", "msg2", "msg3");

        var count = gate.Drop();
        Assert.Equal(3, count);
        Assert.False(gate.Active);
        Assert.Equal(0, gate.PendingCount);
    }

    [Fact]
    public void Deactivate_ClearsActiveButKeepsItems()
    {
        var gate = new BridgeFlushGate<string>();
        gate.Start();
        gate.Enqueue("msg1");

        gate.Deactivate();
        Assert.False(gate.Active);
        Assert.Equal(1, gate.PendingCount);
    }

    [Fact]
    public void FullLifecycle_StartEnqueueEndEnqueue()
    {
        var gate = new BridgeFlushGate<string>();

        // 第一次 flush
        gate.Start();
        gate.Enqueue("hist1");
        var batch1 = gate.End();
        Assert.Single(batch1);

        // flush 结束后，新消息不排队
        var queued = gate.Enqueue("live1");
        Assert.False(queued);
    }
}
