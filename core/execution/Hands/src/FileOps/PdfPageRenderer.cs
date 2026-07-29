using Docnet.Core;
using Docnet.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace Infrastructure.IO.Services.FileOps;

/// <summary>
/// PDF 页面渲染结果
/// </summary>
public sealed class PdfPageImage
{
    /// <summary>页码（1-indexed）</summary>
    public required int PageNumber { get; init; }

    /// <summary>JPEG 编码的图像数据</summary>
    public required byte[] JpegBytes { get; init; }

    /// <summary>图像宽度</summary>
    public required int Width { get; init; }

    /// <summary>图像高度</summary>
    public required int Height { get; init; }
}

/// <summary>
/// PDF 页面提取结果
/// 对齐 TS: pdf.ts ExtractPDFResult
/// </summary>
public sealed class PdfExtractResult
{
    /// <summary>是否成功</summary>
    public required bool Success { get; init; }

    /// <summary>提取的页面图像（成功时）</summary>
    public IReadOnlyList<PdfPageImage>? Pages { get; init; }

    /// <summary>PDF 总页数</summary>
    public int? TotalPageCount { get; init; }

    /// <summary>原始文件大小</summary>
    public long? OriginalSize { get; init; }

    /// <summary>错误原因（失败时）</summary>
    public string? ErrorReason { get; init; }

    /// <summary>错误消息（失败时）</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>获取 Pages，操作失败时抛出异常</summary>
    public IReadOnlyList<PdfPageImage> GetPages() =>
        Pages ?? throw new InvalidOperationException("Pages is not available. Check Success before calling this method.");

    public static PdfExtractResult Ok(IReadOnlyList<PdfPageImage> pages, int? totalCount, long originalSize) =>
        new() { Success = true, Pages = pages, TotalPageCount = totalCount, OriginalSize = originalSize };

    public static PdfExtractResult Fail(string reason, string message) =>
        new() { Success = false, ErrorReason = reason, ErrorMessage = message };
}

/// <summary>
/// PDF 页面渲染器。
/// 使用 Docnet.Core（PDFium P/Invoke）将 PDF 页面渲染为 JPEG 图像。
/// 对齐 TS: pdf.ts extractPDFPages — TS 使用 pdftoppm，C# 使用 PDFium
/// </summary>
public static class PdfPageRenderer
{
    /// <summary>
    /// 渲染目标宽度，对齐 TS: pdftoppm -r 100
    /// US Letter (8.5") 在 100 DPI 下约 850 像素宽
    /// </summary>
    private const int RenderWidth = 850;

    /// <summary>
    /// 渲染目标高度，对齐 TS: pdftoppm -r 100
    /// US Letter (11") 在 100 DPI 下约 1100 像素高
    /// </summary>
    private const int RenderHeight = 1100;

    /// <summary>
    /// JPEG 编码质量，对齐 TS: sharp jpeg quality 80
    /// </summary>
    private const int JpegQuality = 80;

    /// <summary>
    /// 提取 PDF 页面为 JPEG 图像。
    /// 对齐 TS: extractPDFPages — 渲染指定页码范围的 PDF 页面为 JPEG
    /// </summary>
    /// <param name="filePath">PDF 文件路径</param>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="firstPage">起始页（1-indexed），null 表示从第1页</param>
    /// <param name="lastPage">结束页（1-indexed），null 表示到最后一页</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static Task<PdfExtractResult> ExtractPagesAsync(
        string filePath, IFileSystem fs, int? firstPage = null, int? lastPage = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ExtractPagesCore(filePath, fs, firstPage, lastPage), cancellationToken);
    }

    /// <summary>
    /// 检查 PDF 渲染功能是否可用（PDFium 原生库是否加载成功）
    /// </summary>
    public static bool IsAvailable()
    {
        try
        {
            _ = DocLib.Instance;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static PdfExtractResult ExtractPagesCore(string filePath, IFileSystem fs, int? firstPage, int? lastPage)
    {
        try
        {
            if (!fs.FileExists(filePath))
            {
                return PdfExtractResult.Fail("not_found", $"PDF file not found: {filePath}");
            }

            long originalSize;
            using (var sizeStream = fs.OpenRead(filePath))
            {
                originalSize = sizeStream.Length;
            }

            if (originalSize == 0)
            {
                return PdfExtractResult.Fail("empty", $"PDF file is empty: {filePath}");
            }

            // 对齐 TS: PDF_MAX_EXTRACT_SIZE = 100MB
            if (originalSize > 100 * 1024 * 1024)
            {
                return PdfExtractResult.Fail("too_large",
                    $"PDF file exceeds maximum extract size of 100MB.");
            }

            using var docLib = DocLib.Instance;

            // 对齐 TS: pdftoppm -r 100 — DPI=100 决定输出像素尺寸
            // PageDimensions 控制渲染分辨率（像素宽高，非 DPI）
            using var docReader = docLib.GetDocReader(filePath, new PageDimensions(RenderWidth, RenderHeight));

            var totalPages = docReader.GetPageCount();

            // 确定要渲染的页码范围（1-indexed → 0-indexed）
            var startPage = Math.Max((firstPage ?? 1) - 1, 0);
            var endPage = Math.Min((lastPage ?? totalPages) - 1, totalPages - 1);

            if (startPage >= totalPages)
            {
                return PdfExtractResult.Fail("out_of_range",
                    $"Requested page {firstPage} exceeds total pages ({totalPages}).");
            }

            var pages = new List<PdfPageImage>();

            for (var i = startPage; i <= endPage; i++)
            {
                using var pageReader = docReader.GetPageReader(i);

                var width = pageReader.GetPageWidth();
                var height = pageReader.GetPageHeight();
                var rawBytes = pageReader.GetImage(); // BGRA 格式

                // 将 BGRA 原始像素转换为 JPEG
                var jpegBytes = BgraToJpeg(rawBytes, width, height);

                pages.Add(new PdfPageImage
                {
                    PageNumber = i + 1, // 1-indexed
                    JpegBytes = jpegBytes,
                    Width = width,
                    Height = height,
                });
            }

            return PdfExtractResult.Ok(pages, totalPages, originalSize);
        }
        catch (Exception ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            return PdfExtractResult.Fail("password_protected",
                "This PDF is password protected and cannot be read.");
        }
        catch (Exception ex) when (ex.Message.Contains("corrupt", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("damaged", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("invalid", StringComparison.OrdinalIgnoreCase))
        {
            return PdfExtractResult.Fail("corrupted",
                $"PDF file appears to be corrupted: {ex.Message}");
        }
        catch (Exception ex)
        {
            return PdfExtractResult.Fail("unknown", ex.Message);
        }
    }

    /// <summary>
    /// 将 BGRA 原始像素数据编码为 JPEG。
    /// Docnet.Core 返回 BGRA 格式（4字节/像素：Blue, Green, Red, Alpha）
    /// ImageSharp 的 Bgra32 格式正好匹配
    /// </summary>
    private static byte[] BgraToJpeg(byte[] bgraData, int width, int height)
    {
        using var image = Image.LoadPixelData<Bgra32>(bgraData, width, height);

        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms, new JpegEncoder { Quality = JpegQuality });
        return ms.ToArray();
    }
}
