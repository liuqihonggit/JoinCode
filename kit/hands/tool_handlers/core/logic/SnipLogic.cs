namespace Tools.Handlers;

/// <summary>
/// 文件截取预览结果,包含路径、大小、总行数与预览内容。
/// </summary>
/// <param name="FilePath">文件路径。</param>
/// <param name="FileSize">文件字节大小。</param>
/// <param name="TotalLines">文件总行数。</param>
/// <param name="PreviewContent">预览文本内容。</param>
public record SnipPreview(string FilePath, long FileSize, int TotalLines, string PreviewContent);

/// <summary>
/// 文件片段截取逻辑,按行号或偏移读取文件内容并生成预览。
/// </summary>
[Register(typeof(SnipLogic), ServiceLifetime.Singleton)]
public sealed partial class SnipLogic : ServiceEntity {

    private readonly IFileSystem _fs;

    /// <summary>
    /// 初始化 SnipLogic 的新实例。
    /// </summary>
    /// <param name="fs">文件系统抽象。</param>
    public SnipLogic(IFileSystem fs) {
        _fs = fs;
    }

    /// <summary>
    /// 按起始行号与行数异步截取文件内容。UTF-8 文件走零拷贝行索引,其他编码按行流式读取。
    /// </summary>
    /// <param name="filePath">文件路径。</param>
    /// <param name="startLine">起始行号(从 0 开始,负值自动归零)。</param>
    /// <param name="lineCount">要截取的行数,小于等于 0 时返回空字符串。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>截取到的文件行文本。</returns>
    public async Task<string> SnipLinesAsync(string filePath, int startLine, int lineCount, CancellationToken cancellationToken = default) {
        if (lineCount <= 0)
            return string.Empty;

        if (!_fs.FileExists(filePath))
            throw new FileNotFoundException(L.T(StringKey.SnipFileNotFound, filePath), filePath);

        if (startLine < 0)
            startLine = 0;

        var encoding = await FileEncodingDetector.DetectFromFileAsync(filePath, _fs, cancellationToken).ConfigureAwait(false);
        if (encoding is UTF8Encoding) {
            var content = await _fs.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (content.Length == 0)
                return string.Empty;
            var ranges = LineSpanIndexer.BuildLineRanges(content.AsSpan(), cancellationToken);
            var result = new StringBuilder();
            for (var i = startLine; i < ranges.Count && i - startLine < lineCount; i++) {
                var (start, length) = ranges[i];
                result.Append(content.AsSpan(start, length)).AppendLine();
            }
            return result.ToString();
        }

        var result2 = new StringBuilder();
        var currentLine = 0;
        await using var stream = _fs.OpenRead(filePath);
        using var reader = new StreamReader(stream, encoding);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line) {
            if (currentLine >= startLine) {
                if (currentLine - startLine >= lineCount)
                    break;
                result2.AppendLine(line);
            }
            currentLine++;
        }
        return result2.ToString();
    }

    /// <summary>
    /// 按偏移量与限制数异步截取文件行,语义等同于 SnipLinesAsync。
    /// </summary>
    /// <param name="filePath">文件路径。</param>
    /// <param name="offset">起始行偏移(从 0 开始)。</param>
    /// <param name="limit">要截取的行数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>截取到的文件行文本。</returns>
    public async Task<string> SnipOffsetAsync(string filePath, int offset, int limit, CancellationToken cancellationToken = default) {
        return await SnipLinesAsync(filePath, offset, limit, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步获取文件预览,包含文件大小、总行数与最多 maxPreviewLines 行的预览内容。
    /// </summary>
    /// <param name="filePath">文件路径。</param>
    /// <param name="maxPreviewLines">预览的最大行数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>包含路径、大小、总行数与预览文本的 SnipPreview 对象。</returns>
    public async Task<SnipPreview> GetPreviewAsync(string filePath, int maxPreviewLines, CancellationToken cancellationToken = default) {
        if (!_fs.FileExists(filePath))
            throw new FileNotFoundException(L.T(StringKey.SnipFileNotFound, filePath), filePath);

        long fileSize;
        using (var sizeStream = _fs.OpenRead(filePath)) {
            fileSize = sizeStream.Length;
        }

        var encoding = await FileEncodingDetector.DetectFromFileAsync(filePath, _fs, cancellationToken).ConfigureAwait(false);
        if (encoding is UTF8Encoding) {
            var content = await _fs.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            var ranges = LineSpanIndexer.BuildLineRanges(content.AsSpan(), cancellationToken);
            var previewContent = new StringBuilder();
            var previewLinesCollected = 0;
            for (var i = 0; i < ranges.Count; i++) {
                if (previewLinesCollected < maxPreviewLines) {
                    var (start, length) = ranges[i];
                    previewContent.Append(content.AsSpan(start, length)).AppendLine();
                    previewLinesCollected++;
                }
            }
            return new SnipPreview(filePath, fileSize, ranges.Count, previewContent.ToString());
        }

        var totalLines = 0;
        var previewContent2 = new StringBuilder();
        var previewLinesCollected2 = 0;
        await using var stream = _fs.OpenRead(filePath);
        using var reader = new StreamReader(stream, encoding);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line) {
            totalLines++;
            if (previewLinesCollected2 < maxPreviewLines) {
                previewContent2.AppendLine(line);
                previewLinesCollected2++;
            }
        }
        return new SnipPreview(filePath, fileSize, totalLines, previewContent2.ToString());
    }
}