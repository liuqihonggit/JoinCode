namespace JoinCode.CodeIndex.Tests;

public sealed class QueryClassifierTests {
    private readonly QueryClassifier _classifier = new();

    [Fact]
    public void Classify_WhoCalls_ReturnsSymbol() {
        Assert.Equal(QueryKind.Symbol, _classifier.Classify("who calls Foo"));
    }

    [Fact]
    public void Classify_FindReferences_ReturnsSymbol() {
        Assert.Equal(QueryKind.Symbol, _classifier.Classify("find references to Bar"));
    }

    [Fact]
    public void Classify_DottedName_ReturnsSymbol() {
        Assert.Equal(QueryKind.Symbol, _classifier.Classify("Foo.Bar.Baz"));
    }

    [Fact]
    public void Classify_PascalCaseSingleWord_ReturnsSymbol() {
        Assert.Equal(QueryKind.Symbol, _classifier.Classify("StringBuilder"));
    }

    [Fact]
    public void Classify_FindSimilar_ReturnsSemantic() {
        Assert.Equal(QueryKind.Semantic, _classifier.Classify("find similar code to this pattern"));
    }

    [Fact]
    public void Classify_NaturalLanguage_ReturnsSemantic() {
        Assert.Equal(QueryKind.Semantic, _classifier.Classify("how to handle file upload in this project"));
    }

    [Fact]
    public void Classify_CodeThat_ReturnsSemantic() {
        Assert.Equal(QueryKind.Semantic, _classifier.Classify("code that processes user input"));
    }

    [Fact]
    public void Classify_Empty_ReturnsSemantic() {
        Assert.Equal(QueryKind.Semantic, _classifier.Classify(""));
    }

    [Fact]
    public void Classify_Whitespace_ReturnsSemantic() {
        Assert.Equal(QueryKind.Semantic, _classifier.Classify("   "));
    }

    [Fact]
    public void Classify_SymbolWithNaturalLanguage_ReturnsHybrid() {
        var kind = _classifier.Classify("who calls the Foo.Bar method that handles upload");
        Assert.Equal(QueryKind.Hybrid, kind);
    }
}
