namespace JoinCode.CodeIndex.Tests;

public sealed class TreeSitterParserPoolTests
{
    [Fact]
    public void Shared_ReturnsSameInstance()
    {
        var shared1 = TreeSitterParserPool.Shared;
        var shared2 = TreeSitterParserPool.Shared;

        Assert.NotNull(shared1);
        Assert.Same(shared1, shared2);
    }

    [Fact]
    public async Task AcquireSharedAsync_ReturnsReleaserThatCanBeDisposed()
    {
        var releaser = await TreeSitterParserPool.AcquireSharedAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.NotNull(releaser);
        releaser.Dispose();
    }

    [Fact]
    public void AcquireShared_ReturnsReleaserThatCanBeDisposed()
    {
        var releaser = TreeSitterParserPool.AcquireShared();
        Assert.NotNull(releaser);
        releaser.Dispose();
    }

    [Fact]
    public void CreateDisposable_ParseSimpleSource_Succeeds()
    {
        using var parser = TreeSitterParserPool.CreateDisposable();
        using var tree = parser.Parse("class A { }");

        Assert.NotNull(tree);
        Assert.False(tree.RootNode.IsError);
    }
}
