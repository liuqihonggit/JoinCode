namespace Hands.Tests.Web;

/// <summary>
/// BinaryContentTypeDetector 单元测试 — 对齐TS版 mcpOutputStorage.ts isBinaryContentType
/// </summary>
public class BinaryContentTypeDetectorTests
{
    [Theory]
    [InlineData("application/pdf", true)]
    [InlineData("image/png", true)]
    [InlineData("image/jpeg", true)]
    [InlineData("image/gif", true)]
    [InlineData("image/webp", true)]
    [InlineData("audio/mpeg", true)]
    [InlineData("video/mp4", true)]
    [InlineData("application/zip", true)]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", true)]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", true)]
    [InlineData("application/octet-stream", true)]
    public void IsBinaryContentType_BinaryTypes_ReturnsTrue(string contentType, bool expected)
    {
        BinaryContentTypeDetector.IsBinaryContentType(contentType).Should().Be(expected);
    }

    [Theory]
    [InlineData("text/html", false)]
    [InlineData("text/plain", false)]
    [InlineData("text/csv", false)]
    [InlineData("text/markdown", false)]
    [InlineData("application/json", false)]
    [InlineData("application/xml", false)]
    [InlineData("application/javascript", false)]
    [InlineData("application/x-www-form-urlencoded", false)]
    [InlineData("application/vnd.api+json", false)]
    [InlineData("application/atom+xml", false)]
    public void IsBinaryContentType_TextTypes_ReturnsFalse(string contentType, bool expected)
    {
        BinaryContentTypeDetector.IsBinaryContentType(contentType).Should().Be(expected);
    }

    [Fact]
    public void IsBinaryContentType_Null_ReturnsFalse()
    {
        BinaryContentTypeDetector.IsBinaryContentType(null).Should().BeFalse();
    }

    [Fact]
    public void IsBinaryContentType_EmptyString_ReturnsFalse()
    {
        BinaryContentTypeDetector.IsBinaryContentType("").Should().BeFalse();
    }

    [Fact]
    public void IsBinaryContentType_WithCharset_IgnoresParameters()
    {
        // text/html; charset=utf-8 → 非二进制
        BinaryContentTypeDetector.IsBinaryContentType("text/html; charset=utf-8").Should().BeFalse();
        // application/pdf; charset=binary → 二进制
        BinaryContentTypeDetector.IsBinaryContentType("application/pdf; charset=binary").Should().BeTrue();
    }

    [Fact]
    public void IsBinaryContentType_CaseInsensitive()
    {
        BinaryContentTypeDetector.IsBinaryContentType("Application/PDF").Should().BeTrue();
        BinaryContentTypeDetector.IsBinaryContentType("TEXT/HTML").Should().BeFalse();
    }
}

/// <summary>
/// MimeTypeExtensionMapper 单元测试 — 对齐TS版 mcpOutputStorage.ts extensionForMimeType
/// </summary>
public class MimeTypeExtensionMapperTests
{
    [Theory]
    [InlineData("application/pdf", "pdf")]
    [InlineData("application/json", "json")]
    [InlineData("text/csv", "csv")]
    [InlineData("text/plain", "txt")]
    [InlineData("text/html", "html")]
    [InlineData("text/markdown", "md")]
    [InlineData("application/zip", "zip")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "docx")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsx")]
    [InlineData("application/vnd.openxmlformats-officedocument.presentationml.presentation", "pptx")]
    [InlineData("application/msword", "doc")]
    [InlineData("application/vnd.ms-excel", "xls")]
    [InlineData("audio/mpeg", "mp3")]
    [InlineData("audio/wav", "wav")]
    [InlineData("audio/ogg", "ogg")]
    [InlineData("video/mp4", "mp4")]
    [InlineData("video/webm", "webm")]
    [InlineData("image/png", "png")]
    [InlineData("image/jpeg", "jpg")]
    [InlineData("image/gif", "gif")]
    [InlineData("image/webp", "webp")]
    [InlineData("image/svg+xml", "svg")]
    public void GetExtension_KnownMimeTypes_ReturnsCorrectExtension(string mimeType, string expected)
    {
        MimeTypeExtensionMapper.GetExtension(mimeType).Should().Be(expected);
    }

