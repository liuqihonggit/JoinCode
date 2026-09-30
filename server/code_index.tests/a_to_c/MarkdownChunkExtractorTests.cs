namespace JoinCode.CodeIndex.Tests;

/// <summary>
/// MarkdownChunkExtractor 测试 — 按 heading 切块、父文档生成、SymbolKind.Document。
/// </summary>
public sealed class MarkdownChunkExtractorTests {
    private readonly MarkdownChunkExtractor _extractor = new();

    [Fact]
    public void ExtractAll_SimpleMarkdown_HasChunks() {
        var source = "# Title\n\nSome content.\n";
        var result = _extractor.ExtractAll(source, "test.md");
        Assert.NotEmpty(result.Chunks);
    }

    [Fact]
    public void ExtractAll_MultipleHeadings_MultipleChunks() {
        var source = "# A\nContent A\n## B\nContent B\n";
        var result = _extractor.ExtractAll(source, "test.md");
        Assert.True(result.Chunks.Count >= 2);
    }

    [Fact]
    public void ExtractAll_ChunkKind_IsDocument() {
        var source = "# Title\nContent\n";
        var result = _extractor.ExtractAll(source, "test.md");
        foreach (var chunk in result.Chunks) {
            Assert.Equal(SymbolKind.Document, chunk.Kind);
        }
    }

    [Fact]
    public void ExtractAll_ParentDocument_IsWholeFile() {
        var source = "# Title\nContent\n";
        var result = _extractor.ExtractAll(source, "test.md");
        var parentDoc = Assert.Single(result.ParentDocuments);
        Assert.Contains("Title", parentDoc.SourceText);
        Assert.Contains("Content", parentDoc.SourceText);
    }

    [Fact]
    public void ExtractAll_ChunkParentChunkId_PointsToFileParent() {
        var source = "# Title\nContent\n";
        var result = _extractor.ExtractAll(source, "test.md");
        var parentDoc = Assert.Single(result.ParentDocuments);
        foreach (var chunk in result.Chunks) {
            Assert.Equal(parentDoc.ChunkId, chunk.ParentChunkId);
        }
    }

    [Fact]
    public void ExtractAll_EmptySource_NoChunks() {
        var result = _extractor.ExtractAll("", "empty.md");
        Assert.Empty(result.Chunks);
        Assert.Empty(result.ParentDocuments);
    }

    [Fact]
    public void ExtractAll_NoHeading_IntroChunk() {
        var source = "Some content without heading.\n";
        var result = _extractor.ExtractAll(source, "test.md");
        var chunk = Assert.Single(result.Chunks);
        Assert.Contains("intro", chunk.SymbolFqn);
    }

    [Fact]
    public void ExtractAll_LanguageId_IsMarkdown() {
        var source = "# Title\n";
        var result = _extractor.ExtractAll(source, "test.md");
        foreach (var chunk in result.Chunks) {
            Assert.Equal("markdown", chunk.LanguageId);
        }
    }

    [Fact]
    public void ExtractAll_SymbolsCallsDeps_Empty() {
        var source = "# Title\nContent\n";
        var result = _extractor.ExtractAll(source, "test.md");
        Assert.Empty(result.Symbols);
        Assert.Empty(result.Calls);
        Assert.Empty(result.Dependencies);
    }

    [Fact]
    public void ExtractAll_HeadingTextInFqn() {
        var source = "# Authentication Guide\nContent\n";
        var result = _extractor.ExtractAll(source, "test.md");
        var chunk = Assert.Single(result.Chunks);
        Assert.Contains("Authentication", chunk.SymbolFqn);
    }
}
