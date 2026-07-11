using Infrastructure.IO.Services.FileOps;
using IO.FileSystem;

namespace Infrastructure.Tests.Services;

public sealed class PdfPageRendererTests
{
    private static readonly IFileSystem PhysicalFs = new PhysicalFileSystem();

    private static string GetTestPdfPath()
    {
        // 使用预生成的 3 页测试 PDF（QuestPDF 生成，PDFium 可正确解析）
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "test-3page.pdf");
        return PhysicalFs.FileExists(path) ? path : throw new FileNotFoundException($"Test PDF not found: {path}");
    }

    [Fact]
    public void IsAvailable_ReturnsTrue()
    {
        PdfPageRenderer.IsAvailable().Should().BeTrue();
    }

    [Fact]
    public async Task ExtractPagesAsync_NonExistentFile_ReturnsFail()
    {
        var result = await PdfPageRenderer.ExtractPagesAsync(
            Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid()}.pdf"),
            PhysicalFs).ConfigureAwait(true);

        result.Success.Should().BeFalse();
        result.ErrorReason.Should().Be("not_found");
    }

    [Fact]
    public async Task ExtractPagesAsync_EmptyFile_ReturnsFail()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"empty_{Guid.NewGuid()}.pdf");
        try
        {
            await PhysicalFs.WriteAllTextAsync(tempFile, "").ConfigureAwait(true);
            var result = await PdfPageRenderer.ExtractPagesAsync(tempFile, PhysicalFs).ConfigureAwait(true);
            result.Success.Should().BeFalse();
            result.ErrorReason.Should().Be("empty");
        }
        finally
        {
            if (PhysicalFs.FileExists(tempFile)) PhysicalFs.DeleteFile(tempFile);
        }
    }

    [Fact]
    public async Task ExtractPagesAsync_ValidPdf_ExtractsAllPages()
    {
        var result = await PdfPageRenderer.ExtractPagesAsync(
            GetTestPdfPath(), PhysicalFs).ConfigureAwait(true);

        result.Success.Should().BeTrue($"extraction should succeed, but got error: {result.ErrorReason} - {result.ErrorMessage}");
        result.Pages.Should().NotBeNull();
        result.Pages!.Count.Should().Be(3);
        result.TotalPageCount.Should().Be(3);
        result.OriginalSize.Should().BeGreaterThan(0);

        // 每页应有有效的 JPEG 数据
        foreach (var page in result.Pages)
        {
            page.JpegBytes.Length.Should().BeGreaterThan(0);
            page.Width.Should().BeGreaterThan(0);
            page.Height.Should().BeGreaterThan(0);
            // JPEG magic bytes: FF D8 FF
            page.JpegBytes[0].Should().Be(0xFF);
            page.JpegBytes[1].Should().Be(0xD8);
            page.JpegBytes[2].Should().Be(0xFF);
        }
    }

    [Fact]
    public async Task ExtractPagesAsync_WithPageRange_ExtractsOnlySpecifiedPages()
    {
        var result = await PdfPageRenderer.ExtractPagesAsync(
            GetTestPdfPath(), PhysicalFs, firstPage: 2, lastPage: 2).ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.Pages.Should().NotBeNull();
        result.Pages!.Count.Should().Be(1);
        result.Pages[0].PageNumber.Should().Be(2);
        result.TotalPageCount.Should().Be(3);
    }

    [Fact]
    public async Task ExtractPagesAsync_PageOutOfRange_ReturnsFail()
    {
        var result = await PdfPageRenderer.ExtractPagesAsync(
            GetTestPdfPath(), PhysicalFs, firstPage: 99).ConfigureAwait(true);

        result.Success.Should().BeFalse();
        result.ErrorReason.Should().Be("out_of_range");
    }

    [Fact]
    public async Task ExtractPagesAsync_PageNumbers_AreOneIndexed()
    {
        var result = await PdfPageRenderer.ExtractPagesAsync(
            GetTestPdfPath(), PhysicalFs).ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.Pages![0].PageNumber.Should().Be(1);
        result.Pages![1].PageNumber.Should().Be(2);
        result.Pages![2].PageNumber.Should().Be(3);
    }

    [Fact]
    public async Task ExtractPagesAsync_WithFirstPageOnly_ExtractsOnePage()
    {
        var result = await PdfPageRenderer.ExtractPagesAsync(
            GetTestPdfPath(), PhysicalFs, firstPage: 1, lastPage: 1).ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.Pages!.Count.Should().Be(1);
        result.Pages![0].PageNumber.Should().Be(1);
    }
}
