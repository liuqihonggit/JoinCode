namespace Tools.Handlers;

public record SnipPreview(string FilePath, long FileSize, int TotalLines, string PreviewContent);

[Register(typeof(SnipLogic), ServiceLifetime.Singleton)]
public sealed partial class SnipLogic : ServiceEntity
{

    private readonly IFileSystem _fs;

    public SnipLogic(IFileSystem fs)
    {
        _fs = fs;
    }

    public async Task<string> SnipLinesAsync(string filePath, int startLine, int lineCount, CancellationToken cancellationToken = default)
    {
        if (lineCount <= 0)
            return string.Empty;

        if (!_fs.FileExists(filePath))
            throw new FileNotFoundException(L.T(StringKey.SnipFileNotFound, filePath), filePath);

        if (startLine < 0)
            startLine = 0;

        var encoding = await FileEncodingDetector.DetectFromFileAsync(filePath, _fs, cancellationToken).ConfigureAwait(false);
        if (encoding is UTF8Encoding)
        {
            var content = await _fs.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (content.Length == 0)
                return string.Empty;
            var ranges = LineSpanIndexer.BuildLineRanges(content.AsSpan(), cancellationToken);
            var result = new StringBuilder();
            for (var i = startLine; i < ranges.Count && i - startLine < lineCount; i++)
            {
                var (start, length) = ranges[i];
                result.Append(content.AsSpan(start, length)).AppendLine();
            }
            return result.ToString();
        }

        var result2 = new StringBuilder();
        var currentLine = 0;
        using var stream = _fs.OpenRead(filePath);
        using var reader = new StreamReader(stream, encoding);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (currentLine >= startLine)
            {
                if (currentLine - startLine >= lineCount)
                    break;
                result2.AppendLine(line);
            }
            currentLine++;
        }
        return result2.ToString();
    }

    public async Task<string> SnipOffsetAsync(string filePath, int offset, int limit, CancellationToken cancellationToken = default)
    {
        return await SnipLinesAsync(filePath, offset, limit, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SnipPreview> GetPreviewAsync(string filePath, int maxPreviewLines, CancellationToken cancellationToken = default)
    {
        if (!_fs.FileExists(filePath))
            throw new FileNotFoundException(L.T(StringKey.SnipFileNotFound, filePath), filePath);

        long fileSize;
        using (var sizeStream = _fs.OpenRead(filePath))
        {
            fileSize = sizeStream.Length;
        }

        var encoding = await FileEncodingDetector.DetectFromFileAsync(filePath, _fs, cancellationToken).ConfigureAwait(false);
        if (encoding is UTF8Encoding)
        {
            var content = await _fs.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            var ranges = LineSpanIndexer.BuildLineRanges(content.AsSpan(), cancellationToken);
            var previewContent = new StringBuilder();
            var previewLinesCollected = 0;
            for (var i = 0; i < ranges.Count; i++)
            {
                if (previewLinesCollected < maxPreviewLines)
                {
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
        using var stream = _fs.OpenRead(filePath);
        using var reader = new StreamReader(stream, encoding);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            totalLines++;
            if (previewLinesCollected2 < maxPreviewLines)
            {
                previewContent2.AppendLine(line);
                previewLinesCollected2++;
            }
        }
        return new SnipPreview(filePath, fileSize, totalLines, previewContent2.ToString());
    }
}