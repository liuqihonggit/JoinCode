using Infrastructure.IO.Services.FileOps;
using IO.FileSystem;

namespace Infrastructure.Tests.Services;

public sealed class PdfReaderTests
{
    private static readonly IFileSystem Fs = TestFileSystem.Current;

    [Fact]
    public void IsPdfExtension_PdfFile_ReturnsTrue()
    {
        PdfReader.IsPdfExtension("document.pdf").Should().BeTrue();
    }

    [Fact]
    public void IsPdfExtension_PdfUpperCase_ReturnsTrue()
    {
        PdfReader.IsPdfExtension("DOCUMENT.PDF").Should().BeTrue();
    }

    [Fact]
    public void IsPdfExtension_NonPdfFile_ReturnsFalse()
    {
        PdfReader.IsPdfExtension("document.txt").Should().BeFalse();
    }

    [Fact]
    public void IsPdfExtension_NoExtension_ReturnsFalse()
    {
        PdfReader.IsPdfExtension("document").Should().BeFalse();
    }

    [Fact]
    public async Task ReadPdfAsync_NonExistentFile_ReturnsFail()
    {
        var result = await PdfReader.ReadPdfAsync(
            $"/test/nonexistent_{Guid.NewGuid()}.pdf", Fs).ConfigureAwait(true);
        result.Success.Should().BeFalse();
        result.ErrorReason.Should().Be("not_found");
    }

    [Fact]
    public async Task ReadPdfAsync_EmptyFile_ReturnsFail()
    {
        var tempFile = $"/test/empty_{Guid.NewGuid():N}.pdf";
        await Fs.WriteAllTextAsync(tempFile, "").ConfigureAwait(true);
        var result = await PdfReader.ReadPdfAsync(tempFile, Fs).ConfigureAwait(true);
        result.Success.Should().BeFalse();
        result.ErrorReason.Should().Be("empty");
    }

    [Fact]
    public async Task ReadPdfAsync_InvalidPdfHeader_ReturnsFail()
    {
        var tempFile = $"/test/invalid_{Guid.NewGuid():N}.pdf";
        await Fs.WriteAllBytesAsync(tempFile, "Not a PDF file content"u8.ToArray()).ConfigureAwait(true);
        var result = await PdfReader.ReadPdfAsync(tempFile, Fs).ConfigureAwait(true);
        result.Success.Should().BeFalse();
        result.ErrorReason.Should().Be("corrupted");
    }

    [Fact]
    public async Task ReadPdfAsync_ValidPdf_ReturnsSuccess()
    {
        var tempFile = $"/test/valid_{Guid.NewGuid():N}.pdf";
        // 创建最小的有效 PDF（%PDF- 头 + 一些内容）
        var pdfContent = "%PDF-1.4\n1 0 obj\n<<\n/Type /Catalog\n>>\nendobj\n%%EOF"u8;
        await Fs.WriteAllBytesAsync(tempFile, pdfContent.ToArray()).ConfigureAwait(true);

        var result = await PdfReader.ReadPdfAsync(tempFile, Fs).ConfigureAwait(true);
        result.Success.Should().BeTrue();
        result.Base64.Should().NotBeNullOrEmpty();
        result.OriginalSize.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ReadPdfAsync_ValidPdf_Base64IsDecodable()
    {
        var tempFile = $"/test/b64_{Guid.NewGuid():N}.pdf";
        var pdfContent = "%PDF-1.4\n1 0 obj\n<<\n/Type /Catalog\n>>\nendobj\n%%EOF"u8;
        await Fs.WriteAllBytesAsync(tempFile, pdfContent.ToArray()).ConfigureAwait(true);

        var result = await PdfReader.ReadPdfAsync(tempFile, Fs).ConfigureAwait(true);
        result.Success.Should().BeTrue();

        // 验证 base64 可以正确解码回原始字节
        var decoded = Convert.FromBase64String(result.Base64!);
        decoded.Should().StartWith("%PDF-"u8.ToArray());
    }

    [Theory]
    [InlineData("5", 5, 5)]
    [InlineData("1-10", 1, 10)]
    [InlineData("3-", 3, int.MaxValue)]
    public void ParsePageRange_ValidInput_ReturnsCorrectRange(string input, int expectedFirst, int expectedLast)
    {
        var result = PdfReader.ParsePageRange(input);
        result.Should().NotBeNull();
        result!.FirstPage.Should().Be(expectedFirst);
        result.LastPage.Should().Be(expectedLast);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("10-5")]
    [InlineData("abc")]
    [InlineData("1-abc")]
    public void ParsePageRange_InvalidInput_ReturnsNull(string input)
    {
        var result = PdfReader.ParsePageRange(input);
        result.Should().BeNull();
    }

    [Fact]
    public void GetPdfPageCount_NonExistentFile_ReturnsNull()
    {
        PdfReader.GetPdfPageCount($"/test/nonexistent_{Guid.NewGuid()}.pdf", Fs)
            .Should().BeNull();
    }

    [Fact]
    public void GetPdfPageCount_PdfWithPagesDict_ReturnsCorrectCount()
    {
        var tempFile = $"/test/pages_{Guid.NewGuid():N}.pdf";
        // 创建包含 /Type /Pages + /Count 42 的最小 PDF
        var pdf = "%PDF-1.4\n1 0 obj\n<< /Type /Pages /Count 42 /Kids [] >>\nendobj\n%%EOF"u8;
        Fs.WriteAllBytes(tempFile, pdf.ToArray());

        PdfReader.GetPdfPageCount(tempFile, Fs).Should().Be(42);
    }

    [Fact]
    public void GetPdfPageCount_PdfWithSpacedTypePages_ReturnsCorrectCount()
    {
        var tempFile = $"/test/spaced_{Guid.NewGuid():N}.pdf";
        // /Type /Pages 带空格
        var pdf = "%PDF-1.4\n1 0 obj\n<< /Type /Pages /Count 7 /Kids [] >>\nendobj\n%%EOF"u8;
        Fs.WriteAllBytes(tempFile, pdf.ToArray());

        PdfReader.GetPdfPageCount(tempFile, Fs).Should().Be(7);
    }

    [Fact]
    public void GetPdfPageCount_PdfWithoutPagesDict_ReturnsNull()
    {
        var tempFile = $"/test/nopages_{Guid.NewGuid():N}.pdf";
        // 只有 /Type /Catalog，没有 /Pages
        var pdf = "%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\n%%EOF"u8;
        Fs.WriteAllBytes(tempFile, pdf.ToArray());

        PdfReader.GetPdfPageCount(tempFile, Fs).Should().BeNull();
    }

    [Fact]
    public async Task ReadPdfAsync_ValidPdfWithPages_ReturnsPageCount()
    {
        var tempFile = $"/test/pc_{Guid.NewGuid():N}.pdf";
        var pdf = "%PDF-1.4\n1 0 obj\n<< /Type /Pages /Count 15 /Kids [] >>\nendobj\n%%EOF"u8;
        await Fs.WriteAllBytesAsync(tempFile, pdf.ToArray()).ConfigureAwait(true);

        var result = await PdfReader.ReadPdfAsync(tempFile, Fs).ConfigureAwait(true);
        result.Success.Should().BeTrue();
        result.PageCount.Should().Be(15);
    }
}
