namespace JoinCode.CodeIndex.Tests;

public sealed class ChunkExtractionTests {
    private readonly CSharpSymbolExtractor _extractor = new();

    [Fact]
    public void ExtractAll_SimpleClass_HasChunks() {
        var source = """
            public class Foo {
                public void Bar() { }
            }
            """;

        var result = _extractor.ExtractAll(source, "test.cs");

        Assert.NotEmpty(result.Chunks);
    }

    [Fact]
    public void ExtractAll_ChunkContainsSourceText() {
        var source = """
            public class Foo {
                public void Bar() { }
            }
            """;

        var result = _extractor.ExtractAll(source, "test.cs");

        var methodChunk = Assert.Single(result.Chunks, c => c.Kind == SymbolKind.Method);
        Assert.NotNull(methodChunk.SourceText);
        Assert.Contains("Bar", methodChunk.SourceText);
    }

    [Fact]
    public void ExtractAll_ChunkHasContentHash() {
        var source = """
            public class Foo {
                public void Bar() { }
            }
            """;

        var result = _extractor.ExtractAll(source, "test.cs");

        foreach (var chunk in result.Chunks) {
            Assert.False(string.IsNullOrEmpty(chunk.ContentHash));
            Assert.Equal(64, chunk.ContentHash.Length);
        }
    }

    [Fact]
    public void ExtractAll_ChunkHasCorrectMetadata() {
        var source = """
            public class Foo {
                public void Bar() { }
            }
            """;

        var result = _extractor.ExtractAll(source, "test.cs");

        var methodChunk = Assert.Single(result.Chunks, c => c.Kind == SymbolKind.Method);
        Assert.Equal("test.cs", methodChunk.FilePath);
        Assert.Equal("c-sharp", methodChunk.LanguageId);
        Assert.Contains("Bar", methodChunk.SymbolFqn);
    }

    [Fact]
    public void ExtractAll_ChunkIdIsDeterministic() {
        var source = """
            public class Foo {
                public void Bar() { }
            }
            """;

        var result1 = _extractor.ExtractAll(source, "test.cs");
        var result2 = _extractor.ExtractAll(source, "test.cs");

        var ids1 = result1.Chunks.Select(c => c.ChunkId).Order().ToList();
        var ids2 = result2.Chunks.Select(c => c.ChunkId).Order().ToList();
        Assert.Equal(ids1, ids2);
    }

    [Fact]
    public void ExtractAll_DifferentContent_DifferentChunkId() {
        var source1 = """
            public class Foo {
                public void Bar() { }
            }
            """;
        var source2 = """
            public class Foo {
                public void Baz() { }
            }
            """;

        var result1 = _extractor.ExtractAll(source1, "test.cs");
        var result2 = _extractor.ExtractAll(source2, "test.cs");

        var ids1 = new HashSet<string>(result1.Chunks.Select(c => c.ChunkId));
        var ids2 = new HashSet<string>(result2.Chunks.Select(c => c.ChunkId));
        Assert.NotEqual(ids1, ids2);
    }

    [Fact]
    public void ExtractAll_MultipleSymbols_AllHaveChunks() {
        var source = """
            public class Foo {
                public void Bar() { }
                public void Baz() { }
                public int Qux { get; set; }
            }
            """;

        var result = _extractor.ExtractAll(source, "test.cs");

        Assert.True(result.Chunks.Count >= 3);
    }

    [Fact]
    public void ExtractAll_EmptySource_HasNoChunks() {
        var result = _extractor.ExtractAll("", "empty.cs");

        Assert.Empty(result.Chunks);
    }

    [Fact]
    public void ExtractAll_ConsistentChunkCount() {
        var source = """
            public class Foo {
                public void Bar() { }
            }
            """;

        var result = _extractor.ExtractAll(source, "test.cs");

        Assert.Equal(result.Symbols.Count, result.Chunks.Count);
    }
}
