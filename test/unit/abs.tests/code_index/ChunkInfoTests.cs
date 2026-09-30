namespace Abs.Tests.CodeIndex;

/// <summary>
/// ChunkInfo 数据模型测试 — 构造、默认值、record 相等性。
/// </summary>
public sealed class ChunkInfoTests {

    private static ChunkInfo CreateChunk(
        string chunkId = "chunk-001",
        string symbolFqn = "Test.Foo.Bar",
        SymbolKind kind = SymbolKind.Method,
        string filePath = "test.cs",
        int startLine = 1,
        int endLine = 10,
        string languageId = "c-sharp",
        string contentHash = "abc123",
        string? sourceText = "void Bar() { }") => new() {
        ChunkId = chunkId,
        SymbolFqn = symbolFqn,
        Kind = kind,
        FilePath = filePath,
        StartLine = startLine,
        EndLine = endLine,
        LanguageId = languageId,
        ContentHash = contentHash,
        SourceText = sourceText
    };

    [Fact]
    public void Constructor_WithAllRequired_ShouldCreate() {
        var chunk = CreateChunk();
        chunk.ChunkId.Should().Be("chunk-001");
        chunk.SymbolFqn.Should().Be("Test.Foo.Bar");
        chunk.Kind.Should().Be(SymbolKind.Method);
        chunk.FilePath.Should().Be("test.cs");
        chunk.StartLine.Should().Be(1);
        chunk.EndLine.Should().Be(10);
        chunk.LanguageId.Should().Be("c-sharp");
        chunk.ContentHash.Should().Be("abc123");
        chunk.SourceText.Should().Be("void Bar() { }");
    }

    [Fact]
    public void SourceText_CanBeNull_AfterEmbedding() {
        var chunk = CreateChunk(sourceText: null);
        chunk.SourceText.Should().BeNull();
    }

    [Fact]
    public void RecordEquality_SameValues_ShouldBeEqual() {
        var chunk1 = CreateChunk();
        var chunk2 = CreateChunk();
        chunk1.Should().Be(chunk2);
        (chunk1 == chunk2).Should().BeTrue();
    }

    [Fact]
    public void RecordEquality_DifferentChunkId_ShouldNotBeEqual() {
        var chunk1 = CreateChunk(chunkId: "chunk-001");
        var chunk2 = CreateChunk(chunkId: "chunk-002");
        chunk1.Should().NotBe(chunk2);
    }

    [Fact]
    public void RecordEquality_DifferentSourceText_ShouldNotBeEqual() {
        var chunk1 = CreateChunk(sourceText: "code A");
        var chunk2 = CreateChunk(sourceText: "code B");
        chunk1.Should().NotBe(chunk2);
    }

    [Fact]
    public void ParentChunkId_DefaultsToNull_WhenNotSet() {
        var chunk = CreateChunk();
        chunk.ParentChunkId.Should().BeNull();
    }

    [Fact]
    public void ParentChunkId_CanBeSet_ForChildChunk() {
        var chunk = CreateChunk() with { ParentChunkId = "parent-chunk-001" };
        chunk.ParentChunkId.Should().Be("parent-chunk-001");
    }

    [Fact]
    public void RecordEquality_DifferentParentChunkId_ShouldNotBeEqual() {
        var chunk1 = CreateChunk() with { ParentChunkId = "parent-A" };
        var chunk2 = CreateChunk() with { ParentChunkId = "parent-B" };
        chunk1.Should().NotBe(chunk2);
    }
}
