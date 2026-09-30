namespace JoinCode.CodeIndex.Tests;

public sealed class OnnxEmbeddingClientTests {

    [Fact]
    public void Constructor_ExeNotFound_Throws() {
        var fs = new IO.FileSystem.InMemoryFileSystem();
        Assert.Throws<FileNotFoundException>(() =>
            new OnnxEmbeddingClient("nonexistent.exe", fs));
    }

    [Fact]
    public void Constructor_NullExePath_Throws() {
        var fs = new IO.FileSystem.InMemoryFileSystem();
        Assert.Throws<ArgumentNullException>(() =>
            new OnnxEmbeddingClient(null!, fs));
    }

    [Fact]
    public void Constructor_NullFileSystem_Throws() {
        Assert.Throws<ArgumentNullException>(() =>
            new OnnxEmbeddingClient("test.exe", null!));
    }

    [Fact]
    public void Constructor_EmptyExePath_Throws() {
        var fs = new IO.FileSystem.InMemoryFileSystem();
        Assert.Throws<FileNotFoundException>(() =>
            new OnnxEmbeddingClient("", fs));
    }
}
