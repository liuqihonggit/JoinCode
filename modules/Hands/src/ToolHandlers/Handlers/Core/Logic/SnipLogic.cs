namespace Tools.Handlers;

public record SnipPreview(string FilePath, long FileSize, int TotalLines, string PreviewContent);

[Register]
public sealed partial class SnipLogic
{
    [Inject] private readonly IFileSystem _fs;

    public async Task<string> SnipLinesAsync(string filePath, int startLine, int lineCount, CancellationToken cancellationToken = default)
    {
        if (lineCount <= 0)
            return string.Empty;

        if (!_fs.FileExists(filePath))
            throw new FileNotFoundException(L.T(StringKey.SnipFileNotFound, filePath), filePath);

        if (startLine < 0)
            startLine = 0;

        var result = new StringBuilder();
        var currentLine = 0;

        using var stream = _fs.OpenRead(filePath);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (currentLine >= startLine)
            {
                if (currentLine - startLine >= lineCount)
                    break;
                result.AppendLine(line);
            }

            currentLine++;
        }

        return result.ToString();
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

        var totalLines = 0;
        var previewContent = new StringBuilder();
        var previewLinesCollected = 0;

        using var stream = _fs.OpenRead(filePath);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            totalLines++;

            if (previewLinesCollected < maxPreviewLines)
            {
                previewContent.AppendLine(line);
                previewLinesCollected++;
            }
        }

        return new SnipPreview(filePath, fileSize, totalLines, previewContent.ToString());
    }
}