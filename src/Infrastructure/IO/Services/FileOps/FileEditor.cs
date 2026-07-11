
namespace IO;

/// <summary>
/// File edit service - provides file editing capabilities
/// </summary>
public sealed class FileEditor
{
    private readonly IFileSystem _fs;
    private readonly ILogger? _logger;
    private readonly FileOperationConfig _config;

    public FileEditor(IFileSystem fs, FileOperationConfig config, ILogger? logger = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
    }

    /// <summary>
    /// Edit file content (search and replace)
    /// </summary>
    public async Task<FileEditResult> EditFileAsync(
        string filePath,
        string oldString,
        string newString,
        bool replaceAll = false,
        CancellationToken cancellationToken = default)
    {
        // Empty old_string with non-empty new_string means creating a new file (TS behavior)
        if (string.IsNullOrEmpty(oldString))
        {
            if (string.IsNullOrEmpty(newString))
            {
                return FileEditResult.FailureResult(filePath, oldString, newString, "old_string and new_string are both empty");
            }

            var normalizedPath = NormalizePath(filePath);
            try
            {
                if (_fs.FileExists(normalizedPath))
                {
                    var (existingContent, _) = await ReadFileWithEncodingAsync(normalizedPath, cancellationToken).ConfigureAwait(false);
                    if (existingContent.Trim() != string.Empty)
                    {
                        return FileEditResult.FailureResult(normalizedPath, oldString, newString,
                            "Cannot create new file - file already exists and is not empty");
                    }
                    // Empty file with empty old_string is valid - replacing empty with content
                }

                // Ensure parent directory exists
                var dir = Path.GetDirectoryName(normalizedPath);
                if (!string.IsNullOrEmpty(dir) && !_fs.DirectoryExists(dir))
                {
                    _fs.CreateDirectory(dir);
                }

                var normalizedNew = newString.Replace("\r\n", "\n");
                await WriteFileWithLockAsync(normalizedPath, normalizedNew, cancellationToken).ConfigureAwait(false);

                _logger?.LogInformation("File created via edit: {FilePath}", normalizedPath);

                return FileEditResult.SuccessResult(normalizedPath, oldString, newString, string.Empty, normalizedNew, 1,
                    StructuredPatchGenerator.Generate(normalizedPath, string.Empty, normalizedNew, cancellationToken: cancellationToken));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Create file via edit failed: {FilePath}", normalizedPath);
                return FileEditResult.FailureResult(normalizedPath, oldString, newString, ex.Message);
            }
        }

        if (oldString == newString)
        {
            return FileEditResult.FailureResult(filePath, oldString, newString, "old_string and new_string must be different");
        }

        var normalizedPath2 = NormalizePath(filePath);

        try
        {
            if (!_fs.FileExists(normalizedPath2))
            {
                return FileEditResult.FailureResult(normalizedPath2, oldString, newString, "File not found");
            }

            var fileLength = _fs.GetFileLength(normalizedPath2);
            const long maxEditFileSize = 1024L * 1024 * 1024;
            if (fileLength > maxEditFileSize)
            {
                return FileEditResult.FailureResult(normalizedPath2, oldString, newString,
                    $"File too large ({fileLength} bytes) to edit. Maximum editable file size is 1 GB");
            }

            var (originalContent, hasCrlf, fileEncoding) = await ReadFileWithLineEndingDetectionAsync(normalizedPath2, cancellationToken);

            var normalizedOld = oldString.Replace("\r\n", "\n");
            var normalizedNew = newString.Replace("\r\n", "\n");
            var normalizedContent = originalContent.Replace("\r\n", "\n");

            // Step 1: Try exact match, then findActualString (quote normalization), then desanitize
            var actualOldString = FindActualString(normalizedContent, normalizedOld);

            if (actualOldString is null)
            {
                // Try desanitizing the old_string (reverse API sanitization of XML tags)
                var (desanitizedOld, appliedReplacements) = DesanitizeMatchString(normalizedOld);
                if (desanitizedOld != normalizedOld)
                {
                    actualOldString = FindActualString(normalizedContent, desanitizedOld);
                    if (actualOldString is not null)
                    {
                        normalizedOld = desanitizedOld;
                        // Apply same desanitization to new_string
                        foreach (var (from, to) in appliedReplacements)
                        {
                            normalizedNew = normalizedNew.Replace(from, to);
                        }
                    }
                }
            }

            if (actualOldString is null)
            {
                return FileEditResult.FailureResult(normalizedPath2, oldString, newString,
                    "String to replace not found in file. Check that the string exists exactly as provided, including whitespace and indentation.");
            }

            // Step 2: Preserve quote style - if file uses curly quotes, apply them to new_string
            var actualNewString = PreserveQuoteStyle(normalizedOld, actualOldString, normalizedNew);

            // Step 3: Strip trailing whitespace from new_string (except for .md/.mdx files)
            var ext = Path.GetExtension(normalizedPath2);
            var isMarkdown = ext.Equals(".md", StringComparison.OrdinalIgnoreCase)
                          || ext.Equals(".mdx", StringComparison.OrdinalIgnoreCase);
            if (!isMarkdown)
            {
                actualNewString = StripTrailingWhitespace(actualNewString);
            }

            if (!replaceAll)
            {
                var occurrenceCount = CountOccurrences(normalizedContent, normalizedOld);
                if (occurrenceCount > 1)
                {
                    return FileEditResult.FailureResult(normalizedPath2, oldString, newString,
                        $"old_string matched {occurrenceCount} times in the file, but replace_all is false. " +
                        "Provide more context to make old_string unique, or set replace_all to true to replace all occurrences.");
                }
            }

            string updatedContent;
            int replaceCount;

            if (replaceAll)
            {
                updatedContent = normalizedContent.Replace(actualOldString, actualNewString);
                replaceCount = CountOccurrences(normalizedContent, actualOldString);
            }
            else
            {
                var index = normalizedContent.IndexOf(actualOldString, StringComparison.Ordinal);
                if (index >= 0)
                {
                    var sb = new StringBuilder(normalizedContent.Length + actualNewString.Length - actualOldString.Length);
                    sb.Append(normalizedContent, 0, index);
                    sb.Append(actualNewString);
                    sb.Append(normalizedContent, index + actualOldString.Length, normalizedContent.Length - index - actualOldString.Length);
                    updatedContent = sb.ToString();
                    replaceCount = 1;
                }
                else
                {
                    updatedContent = normalizedContent;
                    replaceCount = 0;
                }
            }

            if (hasCrlf)
            {
                updatedContent = updatedContent.Replace("\n", "\r\n");
            }

            await WriteFileWithLockAsync(normalizedPath2, updatedContent, cancellationToken, fileEncoding);

            _logger?.LogInformation(
                "File edited: {FilePath} (replaced {Count} occurrence(s))",
                normalizedPath2,
                replaceCount);

            return FileEditResult.SuccessResult(
                normalizedPath2,
                oldString,
                newString,
                originalContent,
                updatedContent,
                replaceCount,
                StructuredPatchGenerator.Generate(normalizedPath2, originalContent, updatedContent, cancellationToken: cancellationToken));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Edit file failed: {FilePath}", normalizedPath2);
            return FileEditResult.FailureResult(normalizedPath2, oldString, newString, ex.Message);
        }
    }

