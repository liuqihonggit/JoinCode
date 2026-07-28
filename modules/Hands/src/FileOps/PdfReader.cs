
namespace Infrastructure.IO.Services.FileOps;

/// <summary>
/// PDF 读取结果
/// 对齐 TS: pdf.ts PDFResult
/// </summary>
public sealed record PdfReadResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// base64 编码的 PDF 数据（成功时）
    /// </summary>
    public string? Base64 { get; init; }

    /// <summary>
    /// 原始文件大小（字节）
    /// </summary>
    public long? OriginalSize { get; init; }

    /// <summary>
    /// PDF 页数（成功时可能为 null 如果无法检测）
    /// 对齐 TS: getPDFPageCount
    /// </summary>
    public int? PageCount { get; init; }

    /// <summary>
    /// 错误原因（失败时）
    /// </summary>
    public string? ErrorReason { get; init; }

    /// <summary>
    /// 错误消息（失败时）
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>获取 Base64，操作失败时抛出异常</summary>
    public string GetBase64() =>
        Base64 ?? throw new InvalidOperationException("Base64 is not available. Check Success before calling this method.");

    /// <summary>获取 OriginalSize，操作失败时抛出异常</summary>
    public long GetOriginalSize() =>
        OriginalSize ?? throw new InvalidOperationException("OriginalSize is not available. Check Success before calling this method.");

    public static PdfReadResult Ok(string base64, long originalSize, int? pageCount = null) =>
        new() { Success = true, Base64 = base64, OriginalSize = originalSize, PageCount = pageCount };

    public static PdfReadResult Fail(string reason, string message) =>
        new() { Success = false, ErrorReason = reason, ErrorMessage = message };
}

/// <summary>
/// PDF 页面范围解析结果
/// 对齐 TS: pdfUtils.ts parsePDFPageRange
/// </summary>
public sealed record PdfPageRange
{
    /// <summary>
    /// 起始页（1-indexed）
    /// </summary>
    public required int FirstPage { get; init; }

    /// <summary>
    /// 结束页（1-indexed，int.MaxValue 表示到末尾）
    /// </summary>
    public required int LastPage { get; init; }
}

/// <summary>
/// PDF 读取器
/// 对齐 TS: pdf.ts readPDF + pdfUtils.ts
/// </summary>
public static class PdfReader
{
    /// <summary>
    /// PDF magic bytes: %PDF-
    /// </summary>
    private static ReadOnlySpan<byte> PdfMagic => "%PDF-"u8;

