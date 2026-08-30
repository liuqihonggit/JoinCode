#pragma warning disable JCC51010, JCC3010, JCC3011, JCC3012, JCC9001
namespace State.Tests;


public sealed class TranscriptFileWriterPasteStoreTests : IDisposable
{
    private readonly TestInMemFs _fs = new();
    private readonly string _tempDir;
    private readonly Mock<IPasteStore> _pasteStore = new();
    private readonly TranscriptFileWriter _writer;

    public TranscriptFileWriterPasteStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"paste_store_test_{Guid.NewGuid():N}");
        _fs.CreateDirectory(_tempDir);
        _writer = new TranscriptFileWriter(_fs, _tempDir, NullLogger.Instance, _pasteStore.Object);
    }

    [Fact]
    public async Task AppendEntryAsync_SmallContent_ShouldStoreInline()
    {
        var filePath = Path.Combine(_tempDir, "test.jsonl");
        var entry = new TranscriptEntry
        {
            SessionId = "s1",
            Role = "user",
            Content = "short text",
            Timestamp = DateTime.UtcNow
        };

        await _writer.AppendEntryAsync(filePath, entry);

        var loaded = await _writer.LoadTranscriptAsync(filePath);
        loaded.Should().HaveCount(1);
        loaded[0].Content.Should().Be("short text");
        loaded[0].ContentHash.Should().BeNull();
        _pasteStore.Verify(p => p.HashPastedText(It.IsAny<string>()), Times.Never());
    }

    [Fact]
    public async Task AppendEntryAsync_LargeContent_ShouldOffloadToPasteStore()
    {
        var largeContent = new string('x', 1025);
        var hash = "ABCDEF0123456789";

        _pasteStore.Setup(p => p.HashPastedText(largeContent)).Returns(hash);
        _pasteStore.Setup(p => p.StorePastedText(hash, largeContent));

        var filePath = Path.Combine(_tempDir, "large.jsonl");
        var entry = new TranscriptEntry
        {
            SessionId = "s1",
            Role = "user",
            Content = largeContent,
            Timestamp = DateTime.UtcNow
        };

        await _writer.AppendEntryAsync(filePath, entry);

        _pasteStore.Verify(p => p.HashPastedText(largeContent), Times.Once());
        _pasteStore.Verify(p => p.StorePastedText(hash, largeContent), Times.Once());
    }

    [Fact]
    public async Task LoadTranscriptAsync_WithContentHash_ShouldResolveFromPasteStore()
    {
        var hash = "ABCDEF0123456789";
        var originalContent = new string('x', 1025);

        _pasteStore.Setup(p => p.RetrievePastedText(hash)).Returns(originalContent);

        var filePath = Path.Combine(_tempDir, "resolve.jsonl");
        var line = $$"""[{"sessionId":"s1","role":"user","content":"","contentHash":"{{hash}}","timestamp":"2026-07-30T00:00:00Z"}]""";
        _fs.WriteAllText(filePath, line);

        var loaded = await _writer.LoadTranscriptAsync(filePath);
        loaded.Should().HaveCount(1);
        loaded[0].Content.Should().Be(originalContent);
        loaded[0].ContentHash.Should().BeNull();
    }

    [Fact]
    public async Task LoadTranscriptAsync_WithContentHash_MissingInPasteStore_ShouldKeepHash()
    {
        var hash = "MISSINGHASH0000";
        _pasteStore.Setup(p => p.RetrievePastedText(hash)).Returns((string?)null);

        var filePath = Path.Combine(_tempDir, "missing.jsonl");
        var line = $$"""[{"sessionId":"s1","role":"user","content":"","contentHash":"{{hash}}","timestamp":"2026-07-30T00:00:00Z"}]""";
        _fs.WriteAllText(filePath, line);

        var loaded = await _writer.LoadTranscriptAsync(filePath);
        loaded.Should().HaveCount(1);
        loaded[0].Content.Should().BeEmpty();
        loaded[0].ContentHash.Should().Be(hash);
    }

    [Fact]
    public async Task AppendEntryAsync_WithoutPasteStore_ShouldStoreInlineRegardlessOfSize()
    {
        var writerNoPaste = new TranscriptFileWriter(_fs, _tempDir, NullLogger.Instance, pasteStore: null);
        var largeContent = new string('y', 2000);

        var filePath = Path.Combine(_tempDir, "nopaste.jsonl");
        var entry = new TranscriptEntry
        {
            SessionId = "s1",
            Role = "user",
            Content = largeContent,
            Timestamp = DateTime.UtcNow
        };

        await writerNoPaste.AppendEntryAsync(filePath, entry);

        var loaded = await writerNoPaste.LoadTranscriptAsync(filePath);
        loaded.Should().HaveCount(1);
        loaded[0].Content.Should().Be(largeContent);
        loaded[0].ContentHash.Should().BeNull();

        writerNoPaste.Dispose();
    }

    public void Dispose()
    {
        _writer.Dispose();
    }
}
#pragma warning restore JCC51010, JCC3010, JCC3011, JCC3012, JCC9001