    /// <summary>
    /// Edit file content by line range
    /// </summary>
    public async Task<FileLineEditResult> EditByLineRangeAsync(
        LineRangeEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (filePath, startLine, endLine, newContent) = (request.FilePath, request.StartLine, request.EndLine, request.NewContent);

        if (startLine < 1)
        {
            return FileLineEditResult.FailureResult(filePath, startLine, endLine, "Start line must be at least 1");
        }

        if (endLine < startLine)
        {
            return FileLineEditResult.FailureResult(filePath, startLine, endLine, "End line must not be less than start line");
        }

        var normalizedPath = NormalizePath(filePath);

        try
        {
            if (!_fs.FileExists(normalizedPath))
            {
                return FileLineEditResult.FailureResult(normalizedPath, startLine, endLine, "File not found");
            }

            // 对齐 TS: 检测 BOM 编码
            var fileEncoding = await FileEncodingDetector.DetectFromFileAsync(normalizedPath, _fs, cancellationToken).ConfigureAwait(false);

            // Read all lines using streaming to avoid loading entire file at once
            var allLines = new List<string>();
            using (var stream = _fs.CreateStream(normalizedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, fileEncoding))
            {
                string? line;
                while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
                {
                    allLines.Add(line);
                }
            }
            var totalLines = allLines.Count;

            if (startLine > totalLines)
            {
                return FileLineEditResult.FailureResult(normalizedPath, startLine, endLine, $"Start line ({startLine}) exceeds total line count ({totalLines})");
            }

            // Adjust end line if out of range
            var actualEndLine = Math.Min(endLine, totalLines);
            var replacedLinesCount = actualEndLine - startLine + 1;

            // Extract original content
            var originalLines = allLines.Skip(startLine - 1).Take(replacedLinesCount).ToList();
            var originalContent = string.Join("\n", originalLines);

            // Build new content
            var newLines = newContent.Split('\n').ToList();

            // Assemble new file content
            var resultLines = new List<string>();

            // Add lines before replacement
            if (startLine > 1)
            {
                resultLines.AddRange(allLines.Take(startLine - 1));
            }

            // Add new content
            resultLines.AddRange(newLines);

            // Add lines after replacement
            if (actualEndLine < totalLines)
            {
                resultLines.AddRange(allLines.Skip(actualEndLine));
            }

            var updatedFileContent = string.Join("\n", resultLines);

            // Write file — 保持原始编码
            await WriteFileWithLockAsync(normalizedPath, updatedFileContent, cancellationToken, fileEncoding);

            _logger?.LogInformation(
                "File line range edited: {FilePath} (lines {StartLine}-{EndLine}, replaced {Count} lines)",
                normalizedPath,
                startLine,
                actualEndLine,
                replacedLinesCount);

            return FileLineEditResult.SuccessResult(
                normalizedPath,
                startLine,
                actualEndLine,
                originalContent,
                newContent,
                updatedFileContent,
                replacedLinesCount);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Edit file by line range failed: {FilePath}", normalizedPath);
            return FileLineEditResult.FailureResult(normalizedPath, startLine, endLine, ex.Message);
        }
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

    private string NormalizePath(string path)
    {
        if (Path.IsPathFullyQualified(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(_fs.GetCurrentDirectory(), path));
    }

    private async Task<(string Content, bool HasCrlf, Encoding Encoding)> ReadFileWithLineEndingDetectionAsync(string path, CancellationToken ct)
    {
        var timeout = IsTestEnvironment() ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(30);
        var result = await FileLockService.AcquireAsync(path, timeout, ct);
        if (!result.Success)
            throw new TimeoutException($"Lock acquisition timed out: {path}");

        await using (result.Lock!)
        {
            if (!_fs.FileExists(path))
                return (string.Empty, false, Encoding.UTF8);

            // 对齐 TS: FileEditTool.ts L207-213 — 检测 BOM 编码
            var encoding = await FileEncodingDetector.DetectFromFileAsync(path, _fs, ct).ConfigureAwait(false);

            await using var stream = _fs.CreateStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, encoding);
            var content = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            var hasCrlf = content.Contains("\r\n");
            return (content, hasCrlf, encoding);
        }
    }

    private async Task<(string Content, Encoding Encoding)> ReadFileWithEncodingAsync(string path, CancellationToken ct)
    {
        var timeout = IsTestEnvironment() ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(30);
        var result = await FileLockService.AcquireAsync(path, timeout, ct);
        if (!result.Success)
            throw new TimeoutException($"Lock acquisition timed out: {path}");

        await using (result.Lock!)
        {
            if (!_fs.FileExists(path))
                return (string.Empty, Encoding.UTF8);

            // 对齐 TS: 检测 BOM 编码
            var encoding = await FileEncodingDetector.DetectFromFileAsync(path, _fs, ct).ConfigureAwait(false);

            await using var stream = _fs.CreateStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, encoding);
            var content = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            return (content, encoding);
        }
    }

