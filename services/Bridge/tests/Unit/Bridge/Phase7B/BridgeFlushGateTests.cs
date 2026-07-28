
namespace Bridge.Tests.Phase7B;

public sealed class BridgeFlushGateTests
{
    [Fact]
    public void Start_End_Cycle()
    {
        var gate = new BridgeFlushGate<string>();
        Assert.False(gate.Active);

        gate.Start();
        Assert.True(gate.Active);

        gate.End();
        Assert.False(gate.Active);
    }

    [Fact]
    public void Enqueue_WhenActive_Enqueued()
    {
        var gate = new BridgeFlushGate<string>();
        gate.Start();

        var enqueued = gate.Enqueue("item1");
        Assert.True(enqueued);
    }

    [Fact]
    public void Enqueue_WhenNotActive_Dropped()
    {
        var gate = new BridgeFlushGate<string>();

        var enqueued = gate.Enqueue("item1");
        Assert.False(enqueued);
    }

    [Fact]
    public void Drop_ClearsQueueAndDeactivates()
    {
        var gate = new BridgeFlushGate<string>();
        gate.Start();
        gate.Enqueue("item1");
        gate.Enqueue("item2");

        var count = gate.Drop();
        // 对齐 TS 端: Drop 后队列清空且不再 active
        Assert.Equal(2, count);
        Assert.False(gate.Active);
        Assert.Equal(0, gate.PendingCount);
    }

    [Fact]
    public void Deactivate_StopsAndClears()
    {
        var gate = new BridgeFlushGate<string>();
        gate.Start();
        gate.Enqueue("item1");

        gate.Deactivate();
        Assert.False(gate.Active);
    }
}
