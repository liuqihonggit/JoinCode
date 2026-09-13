namespace Tools.Handlers;

/// <summary>
/// 批量编辑单文件结果,携带文件路径与其对应的编辑结果。
/// </summary>
/// <param name="FilePath">文件路径。</param>
/// <param name="Result">编辑结果。</param>
public record BatchEditResult(string FilePath, FileEditResult Result);

/// <summary>
/// 文件编辑逻辑,提供正则替换、行插入、行删除与批量编辑能力。
/// </summary>
[Register(typeof(FileEditLogic), ServiceLifetime.Singleton)]
public sealed partial class FileEditLogic : ServiceEntity
{

    private readonly IFileSystem _fs;

    /// <summary>
    /// 初始化 FileEditLogic 的新实例。
    /// </summary>
    /// <param name="fs">文件系统抽象。</param>
    public FileEditLogic(IFileSystem fs)
    {
        _fs = fs;
    }

    /// <summary>
    /// 使用正则表达式异步编辑文件内容,可选全部替换或仅替换首个匹配。
    /// </summary>
    /// <param name="filePath">文件路径。</param>
    /// <param name="pattern">正则表达式模式。</param>
    /// <param name="replacement">替换字符串。</param>
    /// <param name="replaceAll">是否替换全部匹配,默认 true;false 时仅替换首个。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>包含替换计数与诊断信息的编辑结果。</returns>
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
                var (originalContent, encoding) = FileEncodingDetector.DecodeBytes(bytes);

                if (!regex.IsMatch(originalContent))
                    return (null, FileEditResult.FailureResult(filePath, pattern, replacement, L.T(StringKey.FileEditPatternNotFound)));

                var count = replaceAll ? regex.Matches(originalContent).Count : 1;
                var updatedContent = replaceAll
                    ? regex.Replace(originalContent, replacement)
                    : regex.Replace(originalContent, replacement, 1);

                var newBytes = FileEncodingDetector.EncodeString(updatedContent, encoding);
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

    /// <summary>
    /// 在指定行号之后异步插入新内容,按换行符分割为多行。
    /// </summary>
    /// <param name="filePath">文件路径。</param>
    /// <param name="afterLine">目标行号(0 表示文件开头插入,等于行数表示末尾追加)。</param>
    /// <param name="newContent">要插入的新内容,按 '\n' 分割为多行。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>包含插入行范围与受影响行数的行编辑结果。</returns>
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
                var (content, encoding) = FileEncodingDetector.DecodeBytes(bytes);
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
                var newBytes = FileEncodingDetector.EncodeString(updatedFileContent, encoding);
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

    /// <summary>
    /// 异步删除文件中指定起止行号范围内的行。
    /// </summary>
    /// <param name="filePath">文件路径。</param>
    /// <param name="startLine">起始行号(从 1 开始)。</param>
    /// <param name="endLine">结束行号(从 1 开始,超出总行数时自动截断)。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>包含实际删除行范围与删除行数的行编辑结果。</returns>
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
                var (content, encoding) = FileEncodingDetector.DecodeBytes(bytes);
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
                var newBytes = FileEncodingDetector.EncodeString(updatedFileContent, encoding);
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

    /// <summary>
    /// 对多个文件批量执行字符串替换,逐文件独立处理并汇总结果。
    /// </summary>
    /// <param name="filePaths">要编辑的文件路径列表。</param>
    /// <param name="oldString">要查找的原始字符串。</param>
    /// <param name="newString">替换后的新字符串。</param>
    /// <param name="replaceAll">是否替换全部匹配,默认 true;false 时仅替换首个。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>每个文件对应的批量编辑结果列表。</returns>
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
                    var (originalContent, encoding) = FileEncodingDetector.DecodeBytes(bytes);

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

                    var newBytes = FileEncodingDetector.EncodeString(updatedContent, encoding);
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
