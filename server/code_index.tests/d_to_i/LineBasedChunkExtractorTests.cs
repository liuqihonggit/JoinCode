namespace JoinCode.CodeIndex.Tests;

public sealed class LineBasedChunkExtractorTests {

    private static SymbolInfo Sym(string name, string fqn, SymbolKind kind, int start, int end) => new() {
        Name = name,
        FullyQualifiedName = fqn,
        Kind = kind,
        FilePath = "test.cs",
        StartLine = start,
        EndLine = end,
        StartColumn = 0,
        EndColumn = 10
    };

    [Fact]
    public void Extract_WithSymbols_ContainedSymbolKindsIncludesAllSymbolTypes() {
        var source = "class Foo {\n  Foo() {}\n  void Bar() {}\n}\n";
        var symbols = new List<SymbolInfo> {
            Sym("Foo", "Ns.Foo", SymbolKind.Class, 1, 4),
            Sym("Foo", "Ns.Foo.Foo", SymbolKind.Constructor, 2, 2),
            Sym("Bar", "Ns.Foo.Bar", SymbolKind.Method, 3, 3)
        };

        var chunks = LineBasedChunkExtractor.Extract("test.cs", source, symbols);

        Assert.NotEmpty(chunks);
        var chunk = chunks[0];
        Assert.True(BitMask.Contains(chunk.ContainedSymbolKinds, SymbolKind.Class));
        Assert.True(BitMask.Contains(chunk.ContainedSymbolKinds, SymbolKind.Constructor));
        Assert.True(BitMask.Contains(chunk.ContainedSymbolKinds, SymbolKind.Method));
    }

    [Fact]
    public void Extract_WithConstructor_ContainedSymbolKindsIncludesConstructor() {
        var source = "class Foo {\n  Foo() {}\n  void Bar() {}\n}\n";
        var symbols = new List<SymbolInfo> {
            Sym("Foo", "Ns.Foo", SymbolKind.Class, 1, 4),
            Sym("Foo", "Ns.Foo.Foo", SymbolKind.Constructor, 2, 2),
            Sym("Bar", "Ns.Foo.Bar", SymbolKind.Method, 3, 3)
        };

        var chunks = LineBasedChunkExtractor.Extract("test.cs", source, symbols);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.True(BitMask.Contains(c.ContainedSymbolKinds, SymbolKind.Constructor)));
    }

    [Fact]
    public void Extract_NoSymbols_ContainedSymbolKindsIsZero() {
        var source = "class Foo {\n  Foo() {}\n}\n";

        var chunks = LineBasedChunkExtractor.Extract("test.cs", source, []);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.Equal(0, c.ContainedSymbolKinds));
    }

    [Fact]
    public void Extract_SymbolOutsideChunkRange_NotIncludedInKinds() {
        var sb = new StringBuilder();
        for (var i = 0; i < 500; i++) sb.AppendLine($"void Method{i}() {{}}");
        sb.AppendLine("class Late {");
        sb.AppendLine("  Late() {}");
        sb.AppendLine("}");
        var source = sb.ToString();
        var symbols = new List<SymbolInfo> {
            Sym("Late", "Ns.Late", SymbolKind.Class, 501, 503),
            Sym("Late", "Ns.Late.Late", SymbolKind.Constructor, 502, 502)
        };
        for (var i = 0; i < 500; i++) {
            symbols.Add(Sym($"Method{i}", $"Ns.Method{i}", SymbolKind.Method, i + 1, i + 1));
        }

        var chunks = LineBasedChunkExtractor.Extract("test.cs", source, symbols, chunkSize: 500, overlap: 0);

        var firstChunk = chunks[0];
        Assert.False(BitMask.Contains(firstChunk.ContainedSymbolKinds, SymbolKind.Constructor));
        Assert.True(BitMask.Contains(firstChunk.ContainedSymbolKinds, SymbolKind.Method));
        var lastChunk = chunks[^1];
        Assert.True(BitMask.Contains(lastChunk.ContainedSymbolKinds, SymbolKind.Constructor));
    }
}
