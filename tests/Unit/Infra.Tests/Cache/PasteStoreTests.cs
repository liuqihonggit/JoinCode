namespace Infra.Tests.Cache;

using Infrastructure.Cache;
using TestInMemFs = Testing.Common.Services.InMemoryFileSystem;

public sealed class PasteStoreTests
{
    private readonly TestInMemFs _fs = new();

    private static readonly string PasteCacheDir = Path.Combine(
        WorkflowConstants.Paths.JccDirectory, "paste-cache");

    private PasteStore CreateSut()
        => new(_fs, NullLogger<PasteStore>.Instance);

    [Fact]
    public void HashPastedText_ShouldReturn16CharHex()
    {
        var sut = CreateSut();
        var hash = sut.HashPastedText("hello world");

        hash.Length.Should().Be(16);
        hash.Should().MatchRegex("^[0-9A-F]{16}$");
    }

    [Fact]
    public void HashPastedText_SameContent_ShouldReturnSameHash()
    {
        var sut = CreateSut();
        var hash1 = sut.HashPastedText("test content");
        var hash2 = sut.HashPastedText("test content");

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void HashPastedText_DifferentContent_ShouldReturnDifferentHash()
    {
        var sut = CreateSut();
        var hash1 = sut.HashPastedText("content A");
        var hash2 = sut.HashPastedText("content B");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void StorePastedText_ShouldWriteFileToDisk()
    {
        var sut = CreateSut();
        var hash = sut.HashPastedText("stored content");
        sut.StorePastedText(hash, "stored content");

        var filePath = Path.Combine(PasteCacheDir, $"{hash}.txt");
        _fs.FileExists(filePath).Should().BeTrue();
        _fs.ReadAllText(filePath).Should().Be("stored content");
    }

    [Fact]
    public void RetrievePastedText_ShouldReturnContent()
    {
        var sut = CreateSut();
        var hash = sut.HashPastedText("retrieved content");
        sut.StorePastedText(hash, "retrieved content");

        var result = sut.RetrievePastedText(hash);
        result.Should().Be("retrieved content");
    }

    [Fact]
    public void RetrievePastedText_WithNonExistentHash_ShouldReturnNull()
    {
        var sut = CreateSut();
        var result = sut.RetrievePastedText("nonexistent0000");
        result.Should().BeNull();
    }

    [Fact]
    public void StorePastedText_SameHash_ShouldOverwriteSafely()
    {
        var sut = CreateSut();
        var hash = sut.HashPastedText("original");
        sut.StorePastedText(hash, "original");
        sut.StorePastedText(hash, "updated");

        var result = sut.RetrievePastedText(hash);
        result.Should().Be("updated");
    }
}
