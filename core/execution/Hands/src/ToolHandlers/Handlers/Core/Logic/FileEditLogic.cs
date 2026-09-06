namespace Tools.Handlers;

public record BatchEditResult(string FilePath, FileEditResult Result);

[Register(typeof(FileEditLogic), ServiceLifetime.Singleton)]
public sealed partial class FileEditLogic : ServiceEntity
{

    private readonly IFileSystem _fs;

    public FileEditLogic(IFileSystem fs)
    {
        _fs = fs;
    }

    public async Task<FileEditResult> EditWithRegexAsync(
        string filePath,
        string pattern,
        string replacement,
        bool replaceAll = true,
        CancellationToken cancellationToken = default)
    {
        if (!_fs.FileExists(filePath))
            return FileEditResult.FailureResult(filePath, pattern, replacement, FileSuggestionHelper.BuildFileNotFoundDiagnostic(filePath, _fs));

        Regex regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.Multiline);
        }
        catch (ArgumentException ex)
        {
            return FileEditResult.FailureResult(filePath, pattern, replacement, L.T(StringKey.FileEditRegexInvalid, ex.Message));
        }

        try
        {
            return await _fs.EditFileAsync<FileEditResult>(filePath, async (bytes, ct) =>
            {
                var (originalContent, encoding) = DecodeBytes(bytes);

                if (!regex.IsMatch(originalContent))
                    return (null, FileEditResult.FailureResult(filePath, pattern, replacement, L.T(StringKey.FileEditPatternNotFound)));

                var count = replaceAll ? regex.Matches(originalContent).Count : 1;
                var updatedContent = replaceAll
                    ? regex.Replace(originalContent, replacement)
                    : regex.Replace(originalContent, replacement, 1);

                var newBytes = EncodeString(updatedContent, encoding);
                return (newBytes, FileEditResult.SuccessResult(filePath, pattern, replacement, originalContent, updatedContent, count));
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return FileEditResult.FailureResult(filePath, pattern, replacement, FileSuggestionHelper.BuildFileNotFoundDiagnostic(filePath, _fs));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var diagnostic = ToolDiagnostic.Create("EditRegexWriteFailed",
                $"正则编辑写入失败: {ex.Message}",
                [new DiagnosticDetail("filePath", filePath), new DiagnosticDetail("exceptionType", ex.GetType().Name)],
                ["检查文件权限、是否被其他进程锁定。"]);
            return FileEditResult.FailureResult(filePath, pattern, replacement, diagnostic);
        }
    }

    public async Task<FileLineEditResult> InsertLinesAfterAsync(
        string filePath,
        int afterLine,
        string newContent,
        CancellationToken cancellationToken = default)
    {
        if (!_fs.FileExists(filePath))
            return FileLineEditResult.FailureResult(filePath, afterLine, afterLine, FileSuggestionHelper.BuildFileNotFoundDiagnostic(filePath, _fs));

        try
        {
            return await _fs.EditFileAsync<FileLineEditResult>(filePath, async (bytes, ct) =>
            {
                var (content, encoding) = DecodeBytes(bytes);
                var allLines = SplitLines(content);

                if (afterLine < 0 || afterLine > allLines.Count)
                    return (null, FileLineEditResult.FailureResult(filePath, afterLine, afterLine, L.T(StringKey.FileEditLineOutOfRange, afterLine, allLines.Count)));

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
                var newBytes = EncodeString(updatedFileContent, encoding);
                return (newBytes, FileLineEditResult.SuccessResult(filePath, afterLine, afterLine + newLines.Length, string.Empty, newContent, updatedFileContent, newLines.Length));
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return FileLineEditResult.FailureResult(filePath, afterLine, afterLine, FileSuggestionHelper.BuildFileNotFoundDiagnostic(filePath, _fs));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var diagnostic = ToolDiagnostic.Create("InsertLinesWriteFailed",
                $"插入行写入失败: {ex.Message}",
                [new DiagnosticDetail("filePath", filePath), new DiagnosticDetail("exceptionType", ex.GetType().Name)],
                ["检查文件权限、是否被其他进程锁定。"]);
            return FileLineEditResult.FailureResult(filePath, afterLine, afterLine, diagnostic);
        }
    }

    public async Task<FileLineEditResult> DeleteLinesAsync(
        string filePath,
        int startLine,
        int endLine,
        CancellationToken cancellationToken = default)
    {
        if (!_fs.FileExists(filePath))
            return FileLineEditResult.FailureResult(filePath, startLine, endLine, FileSuggestionHelper.BuildFileNotFoundDiagnostic(filePath, _fs));

        if (startLine > endLine)
            return FileLineEditResult.FailureResult(filePath, startLine, endLine, L.T(StringKey.FileEditStartLineGreaterThanEnd));

        try
        {
            return await _fs.EditFileAsync<FileLineEditResult>(filePath, async (bytes, ct) =>
            {
                var (content, encoding) = DecodeBytes(bytes);
                var allLines = SplitLines(content);

                if (startLine < 1 || startLine > allLines.Count)
                    return (null, FileLineEditResult.FailureResult(filePath, startLine, endLine, L.T(StringKey.FileEditStartLineOutOfRange, startLine, allLines.Count)));

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
                var newBytes = EncodeString(updatedFileContent, encoding);
                var deletedCount = actualEndLine - startLine + 1;
                return (newBytes, FileLineEditResult.SuccessResult(filePath, startLine, actualEndLine, originalContent, string.Empty, updatedFileContent, deletedCount));
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return FileLineEditResult.FailureResult(filePath, startLine, endLine, FileSuggestionHelper.BuildFileNotFoundDiagnostic(filePath, _fs));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var diagnostic = ToolDiagnostic.Create("DeleteLinesWriteFailed",
                $"删除行写入失败: {ex.Message}",
                [new DiagnosticDetail("filePath", filePath), new DiagnosticDetail("exceptionType", ex.GetType().Name)],
                ["检查文件权限、是否被其他进程锁定。"]);
            return FileLineEditResult.FailureResult(filePath, startLine, endLine, diagnostic);
        }
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
                    results.Add(new BatchEditResult(filePath, FileEditResult.FailureResult(filePath, oldString, newString, FileSuggestionHelper.BuildFileNotFoundDiagnostic(filePath, _fs))));
                    continue;
                }

                var editResult = await _fs.EditFileAsync<FileEditResult>(filePath, async (bytes, ct) =>
                {
                    var (originalContent, encoding) = DecodeBytes(bytes);

                    if (!originalContent.Contains(oldString))
                    {
                        var diagnostic = EditDiagnosticBuilder.BuildDiagnostic(originalContent, oldString);
                        return (null, FileEditResult.FailureResult(filePath, oldString, newString, diagnostic.ToToolDiagnostic()));
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

                    var newBytes = EncodeString(updatedContent, encoding);
                    return (newBytes, FileEditResult.SuccessResult(filePath, oldString, newString, originalContent, updatedContent, replaceCount));
                }, cancellationToken).ConfigureAwait(false);

                results.Add(new BatchEditResult(filePath, editResult));
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
    /// 从字节数组解码为字符串 — 自动检测 BOM 编码，StreamReader 自动跳过 BOM。
    /// </summary>
    private static (string Content, Encoding Encoding) DecodeBytes(byte[] bytes)
    {
        var encoding = FileEncodingDetector.DetectFromBOM(bytes);
        using var ms = new MemoryStream(bytes, writable: false);
        using var reader = new StreamReader(ms, encoding);
        return (reader.ReadToEnd(), encoding);
    }

    /// <summary>
    /// 将字符串编码为字节数组 — 保留原始编码的 BOM（如有）。
    /// </summary>
    private static byte[] EncodeString(string content, Encoding encoding)
    {
        var preamble = encoding.GetPreamble();
        var contentBytes = encoding.GetBytes(content);
        if (preamble.Length == 0)
            return contentBytes;
        var bytes = new byte[preamble.Length + contentBytes.Length];
        Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
        Buffer.BlockCopy(contentBytes, 0, bytes, preamble.Length, contentBytes.Length);
        return bytes;
    }

    /// <summary>
    /// 按行分割字符串 — 对齐 File.ReadAllLines 语义：去除行终止符，忽略末尾空行。
    /// </summary>
    private static List<string> SplitLines(string content)
    {
        if (string.IsNullOrEmpty(content))
            return [];
        var lines = content.Split('\n');
        var result = new List<string>(lines.Length);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (i == lines.Length - 1 && line.Length == 0)
                continue;
            result.Add(line);
        }
        return result;
    }
}
