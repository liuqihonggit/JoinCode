namespace Tui.Tests.Snapshot;

/// <summary>
/// OutputView 渲染快照测试 — 验证追加文本后的 View 树状态。
/// 为 P0-1（chunk 处理）修复提供测试网：修 chunk 映射后断言 OutputView 收到正确文本。
/// </summary>
public class OutputViewSnapshotTests
{
    [Fact]
    public void Empty_NoText()
    {
        var view = new OutputView();
        var actual = ViewTreeSerializer.Serialize(view.TerminalView);
        SnapshotVerifier.Verify(actual, "OutputView_Empty");
    }

    [Fact]
    public void AppendSingleLine_TextReflects()
    {
        var view = new OutputView();
        view.AppendLine("👤 hello");

        var actual = ViewTreeSerializer.Serialize(view.TerminalView);
        SnapshotVerifier.Verify(actual, "OutputView_SingleLine");
    }

    [Fact]
    public void AppendMultipleLines_ToolChunkSequence()
    {
        var view = new OutputView();
        view.AppendLine("👤 分析项目结构");
        view.AppendLine("  [工具] Read");
        view.AppendLine("  [工具] Read 完成");
        view.AppendLine("💭 根据读取的内容...");

        var actual = ViewTreeSerializer.Serialize(view.TerminalView);
        SnapshotVerifier.Verify(actual, "OutputView_ToolSequence");
    }

    [Fact]
    public void Clear_RemovesAllText()
    {
        var view = new OutputView();
        view.AppendLine("line1");
        view.AppendLine("line2");
        view.Clear();

        var actual = ViewTreeSerializer.Serialize(view.TerminalView);
        SnapshotVerifier.Verify(actual, "OutputView_Cleared");
    }
}
