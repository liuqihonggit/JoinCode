namespace Agents.Tests;

public class AgentActivityHistoryTests {
    [Fact]
    public void Append_IncrementsCount() {
        var history = new AgentActivityHistory();
        history.Append(AgentActivityType.Started, "agent started");
        history.Count.Should().Be(1);
    }

    [Fact]
    public void Append_MultipleCalls_OrdersByTime() {
        var history = new AgentActivityHistory();
        history.Append(AgentActivityType.Started, "first");
        history.Append(AgentActivityType.Content, "second");
        history.Append(AgentActivityType.ToolCallStart, "third");
        var snapshot = history.Snapshot();
        snapshot.Select(e => e.Text).Should().Equal("first", "second", "third");
    }

    [Fact]
    public void Append_ExceedsMax_DropsOldest() {
        var history = new AgentActivityHistory();
        for (var i = 0; i < 205; i++)
            history.Append(AgentActivityType.Content, $"entry-{i}");
        history.Count.Should().Be(200);
        var snapshot = history.Snapshot();
        snapshot[0].Text.Should().Be("entry-5");
        snapshot[^1].Text.Should().Be("entry-204");
    }

    [Fact]
    public void Recent_ReturnsLastN() {
        var history = new AgentActivityHistory();
        for (var i = 0; i < 10; i++)
            history.Append(AgentActivityType.Content, $"entry-{i}");
        var recent = history.Recent(3);
        recent.Select(e => e.Text).Should().Equal("entry-7", "entry-8", "entry-9");
    }

    [Fact]
    public void LastActivityText_ReturnsLastEntryText() {
        var history = new AgentActivityHistory();
        history.Append(AgentActivityType.Started, "started");
        history.Append(AgentActivityType.Content, "working");
        history.LastActivityText().Should().Be("working");
    }

    [Fact]
    public void LastActivityText_EmptyHistory_ReturnsNull() {
        var history = new AgentActivityHistory();
        history.LastActivityText().Should().BeNull();
    }

    [Fact]
    public void Snapshot_EmptyHistory_ReturnsEmpty() {
        var history = new AgentActivityHistory();
        history.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public void Append_PreservesAllFields() {
        var history = new AgentActivityHistory();
        history.Append(AgentActivityType.ToolCallStart, "calling bash", "🔧", "call-1", "bash", false);
        var entry = history.Snapshot()[0];
        entry.Type.Should().Be(AgentActivityType.ToolCallStart);
        entry.Text.Should().Be("calling bash");
        entry.Glyph.Should().Be("🔧");
        entry.ToolCallId.Should().Be("call-1");
        entry.ToolName.Should().Be("bash");
        entry.IsError.Should().BeFalse();
    }

    [Fact]
    public void Append_Concurrent_NoLostEntries() {
        var history = new AgentActivityHistory();
        const int threads = 3;
        const int perThread = 50;
        Enumerable.Range(0, threads).AsParallel().ForAll(_ => {
            for (var i = 0; i < perThread; i++)
                history.Append(AgentActivityType.Content, "concurrent");
        });
        history.Count.Should().Be(threads * perThread);
    }
}
