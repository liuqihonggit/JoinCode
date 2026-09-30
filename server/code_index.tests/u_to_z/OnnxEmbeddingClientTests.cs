namespace JoinCode.CodeIndex.Tests;

public sealed class OnnxEmbeddingClientTests {

    [Fact]
    public void Constructor_ModelNotFound_Throws() {
        var fs = new IO.FileSystem.InMemoryFileSystem();
        Assert.Throws<FileNotFoundException>(() =>
            new OnnxEmbeddingClient("nonexistent.onnx", "vocab.txt", fs));
    }

    [Fact]
    public void Constructor_NullModelPath_Throws() {
        var fs = new IO.FileSystem.InMemoryFileSystem();
        Assert.Throws<ArgumentNullException>(() =>
            new OnnxEmbeddingClient(null!, "vocab.txt", fs));
    }

    [Fact]
    public void Constructor_NullVocabPath_Throws() {
        var fs = new IO.FileSystem.InMemoryFileSystem();
        Assert.Throws<ArgumentNullException>(() =>
            new OnnxEmbeddingClient("model.onnx", null!, fs));
    }

    [Fact]
    public void Constructor_NullFileSystem_Throws() {
        Assert.Throws<ArgumentNullException>(() =>
            new OnnxEmbeddingClient("model.onnx", "vocab.txt", null!));
    }

    [Fact]
    public async Task Constructor_VocabNotFound_Throws() {
        var fs = new IO.FileSystem.InMemoryFileSystem();
        await fs.WriteAllText("model.onnx", "fake");
        Assert.Throws<FileNotFoundException>(() =>
            new OnnxEmbeddingClient("model.onnx", "nonexistent.txt", fs));
    }
}
