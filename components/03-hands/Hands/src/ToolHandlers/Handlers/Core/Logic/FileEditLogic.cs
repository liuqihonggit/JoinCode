namespace Tools.Handlers;

public record BatchEditResult(string FilePath, FileEditResult Result);

[Register]
public sealed partial class FileEditLogic
{
    [Inject] private readonly IFileSystem _fs;

    public async Task<FileEditResult> EditWithRegexAsync(
        string filePath,
        string pattern,
        string replacement,
        bool replaceAll = true,
        CancellationToken cancellationToken = default)
    {
        if (!_fs.FileExists(filePath))
            return FileEditResult.FailureResult(filePath, pattern, replacement, L.T(StringKey.FileEditFileNotExist));

        Regex regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.Multiline);
        }
        catch (ArgumentException ex)
        {
            return FileEditResult.FailureResult(filePath, pattern, replacement, L.T(StringKey.FileEditRegexInvalid, ex.Message));
        }

        var originalContent = await ReadFileWithEncodingAsync(filePath, cancellationToken).ConfigureAwait(false);

        if (!regex.IsMatch(originalContent))
            return FileEditResult.FailureResult(filePath, pattern, replacement, L.T(StringKey.FileEditPatternNotFound));

        var count = replaceAll ? regex.Matches(originalContent).Count : 1;

        string updatedContent;
        if (replaceAll)
        {
            updatedContent = regex.Replace(originalContent, replacement);
        }
        else
        {
            updatedContent = regex.Replace(originalContent, replacement, 1);
        }

        await WriteFileWithEncodingAsync(filePath, updatedContent, cancellationToken).ConfigureAwait(false);

        return FileEditResult.SuccessResult(filePath, pattern, replacement, originalContent, updatedContent, count);
    }

    public async Task<FileLineEditResult> InsertLinesAfterAsync(
        string filePath,
        int afterLine,
        string newContent,
        CancellationToken cancellationToken = default)
    {
        if (!_fs.FileExists(filePath))
            return FileLineEditResult.FailureResult(filePath, afterLine, afterLine, L.T(StringKey.FileEditFileNotExist));

        var allLines = new List<string>();
        var fileEncoding = await FileEncodingDetector.DetectFromFileAsync(filePath, _fs, cancellationToken).ConfigureAwait(false);
        using (var stream = _fs.OpenRead(filePath))
        using (var reader = new StreamReader(stream, fileEncoding))
        {
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
                allLines.Add(line);
        }

        if (afterLine < 0 || afterLine > allLines.Count)
            return FileLineEditResult.FailureResult(filePath, afterLine, afterLine, L.T(StringKey.FileEditLineOutOfRange, afterLine, allLines.Count));

        var newLines = newContent.Split('\n');
        var resultLines = new List<string>();

        for (var i = 0; i < allLines.Count; i++)
        {
            resultLines.Add(allLines[i]);
            if (i == afterLine)
                resultLines.AddRange(newLines);
        }

        if (afterLine == allLines.Count)
            resultLines.AddRange(newLines);

        var updatedFileContent = string.Join("\n", resultLines);
        await WriteFileWithEncodingAsync(filePath, updatedFileContent, cancellationToken, fileEncoding).ConfigureAwait(false);

        return FileLineEditResult.SuccessResult(filePath, afterLine, afterLine + newLines.Length, string.Empty, newContent, updatedFileContent, newLines.Length);
    }

    public async Task<FileLineEditResult> DeleteLinesAsync(
        string filePath,
        int startLine,
        int endLine,
        CancellationToken cancellationToken = default)
    {
        if (!_fs.FileExists(filePath))
            return FileLineEditResult.FailureResult(filePath, startLine, endLine, L.T(StringKey.FileEditFileNotExist));

        if (startLine > endLine)
            return FileLineEditResult.FailureResult(filePath, startLine, endLine, L.T(StringKey.FileEditStartLineGreaterThanEnd));

        var allLines = new List<string>();
        var fileEncoding = await FileEncodingDetector.DetectFromFileAsync(filePath, _fs, cancellationToken).ConfigureAwait(false);
        using (var stream = _fs.OpenRead(filePath))
        using (var reader = new StreamReader(stream, fileEncoding))
        {
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
                allLines.Add(line);
        }

        if (startLine < 1 || startLine > allLines.Count)
            return FileLineEditResult.FailureResult(filePath, startLine, endLine, L.T(StringKey.FileEditStartLineOutOfRange, startLine, allLines.Count));

        var actualEndLine = Math.Min(endLine, allLines.Count);
        var originalContent = string.Join("\n", allLines.Skip(startLine - 1).Take(actualEndLine - startLine + 1));

        var resultLines = new List<string>();
        for (var i = 0; i < allLines.Count; i++)
        {
            var lineNumber = i + 1;
            if (lineNumber < startLine || lineNumber > actualEndLine)
                resultLines.Add(allLines[i]);
        }

        var updatedFileContent = string.Join("\n", resultLines);
        await WriteFileWithEncodingAsync(filePath, updatedFileContent, cancellationToken, fileEncoding).ConfigureAwait(false);

        var deletedCount = actualEndLine - startLine + 1;
        return FileLineEditResult.SuccessResult(filePath, startLine, actualEndLine, originalContent, string.Empty, updatedFileContent, deletedCount);
    }

    public async Task<IReadOnlyList<BatchEditResult>> BatchEditAsync(
        IReadOnlyList<string> filePaths,
        string oldString,
        string newString,
        bool replaceAll = true,
        CancellationToken cancellationToken = default)
    {
        var results = new List<BatchEditResult>();
        foreach (var filePath in filePaths)
        {
            try
            {
                if (!_fs.FileExists(filePath))
                {
                    results.Add(new BatchEditResult(filePath, FileEditResult.FailureResult(filePath, oldString, newString, L.T(StringKey.FileEditFileNotExist))));
                    continue;
                }

                var originalContent = await ReadFileWithEncodingAsync(filePath, cancellationToken).ConfigureAwait(false);

                if (!originalContent.Contains(oldString))
                {
                    results.Add(new BatchEditResult(filePath, FileEditResult.FailureResult(filePath, oldString, newString, L.T(StringKey.FileEditStringNotFound))));
                    continue;
                }

                string updatedContent;
                int replaceCount;

                if (replaceAll)
                {
                    updatedContent = originalContent.Replace(oldString, newString);
                    replaceCount = CountOccurrences(originalContent, oldString);
                }
                else
                {
                    var index = originalContent.IndexOf(oldString, StringComparison.Ordinal);
                    var sb = new StringBuilder(originalContent.Length + newString.Length - oldString.Length);
                    sb.Append(originalContent, 0, index);
                    sb.Append(newString);
                    sb.Append(originalContent, index + oldString.Length, originalContent.Length - index - oldString.Length);
                    updatedContent = sb.ToString();
                    replaceCount = 1;
                }

                await WriteFileWithEncodingAsync(filePath, updatedContent, cancellationToken).ConfigureAwait(false);
                results.Add(new BatchEditResult(filePath, FileEditResult.SuccessResult(filePath, oldString, newString, originalContent, updatedContent, replaceCount)));
            }
            catch (Exception ex)
            {
                results.Add(new BatchEditResult(filePath, FileEditResult.FailureResult(filePath, oldString, newString, ex.Message)));
            }
        }

        return results;
    }

    private static int CountOccurrences(string text, string substring)
    {
        if (string.IsNullOrEmpty(substring) || string.IsNullOrEmpty(text))
            return 0;

        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(substring, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += substring.Length;
        }

        return count;
    }

    /// <summary>
    /// 读取文件内容，自动检测 BOM 编码。
    /// 对齐 TS: FileEditTool.ts L207-213
    /// </summary>
    private async Task<string> ReadFileWithEncodingAsync(string filePath, CancellationToken ct)
    {
        var encoding = await FileEncodingDetector.DetectFromFileAsync(filePath, _fs, ct).ConfigureAwait(false);
        return await _fs.ReadAllTextAsync(filePath, encoding, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 写入文件内容，保持原始编码。
    /// 对齐 TS: writeTextContent(filePath, content, encoding, endings)
    /// </summary>
    private async Task WriteFileWithEncodingAsync(string filePath, string content, CancellationToken ct, Encoding? encoding = null)
    {
        var effectiveEncoding = encoding ?? await FileEncodingDetector.DetectFromFileAsync(filePath, _fs, ct).ConfigureAwait(false);
        await _fs.WriteAllTextAsync(filePath, content, effectiveEncoding, ct).ConfigureAwait(false);
    }
}
