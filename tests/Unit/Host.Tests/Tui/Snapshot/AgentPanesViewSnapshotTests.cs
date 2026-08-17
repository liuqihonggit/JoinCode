namespace Host.Tests.Tui.Snapshot;

/// <summary>
/// AgentPanesView 快照测试 — 验证多 Agent 面板的注册/显示行为。
/// P0-3 组件接入：注册 agent 后可见，显示 agent 名称和输出。
/// </summary>
public class AgentPanesViewSnapshotTests
{
    [Fact]
    public void Initial_Hidden()
    {
        var view = new AgentPanesView();
        var actual = ViewTreeSerializer.Serialize(view.TerminalView);
        SnapshotVerifier.Verify(actual, "AgentPanes_Empty");
    }

    [Fact]
    public void RegisterAgent_VisibleWithName()
    {
        var view = new AgentPanesView();
        view.RegisterAgent("sub-1", "SubAgent");
        view.AppendLine("sub-1", "working...");

        var actual = ViewTreeSerializer.Serialize(view.TerminalView);
        SnapshotVerifier.Verify(actual, "AgentPanes_WithAgent");
    }
}