    /// <summary>
    /// 判断文件扩展名是否为 PDF
    /// 对齐 TS: isPDFExtension
    /// </summary>
    public static bool IsPdfExtension(string filePath)
    {
        var ext = Path.GetExtension(filePath.AsSpan());
        return ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 读取 PDF 文件为 base64
    /// 对齐 TS: readPDF — 检查大小限制、验证 %PDF- 头、返回 base64
    /// </summary>
    public static async Task<PdfReadResult> ReadPdfAsync(string filePath, IFileSystem fs, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!fs.FileExists(filePath))
            {
                return PdfReadResult.Fail("not_found", $"PDF file not found: {filePath}");
            }

            long originalSize;
            using (var sizeStream = fs.OpenRead(filePath))
            {
                originalSize = sizeStream.Length;
            }

            // 对齐 TS: 检查空文件
            if (originalSize == 0)
            {
                return PdfReadResult.Fail("empty", $"PDF file is empty: {filePath}");
            }

            // 对齐 TS: 检查大小限制
            if (originalSize > FileOperationConfig.PdfTargetRawSize)
            {
                return PdfReadResult.Fail("too_large",
                    $"PDF file exceeds maximum allowed size of {JoinCode.Abstractions.LLM.Chat.ContentReplacementConstants.FormatFileSize(FileOperationConfig.PdfTargetRawSize)}.");
            }

            // 对齐 TS: 验证 %PDF- 头（magic bytes）
            await using var stream = fs.CreateStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var header = new byte[5];
            var bytesRead = await stream.ReadAsync(header, cancellationToken).ConfigureAwait(false);

            if (bytesRead < 5 || !header.AsSpan().SequenceEqual(PdfMagic))
            {
                return PdfReadResult.Fail("corrupted",
                    $"File is not a valid PDF (missing %PDF- header): {filePath}");
            }

            // 读取完整文件并转换为 base64
            var fileBytes = await fs.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
            var base64 = Convert.ToBase64String(fileBytes);

            // 对齐 TS: getPDFPageCount — 解析 /Pages 字典中的 /Count 值
            var pageCount = DetectPageCount(fileBytes);

            return PdfReadResult.Ok(base64, originalSize, pageCount);
        }
        catch (Exception ex)
        {
            return PdfReadResult.Fail("unknown", ex.Message);
        }
    }

    /// <summary>
    /// 解析 PDF 页面范围字符串
    /// 对齐 TS: parsePDFPageRange
    /// 支持格式: "5" → 第5页, "1-10" → 第1到10页, "3-" → 第3页到末尾
    /// </summary>
    public static PdfPageRange? ParsePageRange(string pages)
    {
        var trimmed = pages.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        // "N-" 开放式范围
        if (trimmed.EndsWith('-'))
        {
            var firstStr = trimmed[..^1];
            if (!int.TryParse(firstStr, out var first) || first < 1)
                return null;
            return new PdfPageRange { FirstPage = first, LastPage = int.MaxValue };
        }

        var dashIndex = trimmed.IndexOf('-');
        if (dashIndex < 0)
        {
            // 单页: "5"
            if (!int.TryParse(trimmed, out var page) || page < 1)
                return null;
            return new PdfPageRange { FirstPage = page, LastPage = page };
        }

        // 范围: "1-10"
        var firstPart = trimmed[..dashIndex];
        var lastPart = trimmed[(dashIndex + 1)..];
        if (!int.TryParse(firstPart, out var firstPage) || !int.TryParse(lastPart, out var lastPage))
            return null;
        if (firstPage < 1 || lastPage < 1 || lastPage < firstPage)
            return null;
        return new PdfPageRange { FirstPage = firstPage, LastPage = lastPage };
    }

    /// <summary>
    /// 获取 PDF 文件页数（不读取完整文件内容为 base64）
    /// 对齐 TS: getPDFPageCount — TS 使用 pdfinfo 命令，C# 直接解析 PDF 结构
    /// </summary>
    public static int? GetPdfPageCount(string filePath, IFileSystem fs)
    {
        try
        {
            if (!fs.FileExists(filePath))
                return null;

            var bytes = fs.ReadAllBytes(filePath);
            return DetectPageCount(bytes);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 从 PDF 字节中检测页数
    /// 搜索 /Type /Pages 附近的 /Count N 模式
    /// PDF 规范: /Pages 字典的 /Count 值为文档总页数
    /// </summary>
    private static int? DetectPageCount(byte[] bytes)
    {
        // 搜索 /Type/Pages 或 /Type /Pages
        var typePagesIdx = IndexOfPattern(bytes, "/Type/Pages"u8);
        if (typePagesIdx < 0)
            typePagesIdx = IndexOfPattern(bytes, "/Type /Pages"u8);

        if (typePagesIdx < 0)
            return null;

        // 在 /Type /Pages 后 500 字节内搜索 /Count N
        var searchEnd = Math.Min(typePagesIdx + 500, bytes.Length);
        var countIdx = IndexOfPattern(bytes, "/Count"u8, typePagesIdx, searchEnd);

        if (countIdx < 0)
            return null;

        // 跳过 "/Count" 和空白字符
        var k = countIdx + 6; // "/Count" 长度
        while (k < bytes.Length && (bytes[k] == ' ' || bytes[k] == '\t' || bytes[k] == '\r' || bytes[k] == '\n'))
            k++;

        // 解析数字
        if (k < bytes.Length && bytes[k] >= '0' && bytes[k] <= '9')
        {
            var count = 0;
            while (k < bytes.Length && bytes[k] >= '0' && bytes[k] <= '9')
            {
                count = count * 10 + (bytes[k] - '0');
                k++;
            }
            return count;
        }

        return null;
    }

    /// <summary>
    /// 在字节数组中搜索指定模式
    /// </summary>
    private static int IndexOfPattern(byte[] data, ReadOnlySpan<byte> pattern, int start = 0, int end = -1)
    {
        end = end < 0 ? data.Length : end;
        var limit = Math.Min(end, data.Length) - pattern.Length;
        for (var i = start; i <= limit; i++)
        {
            var match = true;
            for (var j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
                return i;
        }
        return -1;
    }
}
