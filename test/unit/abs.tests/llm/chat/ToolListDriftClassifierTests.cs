// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Abs.Tests.Llm.Chat;

/// <summary>
/// ToolListDriftClassifier 确定性单元测试 — 覆盖 Classify(public)。
/// 纯列表对比逻辑,不依赖时序/IO。
/// </summary>
public sealed class ToolListDriftClassifierTests {

    private static ToolSpec T(string name, string? desc = null, string? schema = null)
        => new(name, desc, schema);

    [Fact]
    public void Classify_BothEmpty_ReturnsIdentity() {
        var r = ToolListDriftClassifier.Classify([], []);
        r.Kind.Should().Be(ToolDriftKind.Identity);
        r.IsCacheSafe.Should().BeTrue();
    }

    [Fact]
    public void Classify_BeforeEmptyAfterHasTools_ReturnsAppend() {
        var r = ToolListDriftClassifier.Classify([], [T("a"), T("b")]);
        r.Kind.Should().Be(ToolDriftKind.Append);
        r.AddedNames.Should().Equal(["a", "b"]);
        r.IsCacheSafe.Should().BeTrue();
    }

    [Fact]
    public void Classify_PureAppend_ReturnsAppend() {
        var before = new List<ToolSpec> { T("a"), T("b") };
        var after = new List<ToolSpec> { T("a"), T("b"), T("c") };
        var r = ToolListDriftClassifier.Classify(before, after);
        r.Kind.Should().Be(ToolDriftKind.Append);
        r.AddedNames.Should().Equal(["c"]);
        r.IsCacheSafe.Should().BeTrue();
    }

    [Fact]
    public void Classify_RemoveTool_ReturnsRemove() {
        var before = new List<ToolSpec> { T("a"), T("b") };
        var after = new List<ToolSpec> { T("a") };
        var r = ToolListDriftClassifier.Classify(before, after);
        r.Kind.Should().Be(ToolDriftKind.Remove);
        r.RemovedNames.Should().Equal(["b"]);
        r.IsCacheSafe.Should().BeFalse();
    }

    [Fact]
    public void Classify_ReorderOnly_ReturnsReorder() {
        // 顺序变了但内容相同
        var before = new List<ToolSpec> { T("a"), T("b") };
        var after = new List<ToolSpec> { T("b"), T("a") };
        var r = ToolListDriftClassifier.Classify(before, after);
        r.Kind.Should().Be(ToolDriftKind.Reorder);
        r.IsCacheSafe.Should().BeFalse();
    }

    [Fact]
    public void Classify_EditDescription_ReturnsEdit() {
        var before = new List<ToolSpec> { T("a", "old") };
        var after = new List<ToolSpec> { T("a", "new") };
        var r = ToolListDriftClassifier.Classify(before, after);
        r.Kind.Should().Be(ToolDriftKind.Edit);
        r.EditedNames.Should().Equal(["a"]);
        r.IsCacheSafe.Should().BeFalse();
    }

    [Fact]
    public void Classify_EditSchema_ReturnsEdit() {
        var before = new List<ToolSpec> { T("a", "d", "schema1") };
        var after = new List<ToolSpec> { T("a", "d", "schema2") };
        var r = ToolListDriftClassifier.Classify(before, after);
        r.Kind.Should().Be(ToolDriftKind.Edit);
        r.EditedNames.Should().Equal(["a"]);
    }

    [Fact]
    public void Classify_NoChange_ReturnsIdentity() {
        var before = new List<ToolSpec> { T("a", "d", "s") };
        var after = new List<ToolSpec> { T("a", "d", "s") };
        var r = ToolListDriftClassifier.Classify(before, after);
        r.Kind.Should().Be(ToolDriftKind.Identity);
        r.IsCacheSafe.Should().BeTrue();
    }

    [Fact]
    public void Classify_NonAppendAddition_ReturnsReorder() {
        // 新工具插在前面,非追加 → Reorder
        var before = new List<ToolSpec> { T("a") };
        var after = new List<ToolSpec> { T("b"), T("a") };
        var r = ToolListDriftClassifier.Classify(before, after);
        r.Kind.Should().Be(ToolDriftKind.Reorder);
        r.AddedNames.Should().Equal(["b"]);
    }

    [Fact]
    public void Classify_NullBefore_Throws() {
        var act = () => ToolListDriftClassifier.Classify(null!, []);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Classify_NullAfter_Throws() {
        var act = () => ToolListDriftClassifier.Classify([], null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Classify_Summary_Populated() {
        var r = ToolListDriftClassifier.Classify([], [T("a")]);
        r.Summary.Should().NotBeEmpty();
        r.Summary.Should().Contain("a");
    }

    [Fact]
    public void Classify_RemoveSummary_ContainsRemovedName() {
        var r = ToolListDriftClassifier.Classify([T("x"), T("y")], [T("x")]);
        r.Summary.Should().Contain("y");
    }
}
