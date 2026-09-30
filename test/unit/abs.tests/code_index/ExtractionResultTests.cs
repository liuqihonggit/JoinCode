namespace Abs.Tests.CodeIndex;

/// <summary>
/// ExtractionResult 扩展测试 — Chunks 字段默认空、赋值、向后兼容。
/// </summary>
public sealed class ExtractionResultTests {

    [Fact]
    public void Chunks_DefaultEmpty_WhenNotProvided() {
        var result = new ExtractionResult {
            Symbols = [],
            Calls = [],
            Dependencies = []
        };
        result.Chunks.Should().NotBeNull();
        result.Chunks.Should().BeEmpty();
    }

    [Fact]
    public void Chunks_CanBeProvided() {
        var chunk = new ChunkInfo {
            ChunkId = "c1",
            SymbolFqn = "Test.Foo",
            Kind = SymbolKind.Method,
            FilePath = "test.cs",
            StartLine = 1,
            EndLine = 5,
            LanguageId = "c-sharp",
            ContentHash = "hash1",
            SourceText = "void Foo() {}"
        };
        var result = new ExtractionResult {
            Symbols = [],
            Calls = [],
            Dependencies = [],
            Chunks = [chunk]
        };
        result.Chunks.Should().HaveCount(1);
        result.Chunks[0].ChunkId.Should().Be("c1");
    }

    [Fact]
    public void ExtractionResult_WithoutChunks_StillCompiles_BackwardCompatible() {
        var result = new ExtractionResult {
            Symbols = [new SymbolInfo {
                Name = "Foo",
                FullyQualifiedName = "Test.Foo",
                Kind = SymbolKind.Method,
                FilePath = "test.cs",
                StartLine = 1,
                EndLine = 5,
                StartColumn = 0,
                EndColumn = 10
            }],
            Calls = [],
            Dependencies = []
        };
        result.Symbols.Should().HaveCount(1);
        result.Chunks.Should().BeEmpty();
    }
}
