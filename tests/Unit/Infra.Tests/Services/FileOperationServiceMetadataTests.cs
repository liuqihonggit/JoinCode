using IO.FileSystem;

namespace Infra.Tests.Services;

/// <summary>
/// FileOperationService 新方法测试 — ReadFileWithMetadataAsync / WriteFileWithEncodingAsync
/// </summary>
public class FileOperationServiceMetadataTests
{
    private static readonly IFileSystem Fs = TestFileSystem.Current;
    private readonly FileOperationService _service;

    public FileOperationServiceMetadataTests()
    {
        _service = new FileOperationService(Fs, new FileOperationConfig());
    }

    [Fact]
    public async Task ReadFileWithMetadataAsync_UTF8NoBOM_ReturnsUTF8AndLF()
    {
        var filePath = "/test/test_utf8.txt";
        await Fs.WriteAllTextAsync(filePath, "hello\nworld\n", Encoding.UTF8).ConfigureAwait(true);

        var result = await _service.ReadFileWithMetadataAsync(filePath).ConfigureAwait(true);

        Assert.True(result.Success);
        Assert.Equal("hello\nworld\n", result.Content);
        Assert.Equal(Encoding.UTF8, result.Encoding);
        Assert.Equal("LF", result.LineEndings);
    }

    [Fact]
    public async Task ReadFileWithMetadataAsync_CRLFContent_ReturnsCRLF()
    {
        var filePath = "/test/test_crlf.txt";
        await Fs.WriteAllTextAsync(filePath, "hello\r\nworld\r\n", Encoding.UTF8).ConfigureAwait(true);

        var result = await _service.ReadFileWithMetadataAsync(filePath).ConfigureAwait(true);

        Assert.True(result.Success);
        // Content should be normalized to LF
        Assert.Equal("hello\nworld\n", result.Content);
        Assert.Equal("CRLF", result.LineEndings);
    }

    [Fact]
    public async Task ReadFileWithMetadataAsync_UTF16LE_ReturnsUnicodeEncoding()
    {
        var filePath = "/test/test_utf16.txt";
        await Fs.WriteAllTextAsync(filePath, "hello\n", Encoding.Unicode).ConfigureAwait(true);

        var result = await _service.ReadFileWithMetadataAsync(filePath).ConfigureAwait(true);

        Assert.True(result.Success);
        Assert.Equal(Encoding.Unicode, result.Encoding);
    }

    [Fact]
    public async Task ReadFileWithMetadataAsync_NonExistentFile_ReturnsFailure()
    {
        var filePath = "/test/nonexistent.txt";

        var result = await _service.ReadFileWithMetadataAsync(filePath).ConfigureAwait(true);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task WriteFileWithEncodingAsync_UTF8_WritesCorrectly()
    {
        var filePath = "/test/write_utf8.txt";
        var content = "hello\nworld\n";

        var result = await _service.WriteFileWithEncodingAsync(filePath, content, Encoding.UTF8, "LF").ConfigureAwait(true);

        Assert.True(result.Success);
        var written = await Fs.ReadAllTextAsync(filePath, Encoding.UTF8).ConfigureAwait(true);
        Assert.Equal(content, written);
    }

    [Fact]
    public async Task WriteFileWithEncodingAsync_CRLF_RestoresCRLF()
    {
        var filePath = "/test/write_crlf.txt";
        var content = "hello\nworld\n";

        var result = await _service.WriteFileWithEncodingAsync(filePath, content, Encoding.UTF8, "CRLF").ConfigureAwait(true);

        Assert.True(result.Success);
        var written = await Fs.ReadAllTextAsync(filePath, Encoding.UTF8).ConfigureAwait(true);
        Assert.Equal("hello\r\nworld\r\n", written);
    }

    [Fact]
    public async Task WriteFileWithEncodingAsync_NullEncoding_DefaultsToUTF8()
    {
        var filePath = "/test/write_default.txt";
        var content = "test content";

        var result = await _service.WriteFileWithEncodingAsync(filePath, content, encoding: null, lineEndings: "LF").ConfigureAwait(true);

        Assert.True(result.Success);
        var written = await Fs.ReadAllTextAsync(filePath).ConfigureAwait(true);
        Assert.Equal(content, written);
    }

    [Fact]
    public async Task Roundtrip_ReadWithMetadata_WriteWithEncoding_PreservesEncodingAndLineEndings()
    {
        var filePath = "/test/roundtrip.txt";
        // Write CRLF content with UTF-8
        await Fs.WriteAllTextAsync(filePath, "line1\r\nline2\r\n", Encoding.UTF8).ConfigureAwait(true);

        // Read with metadata
        var metadata = await _service.ReadFileWithMetadataAsync(filePath).ConfigureAwait(true);
        Assert.True(metadata.Success);
        Assert.Equal("CRLF", metadata.LineEndings);
        Assert.Equal("line1\nline2\n", metadata.Content); // Normalized to LF

        // Modify content (still LF internally)
        var modifiedContent = metadata.Content + "line3\n";

        // Write back with preserved encoding and line endings
        var writeResult = await _service.WriteFileWithEncodingAsync(filePath, modifiedContent, metadata.Encoding, metadata.LineEndings).ConfigureAwait(true);
        Assert.True(writeResult.Success);

        // Verify CRLF was restored
        var finalContent = await Fs.ReadAllTextAsync(filePath, Encoding.UTF8).ConfigureAwait(true);
        Assert.Equal("line1\r\nline2\r\nline3\r\n", finalContent);
    }
}
