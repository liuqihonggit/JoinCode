namespace Abs.Tests.CodeIndex;

/// <summary>
/// ParentDocument 数据模型测试 — 构造、record 相等性。
/// </summary>
public sealed class ParentDocumentTests {

    private static ParentDocument CreateDoc(
        string chunkId = "parent-chunk-001",
        string filePath = "test.cs",
        string symbolFqn = "Test.Foo",
        int startLine = 1,
        int endLine = 50,
        string sourceText = "class Foo { }") => new() {
        ChunkId = chunkId,
        FilePath = filePath,
        SymbolFqn = symbolFqn,
        StartLine = startLine,
        EndLine = endLine,
        SourceText = sourceText
    };

    [Fact]
    public void Constructor_WithAllRequired_ShouldCreate() {
        var doc = CreateDoc();
        doc.ChunkId.Should().Be("parent-chunk-001");
        doc.FilePath.Should().Be("test.cs");
        doc.SymbolFqn.Should().Be("Test.Foo");
        doc.StartLine.Should().Be(1);
        doc.EndLine.Should().Be(50);
        doc.SourceText.Should().Be("class Foo { }");
    }

    [Fact]
    public void RecordEquality_SameValues_ShouldBeEqual() {
        var d1 = CreateDoc();
        var d2 = CreateDoc();
        d1.Should().Be(d2);
        (d1 == d2).Should().BeTrue();
    }

    [Fact]
    public void RecordEquality_DifferentSourceText_ShouldNotBeEqual() {
        var d1 = CreateDoc(sourceText: "class A { }");
        var d2 = CreateDoc(sourceText: "class B { }");
        d1.Should().NotBe(d2);
    }

    [Fact]
    public void RecordEquality_DifferentChunkId_ShouldNotBeEqual() {
        var d1 = CreateDoc(chunkId: "parent-001");
        var d2 = CreateDoc(chunkId: "parent-002");
        d1.Should().NotBe(d2);
    }
}
