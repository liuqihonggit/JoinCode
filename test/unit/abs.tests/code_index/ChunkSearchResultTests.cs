namespace Abs.Tests.CodeIndex;

/// <summary>
/// ChunkSearchResult 数据模型测试 — 构造、record 相等性。
/// </summary>
public sealed class ChunkSearchResultTests {

    private static ChunkSearchResult CreateResult(
        string chunkId = "chunk-001",
        string filePath = "test.cs",
        string symbolFqn = "Test.Foo.Bar",
        int startLine = 1,
        int endLine = 10,
        float score = 0.95f) => new() {
        ChunkId = chunkId,
        FilePath = filePath,
        SymbolFqn = symbolFqn,
        StartLine = startLine,
        EndLine = endLine,
        Score = score
    };

    [Fact]
    public void Constructor_WithAllRequired_ShouldCreate() {
        var result = CreateResult();
        result.ChunkId.Should().Be("chunk-001");
        result.FilePath.Should().Be("test.cs");
        result.SymbolFqn.Should().Be("Test.Foo.Bar");
        result.StartLine.Should().Be(1);
        result.EndLine.Should().Be(10);
        result.Score.Should().Be(0.95f);
    }

    [Fact]
    public void RecordEquality_SameValues_ShouldBeEqual() {
        var r1 = CreateResult();
        var r2 = CreateResult();
        r1.Should().Be(r2);
    }

    [Fact]
    public void RecordEquality_DifferentScore_ShouldNotBeEqual() {
        var r1 = CreateResult(score: 0.9f);
        var r2 = CreateResult(score: 0.8f);
        r1.Should().NotBe(r2);
    }
}