    [Fact]
    public void GetExtension_Null_ReturnsBin()
    {
        MimeTypeExtensionMapper.GetExtension(null).Should().Be("bin");
    }

    [Fact]
    public void GetExtension_EmptyString_ReturnsBin()
    {
        MimeTypeExtensionMapper.GetExtension("").Should().Be("bin");
    }

    [Fact]
    public void GetExtension_UnknownType_ReturnsBin()
    {
        MimeTypeExtensionMapper.GetExtension("application/x-unknown").Should().Be("bin");
    }

    [Fact]
    public void GetExtension_WithCharset_IgnoresParameters()
    {
        MimeTypeExtensionMapper.GetExtension("text/html; charset=utf-8").Should().Be("html");
    }
}

/// <summary>
/// BinaryContentStorage 单元测试 — 对齐TS版 mcpOutputStorage.ts persistBinaryContent
/// </summary>
public class BinaryContentStorageTests
{
    private readonly IFileSystem _fs = TestFileSystem.Current;

    [Fact]
    public async Task PersistAsync_PdfContent_SavesToCorrectPath()
    {
        var storage = new BinaryContentStorage(_fs);
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // %PDF header
        var persistId = "webfetch-1234567890-abc123";

        var result = await storage.PersistAsync(bytes, "application/pdf", persistId).ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.FilePath.Should().EndWith("webfetch-1234567890-abc123.pdf");
        result.Size.Should().Be(4);
        result.Extension.Should().Be("pdf");
        _fs.FileExists(result.FilePath!).Should().BeTrue();

        var savedBytes = await _fs.ReadAllBytesAsync(result.FilePath!).ConfigureAwait(true);
        savedBytes.Should().Equal(bytes);
    }

    [Fact]
    public async Task PersistAsync_ImageContent_SavesWithCorrectExtension()
    {
        var storage = new BinaryContentStorage(_fs);
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header
        var persistId = "webfetch-test-png";

        var result = await storage.PersistAsync(bytes, "image/png", persistId).ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.Extension.Should().Be("png");
        result.FilePath.Should().EndWith(".png");
    }

    [Fact]
    public async Task PersistAsync_UnknownMimeType_SavesAsBin()
    {
        var storage = new BinaryContentStorage(_fs);
        var bytes = new byte[] { 0x00, 0x01, 0x02 };
        var persistId = "webfetch-unknown";

        var result = await storage.PersistAsync(bytes, "application/x-unknown", persistId).ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.Extension.Should().Be("bin");
        result.FilePath.Should().EndWith(".bin");
    }

    [Fact]
    public async Task PersistAsync_NullMimeType_SavesAsBin()
    {
        var storage = new BinaryContentStorage(_fs);
        var bytes = new byte[] { 0x00 };
        var persistId = "webfetch-null";

        var result = await storage.PersistAsync(bytes, null, persistId).ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.Extension.Should().Be("bin");
    }

    [Fact]
    public void GeneratePersistId_StartsWithWebfetch()
    {
        var storage = new BinaryContentStorage(new IO.FileSystem.PhysicalFileSystem(), null);
        var id = storage.GeneratePersistId();
        id.Should().StartWith("webfetch-");
        id.Length.Should().BeGreaterThan("webfetch-".Length);
    }

    [Fact]
    public void GeneratePersistId_UniqueIds()
    {
        var storage = new BinaryContentStorage(new IO.FileSystem.PhysicalFileSystem(), null);
        var id1 = storage.GeneratePersistId();
        var id2 = storage.GeneratePersistId();
        id1.Should().NotBe(id2);
    }

    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(512, "512 bytes")]
    [InlineData(1024, "1KB")]
    [InlineData(1536, "1.5KB")]
    [InlineData(1048576, "1MB")]
    [InlineData(1572864, "1.5MB")]
    public void FormatFileSize_CorrectFormatting(int bytes, string expected)
    {
        ContentReplacementConstants.FormatFileSize(bytes).Should().Be(expected);
    }
}
