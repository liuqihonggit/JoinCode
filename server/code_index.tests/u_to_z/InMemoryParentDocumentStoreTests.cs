namespace CodeIndex.Tests.UToZ;

/// <summary>
/// InMemoryParentDocumentStore 测试 — 父文档存储的增删查、线程安全、文件级清理。
/// </summary>
public sealed class InMemoryParentDocumentStoreTests {

    private static ParentDocument CreateDoc(
        string chunkId = "parent-001",
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
    public void Add_SingleDocument_CountIncreases() {
        using var store = new InMemoryParentDocumentStore();
        store.Count.Should().Be(0);
        store.Add(CreateDoc());
        store.Count.Should().Be(1);
    }

    [Fact]
    public void Add_OverwriteSameChunkId_ReplacesDocument() {
        using var store = new InMemoryParentDocumentStore();
        store.Add(CreateDoc(sourceText: "class A { }"));
        store.Add(CreateDoc(sourceText: "class B { }"));
        store.Count.Should().Be(1);
        store.Get("parent-001")!.SourceText.Should().Be("class B { }");
    }

    [Fact]
    public void Get_ExistingChunkId_ReturnsDocument() {
        using var store = new InMemoryParentDocumentStore();
        store.Add(CreateDoc());
        var doc = store.Get("parent-001");
        doc.Should().NotBeNull();
        doc!.SymbolFqn.Should().Be("Test.Foo");
    }

    [Fact]
    public void Get_NonExistingChunkId_ReturnsNull() {
        using var store = new InMemoryParentDocumentStore();
        store.Get("nonexistent").Should().BeNull();
    }

    [Fact]
    public void AddRange_BatchInsert_AllAccessible() {
        using var store = new InMemoryParentDocumentStore();
        var docs = new[] {
            CreateDoc(chunkId: "p1", filePath: "a.cs"),
            CreateDoc(chunkId: "p2", filePath: "b.cs"),
            CreateDoc(chunkId: "p3", filePath: "a.cs")
        };
        store.AddRange(docs);
        store.Count.Should().Be(3);
        store.Get("p1").Should().NotBeNull();
        store.Get("p2").Should().NotBeNull();
        store.Get("p3").Should().NotBeNull();
    }

    [Fact]
    public void Remove_SingleChunkId_DeletesDocument() {
        using var store = new InMemoryParentDocumentStore();
        store.Add(CreateDoc());
        store.Remove("parent-001");
        store.Count.Should().Be(0);
        store.Get("parent-001").Should().BeNull();
    }

    [Fact]
    public void Remove_NonExistingChunkId_NoOp() {
        using var store = new InMemoryParentDocumentStore();
        store.Remove("nonexistent");
        store.Count.Should().Be(0);
    }

    [Fact]
    public void RemoveFile_DeletesAllDocsForFile() {
        using var store = new InMemoryParentDocumentStore();
        store.Add(CreateDoc(chunkId: "p1", filePath: "a.cs"));
        store.Add(CreateDoc(chunkId: "p2", filePath: "a.cs"));
        store.Add(CreateDoc(chunkId: "p3", filePath: "b.cs"));
        store.RemoveFile("a.cs");
        store.Count.Should().Be(1);
        store.Get("p1").Should().BeNull();
        store.Get("p2").Should().BeNull();
        store.Get("p3").Should().NotBeNull();
    }

    [Fact]
    public void RemoveFile_NonExistingFile_NoOp() {
        using var store = new InMemoryParentDocumentStore();
        store.Add(CreateDoc());
        store.RemoveFile("nonexistent.cs");
        store.Count.Should().Be(1);
    }

    [Fact]
    public void Clear_RemovesAllDocuments() {
        using var store = new InMemoryParentDocumentStore();
        store.Add(CreateDoc(chunkId: "p1"));
        store.Add(CreateDoc(chunkId: "p2", filePath: "b.cs"));
        store.Clear();
        store.Count.Should().Be(0);
    }

    [Fact]
    public void Add_NullDocument_Throws() {
        using var store = new InMemoryParentDocumentStore();
        var act = () => store.Add(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Get_NullChunkId_Throws() {
        using var store = new InMemoryParentDocumentStore();
        var act = () => store.Get(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