    private async Task WriteFileWithLockAsync(string path, string content, CancellationToken ct, Encoding? encoding = null)
    {
        var timeout = IsTestEnvironment() ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(30);
        var result = await FileLockService.AcquireAsync(path, timeout, ct);
        if (!result.Success)
            throw new TimeoutException($"Lock acquisition timed out: {path}");

        await using (result.Lock!)
        {
            var effectiveEncoding = encoding ?? Encoding.UTF8;
            var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await _fs.WriteAllTextAsync(tempPath, content, effectiveEncoding, ct).ConfigureAwait(false);
                _fs.MoveFile(tempPath, path, overwrite: true);
            }
            catch
            {
                if (_fs.FileExists(tempPath)) _fs.DeleteFile(tempPath);
                throw;
            }
        }
    }

    private static bool IsTestEnvironment()
    {
        return TestEnvironmentDetector.IsTestEnvironment;
    }

    #region TS-Aligned Edit Utilities

    // Curly quote constants
    private const char LeftSingleCurlyQuote = '\u2018';  // '
    private const char RightSingleCurlyQuote = '\u2019'; // '
    private const char LeftDoubleCurlyQuote = '\u201C';  // "
    private const char RightDoubleCurlyQuote = '\u201D';  // "

    /// <summary>
    /// Find the actual string in file content, trying exact match first,
    /// then with quote normalization (curly → straight). Mirrors TS findActualString.
    /// Returns null if not found.
    /// </summary>
    internal static string? FindActualString(string fileContent, string searchString)
    {
        // First try exact match
        if (fileContent.Contains(searchString))
            return searchString;

        // Try with normalized quotes (curly → straight)
        var normalizedSearch = NormalizeQuotes(searchString);
        var normalizedFile = NormalizeQuotes(fileContent);

        var searchIndex = normalizedFile.IndexOf(normalizedSearch, StringComparison.Ordinal);
        if (searchIndex >= 0)
        {
            // Return the actual string from the file (preserving curly quotes)
            return fileContent.Substring(searchIndex, searchString.Length);
        }

        return null;
    }

    /// <summary>
    /// Normalize curly quotes to straight quotes for matching.
    /// </summary>
    private static string NormalizeQuotes(string str)
    {
        if (str.IndexOfAny([LeftSingleCurlyQuote, RightSingleCurlyQuote, LeftDoubleCurlyQuote, RightDoubleCurlyQuote]) < 0)
            return str;

        return str
            .Replace(LeftSingleCurlyQuote, '\'')
            .Replace(RightSingleCurlyQuote, '\'')
            .Replace(LeftDoubleCurlyQuote, '"')
            .Replace(RightDoubleCurlyQuote, '"');
    }

    /// <summary>
    /// Preserve quote style: if the file uses curly quotes, apply them to new_string.
    /// Mirrors TS preserveQuoteStyle.
    /// </summary>
    internal static string PreserveQuoteStyle(string oldString, string actualOldString, string newString)
    {
        // If they're the same, no normalization happened
        if (oldString == actualOldString)
            return newString;

        // Detect which curly quote types were in the file
        var hasDoubleQuotes = actualOldString.Contains(LeftDoubleCurlyQuote)
                           || actualOldString.Contains(RightDoubleCurlyQuote);
        var hasSingleQuotes = actualOldString.Contains(LeftSingleCurlyQuote)
                           || actualOldString.Contains(RightSingleCurlyQuote);

        if (!hasDoubleQuotes && !hasSingleQuotes)
            return newString;

        var result = newString;
        if (hasDoubleQuotes)
            result = ApplyCurlyDoubleQuotes(result);
        if (hasSingleQuotes)
            result = ApplyCurlySingleQuotes(result);

        return result;
    }

    private static bool IsOpeningContext(ReadOnlySpan<char> chars, int index)
    {
        if (index == 0)
            return true;

        var prev = chars[index - 1];
        return prev is ' ' or '\t' or '\n' or '\r' or '(' or '[' or '{'
            or '\u2014'   // em dash
            or '\u2013';  // en dash
    }

    private static string ApplyCurlyDoubleQuotes(string str)
    {
        var chars = str.AsSpan();
        var result = new StringBuilder(str.Length);
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] == '"')
            {
                result.Append(IsOpeningContext(chars, i)
                    ? LeftDoubleCurlyQuote
                    : RightDoubleCurlyQuote);
            }
            else
            {
                result.Append(chars[i]);
            }
        }
        return result.ToString();
    }

    private static string ApplyCurlySingleQuotes(string str)
    {
        var chars = str.AsSpan();
        var result = new StringBuilder(str.Length);
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] == '\'')
            {
                // Don't convert apostrophes in contractions (e.g., "don't", "it's")
                var prev = i > 0 ? chars[i - 1] : '\0';
                var next = i < chars.Length - 1 ? chars[i + 1] : '\0';
                var prevIsLetter = char.IsLetter(prev);
                var nextIsLetter = char.IsLetter(next);

                if (prevIsLetter && nextIsLetter)
                {
                    // Apostrophe in a contraction — use right single curly quote
                    result.Append(RightSingleCurlyQuote);
                }
                else
                {
                    result.Append(IsOpeningContext(chars, i)
                        ? LeftSingleCurlyQuote
                        : RightSingleCurlyQuote);
                }
            }
            else
            {
                result.Append(chars[i]);
            }
        }
        return result.ToString();
    }

    /// <summary>
    /// Strip trailing whitespace from each line. Mirrors TS stripTrailingWhitespace.
    /// Preserves line endings (CRLF, LF, CR).
    /// </summary>
    internal static string StripTrailingWhitespace(string str)
    {
        if (string.IsNullOrEmpty(str))
            return str;

        // Split preserving line endings
        var result = new StringBuilder(str.Length);
        var lineStart = 0;

        for (var i = 0; i < str.Length; i++)
        {
            if (str[i] == '\n' || str[i] == '\r')
            {
                // Trim trailing whitespace from the line content
                var lineEnd = i;
                while (lineEnd > lineStart && char.IsWhiteSpace(str[lineEnd - 1]))
                    lineEnd--;

                result.Append(str, lineStart, lineEnd - lineStart);

                // Preserve the line ending
                result.Append(str[i]);
                if (str[i] == '\r' && i + 1 < str.Length && str[i + 1] == '\n')
                {
                    result.Append('\n');
                    i++;
                }

                lineStart = i + 1;
            }
        }

        // Handle last line (no trailing newline)
        if (lineStart < str.Length)
        {
            var lineEnd = str.Length;
            while (lineEnd > lineStart && char.IsWhiteSpace(str[lineEnd - 1]))
                lineEnd--;

            result.Append(str, lineStart, lineEnd - lineStart);
        }

        return result.ToString();
    }

    /// <summary>
    /// Reverse API sanitization of XML tags and special markers.
    /// Mirrors TS desanitizeMatchString.
    /// Returns the desanitized string and the list of applied replacements.
    /// </summary>
    internal static (string Result, (string From, string To)[] AppliedReplacements) DesanitizeMatchString(string matchString)
    {
        var result = matchString;
        var applied = new List<(string From, string To)>();

        foreach (var (from, to) in DesanitizationMap)
        {
            var before = result;
            result = result.Replace(from, to);
            if (before != result)
            {
                applied.Add((from, to));
            }
        }

        return (result, applied.ToArray());
    }

    private static readonly (string From, string To)[] DesanitizationMap =
    [
        ("<fnr>", "<function_results>"),
        ("<n>", "<name>"),
        ("</n>", "</name>"),
        ("<o>", "<output>"),
        ("</o>", "</output>"),
        ("<e>", "<error>"),
        ("</e>", "</error>"),
        ("<s>", "<system>"),
        ("</s>", "</system>"),
        ("<r>", "<result>"),
        ("</r>", "</result>"),
        ("< META_START >", "<META_START>"),
        ("< META_END >", "<META_END>"),
        ("< EOT >", "<EOT>"),
        ("< META >", "<META>"),
        ("< SOS >", "<SOS>"),
        ("\n\nH:", "\n\nHuman:"),
        ("\n\nA:", "\n\nAssistant:"),
    ];

    #endregion
}
