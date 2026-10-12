namespace JoinCode.CodeIndex.Tests;

public sealed class TreeSitterParserPoolTests {
    [SkippableFact]
    public void Shared_ReturnsSameInstance() {
        Skip.IfNot(CSharpSymbolExtractor.IsAvailable(), "TreeSitter C# 原生库在此平台不可用");
        var shared1 = TreeSitterParserPool.Shared;
        var shared2 = TreeSitterParserPool.Shared;

        Assert.NotNull(shared1);
        Assert.Same(shared1, shared2);
    }

    [Fact]
    public async Task AcquireSharedAsync_ReturnsReleaserThatCanBeDisposed() {
        using var releaser = await TreeSitterParserPool.AcquireSharedAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.NotNull(releaser);
    }

    [Fact]
    public void AcquireShared_ReturnsReleaserThatCanBeDisposed() {
        using var releaser = TreeSitterParserPool.AcquireShared();
        Assert.NotNull(releaser);
    }

    [SkippableFact]
    public void CreateDisposable_ParseSimpleSource_Succeeds() {
        Skip.IfNot(CSharpSymbolExtractor.IsAvailable(), "TreeSitter C# 原生库在此平台不可用");
        using var parser = TreeSitterParserPool.CreateDisposable();
        using var tree = parser.Parse("class A { }");

        Assert.NotNull(tree);
        Assert.False(tree.RootNode.IsError);
    }
}