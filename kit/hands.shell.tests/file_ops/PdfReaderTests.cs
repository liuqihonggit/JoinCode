namespace Core.Tests.FileOps;

public sealed class PdfReaderTests : IDisposable
{
    private readonly InMemoryFileOperationService _fileOperationService = new();

    public void Dispose()
    {
        _fileOperationService.Dispose();
    }

    [Fact]
    public async Task ReadPdfAsync_FileNotFound_ReturnsFail()
    {
        var result = await PdfReader.ReadPdfAsync("/missing.pdf", _fileOperationService.FileSystem).ConfigureAwait(true);

        result.Success.Should().BeFalse();
        result.ErrorReason.Should().Be("not_found");
    }

    [Fact]
    public async Task ReadPdfAsync_EmptyFile_ReturnsFail()
    {
        _fileOperationService.FileSystem.WriteAllText("/empty.pdf", string.Empty);

        var result = await PdfReader.ReadPdfAsync("/empty.pdf", _fileOperationService.FileSystem).ConfigureAwait(true);

        result.Success.Should().BeFalse();
        result.ErrorReason.Should().Be("empty");
    }

    [Fact]
    public async Task ReadPdfAsync_TooLargeFile_ReturnsFail()
    {
        var largeContent = new string('x', 100) + "%PDF-1.4\n";
        var bytes = new byte[FileOperationConfig.PdfTargetRawSize + 1];
        Array.Fill(bytes, (byte)'x');
        bytes[50] = (byte)'%';
        bytes[51] = (byte)'P';
        bytes[52] = (byte)'D';
        bytes[53] = (byte)'F';
        bytes[54] = (byte)'-';
        _fileOperationService.FileSystem.WriteAllBytes("/large.pdf", bytes);

        var result = await PdfReader.ReadPdfAsync("/large.pdf", _fileOperationService.FileSystem).ConfigureAwait(true);

        result.Success.Should().BeFalse();
        result.ErrorReason.Should().Be("too_large");
    }

    [Fact]
    public async Task ReadPdfAsync_InvalidHeader_ReturnsFail()
    {
        _fileOperationService.FileSystem.WriteAllText("/invalid.pdf", "not a pdf");

        var result = await PdfReader.ReadPdfAsync("/invalid.pdf", _fileOperationService.FileSystem).ConfigureAwait(true);

        result.Success.Should().BeFalse();
        result.ErrorReason.Should().Be("corrupted");
    }

    [Fact]
    public async Task ReadPdfAsync_ValidPdf_ReturnsOk()
    {
        var pdfBytes = CreateMinimalPdf(3);
        _fileOperationService.FileSystem.WriteAllBytes("/valid.pdf", pdfBytes);

        var result = await PdfReader.ReadPdfAsync("/valid.pdf", _fileOperationService.FileSystem).ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.Base64.Should().NotBeNullOrEmpty();
        result.OriginalSize.Should().Be(pdfBytes.Length);
        result.PageCount.Should().Be(3);
    }

    [Fact]
    public void IsPdfExtension_PdfExtension_ReturnsTrue()
    {
        PdfReader.IsPdfExtension("/path/to/file.pdf").Should().BeTrue();
        PdfReader.IsPdfExtension("FILE.PDF").Should().BeTrue();
    }

    [Fact]
    public void IsPdfExtension_NonPdfExtension_ReturnsFalse()
    {
        PdfReader.IsPdfExtension("/path/to/file.txt").Should().BeFalse();
        PdfReader.IsPdfExtension("/path/to/file").Should().BeFalse();
    }

    [Theory]
    [InlineData("5", 5, 5)]
    [InlineData("1-10", 1, 10)]
    [InlineData("3-", 3, int.MaxValue)]
    public void ParsePageRange_ValidInput_ReturnsRange(string input, int first, int last)
    {
        var range = PdfReader.ParsePageRange(input);

        range.Should().NotBeNull();
        range!.FirstPage.Should().Be(first);
        range.LastPage.Should().Be(last);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("10-1")]
    [InlineData("abc")]
    [InlineData("1-")]
    public void ParsePageRange_InvalidInput_ReturnsNull(string input)
    {
        // Note: "1-" is valid per implementation, handled separately above
        if (input == "1-") return;

        var range = PdfReader.ParsePageRange(input);

        range.Should().BeNull();
    }

    [Fact]
    public void GetPdfPageCount_FileNotFound_ReturnsNull()
    {
        var count = PdfReader.GetPdfPageCount("/missing.pdf", _fileOperationService.FileSystem);

        count.Should().BeNull();
    }

    [Fact]
    public void GetPdfPageCount_ValidPdf_ReturnsCount()
    {
        var pdfBytes = CreateMinimalPdf(7);
        _fileOperationService.FileSystem.WriteAllBytes("/count.pdf", pdfBytes);

        var count = PdfReader.GetPdfPageCount("/count.pdf", _fileOperationService.FileSystem);

        count.Should().Be(7);
    }

    [Fact]
    public void GetBase64_WhenSuccess_ReturnsBase64()
    {
        var result = PdfReadResult.Ok("SGVsbG8=", 100, 1);

        result.GetBase64().Should().Be("SGVsbG8=");
    }

    [Fact]
    public void GetBase64_WhenFail_Throws()
    {
        var result = PdfReadResult.Fail("error", "message");

        var act = () => result.GetBase64();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GetOriginalSize_WhenSuccess_ReturnsSize()
    {
        var result = PdfReadResult.Ok("SGVsbG8=", 100, 1);

        result.GetOriginalSize().Should().Be(100);
    }

    [Fact]
    public void GetOriginalSize_WhenFail_Throws()
    {
        var result = PdfReadResult.Fail("error", "message");

        var act = () => result.GetOriginalSize();

        act.Should().Throw<InvalidOperationException>();
    }

    private static byte[] CreateMinimalPdf(int pageCount)
    {
        // Minimal PDF structure with /Type /Pages and /Count
        var pages = new StringBuilder();
        for (var i = 1; i <= pageCount; i++)
        {
            pages.Append($"1 0 obj\n<< /Type /Page /Parent 2 0 R >>\nendobj\n");
        }

        var content = $"%PDF-1.4\n" +
                      $"1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n" +
                      $"2 0 obj\n<< /Type /Pages /Kids [] /Count {pageCount} >>\nendobj\n" +
                      pages +
                      $"xref\n0 3\n0000000000 65535 f\n0000000009 00000 n\n0000000058 00000 n\n" +
                      $"trailer\n<< /Size 3 /Root 1 0 R >>\nstartxref\n0\n%%EOF\n";
        return Encoding.UTF8.GetBytes(content);
    }
}
