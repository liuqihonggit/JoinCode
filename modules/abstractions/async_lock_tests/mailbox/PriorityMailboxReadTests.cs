namespace Core.Utils;

internal sealed class TestPriorityMailbox : PriorityMailbox<string> {
    public TestPriorityMailbox(
        ActorBackpressure? high = null,
        ActorBackpressure? normal = null,
        ActorBackpressure? low = null)
        : base(high, normal, low, startConsuming: false) { }

    internal bool SendForTest(string cmd, MessagePriority priority) => TrySend(cmd, priority);

    protected override ValueTask HandleAsync(string command, MessagePriority priority, CancellationToken ct)
        => ValueTask.CompletedTask;
}

public class PriorityMailboxTryReadByPriorityTests {
    [Fact]
    public async Task all_empty_returns_false() {
        await using var mb = new TestPriorityMailbox();
        mb.TryReadByPriority(out var cmd, out var priority).Should().BeFalse();
    }

    [Fact]
    public async Task high_only_returns_high() {
        await using var mb = new TestPriorityMailbox();
        mb.SendForTest("h1", MessagePriority.High);
        mb.TryReadByPriority(out var cmd, out var priority).Should().BeTrue();
        cmd.Should().Be("h1");
        priority.Should().Be(MessagePriority.High);
    }

    [Fact]
    public async Task normal_only_returns_normal() {
        await using var mb = new TestPriorityMailbox();
        mb.SendForTest("n1", MessagePriority.Normal);
        mb.TryReadByPriority(out var cmd, out var priority).Should().BeTrue();
        cmd.Should().Be("n1");
        priority.Should().Be(MessagePriority.Normal);
    }

    [Fact]
    public async Task low_only_returns_low() {
        await using var mb = new TestPriorityMailbox();
        mb.SendForTest("l1", MessagePriority.Low);
        mb.TryReadByPriority(out var cmd, out var priority).Should().BeTrue();
        cmd.Should().Be("l1");
        priority.Should().Be(MessagePriority.Low);
    }

    [Fact]
    public async Task high_precedes_normal() {
        await using var mb = new TestPriorityMailbox();
        mb.SendForTest("n1", MessagePriority.Normal);
        mb.SendForTest("h1", MessagePriority.High);
        mb.TryReadByPriority(out var cmd, out var priority).Should().BeTrue();
        cmd.Should().Be("h1");
        priority.Should().Be(MessagePriority.High);
    }

    [Fact]
    public async Task normal_precedes_low() {
        await using var mb = new TestPriorityMailbox();
        mb.SendForTest("l1", MessagePriority.Low);
        mb.SendForTest("n1", MessagePriority.Normal);
        mb.TryReadByPriority(out var cmd, out var priority).Should().BeTrue();
        cmd.Should().Be("n1");
        priority.Should().Be(MessagePriority.Normal);
    }

    [Fact]
    public async Task high_precedes_normal_and_low() {
        await using var mb = new TestPriorityMailbox();
        mb.SendForTest("l1", MessagePriority.Low);
        mb.SendForTest("n1", MessagePriority.Normal);
        mb.SendForTest("h1", MessagePriority.High);
        mb.TryReadByPriority(out var _, out var p1).Should().BeTrue();
        p1.Should().Be(MessagePriority.High);
        mb.TryReadByPriority(out var _, out var p2).Should().BeTrue();
        p2.Should().Be(MessagePriority.Normal);
        mb.TryReadByPriority(out var _, out var p3).Should().BeTrue();
        p3.Should().Be(MessagePriority.Low);
        mb.TryReadByPriority(out var _, out _).Should().BeFalse();
    }

    [Fact]
    public async Task fifo_within_same_priority() {
        await using var mb = new TestPriorityMailbox();
        mb.SendForTest("h1", MessagePriority.High);
        mb.SendForTest("h2", MessagePriority.High);
        mb.SendForTest("h3", MessagePriority.High);
        mb.TryReadByPriority(out var c1, out _).Should().BeTrue();
        c1.Should().Be("h1");
        mb.TryReadByPriority(out var c2, out _).Should().BeTrue();
        c2.Should().Be("h2");
        mb.TryReadByPriority(out var c3, out _).Should().BeTrue();
        c3.Should().Be("h3");
    }
}
