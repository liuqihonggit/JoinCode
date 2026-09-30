
namespace IO;

/// <summary>
/// File edit service - provides file editing capabilities
/// </summary>
public sealed class FileEditor {
    private readonly IFileSystem _fs;
    private readonly ILogger? _logger;
    private readonly FileOperationConfig _config;

    /// <summary>
    /// 构造文件编辑器
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="config">文件操作配置</param>
    /// <param name="logger">可选日志记录器</param>
    public FileEditor(IFileSystem fs, FileOperationConfig config, ILogger? logger = null) {
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
        CancellationToken cancellationToken = default) {
        // Empty old_string with non-empty new_string means creating a new file (TS behavior)
        if (string.IsNullOrEmpty(oldString)) {
            return await CreateNewFileViaEditAsync(filePath, oldString, newString, cancellationToken).ConfigureAwait(false);
        }

        if (oldString == newString) {
            return FileEditResult.FailureResult(filePath, oldString, newString, "old_string and new_string must be different");
        }

        var normalizedPath2 = NormalizePath(filePath);

        try {
            if (!_fs.FileExists(normalizedPath2)) {
                return FileEditResult.FailureResult(normalizedPath2, oldString, newString,
                    FileSuggestionHelper.BuildFileNotFoundDiagnostic(normalizedPath2, _fs));
            }

            var fileLength = _fs.GetFileLength(normalizedPath2);
            const long maxEditFileSize = 1024L * 1024 * 1024;
            if (fileLength > maxEditFileSize) {
                return FileEditResult.FailureResult(normalizedPath2, oldString, newString,
                    $"File too large ({fileLength} bytes) to edit. Maximum editable file size is 1 GB");
            }

            var (originalContent, hasCrlf, fileEncoding) = await ReadFileWithLineEndingDetectionAsync(normalizedPath2, cancellationToken).ConfigureAwait(false);

            var (normalizedOld, normalizedNew, normalizedContent) = NormalizeForMatch(oldString, newString, originalContent);

            // Step 1: Try exact match, then findActualString (quote normalization), then desanitize
            var actualOldString = FindActualString(normalizedContent, normalizedOld);

            if (actualOldString is null) {
                // Try desanitizing the old_string (reverse API sanitization of XML tags)
                var (actual, old, newStr) = TryDesanitizeMatch(normalizedContent, normalizedOld, normalizedNew);
                actualOldString = actual;
                normalizedOld = old;
                normalizedNew = newStr;
            }

            if (actualOldString is null) {
                var diagnostic = EditDiagnosticBuilder.BuildDiagnostic(normalizedContent, normalizedOld);
                return FileEditResult.FailureResult(normalizedPath2, oldString, newString,
                    diagnostic.ToToolDiagnostic());
            }

            // Step 2: Preserve quote style - if file uses curly quotes, apply them to new_string
            var actualNewString = PreserveQuoteStyle(normalizedOld, actualOldString, normalizedNew);

            // Step 3: Strip trailing whitespace from new_string (except for .md/.mdx files)
            if (!IsMarkdownFile(normalizedPath2)) {
                actualNewString = StripTrailingWhitespace(actualNewString);
            }

            var uniquenessError = CheckUniqueOccurrence(normalizedContent, normalizedOld, replaceAll);
            if (uniquenessError is not null) {
                return FileEditResult.FailureResult(normalizedPath2, oldString, newString, uniquenessError);
            }

            var (updatedContent, replaceCount) = ApplyReplacement(normalizedContent, actualOldString, actualNewString, replaceAll);

            updatedContent = RestoreLineEndings(updatedContent, hasCrlf);

            await WriteFileWithLockAsync(normalizedPath2, updatedContent, cancellationToken, fileEncoding).ConfigureAwait(false);

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
        } catch (Exception ex) {
            _logger?.LogError(ex, "Edit file failed: {FilePath}", normalizedPath2);
            var diagnostic = ToolDiagnostic.Create("EditFailed",
                $"编辑文件失败: {ex.Message}",
                [new DiagnosticDetail("filePath", normalizedPath2), new DiagnosticDetail("exceptionType", ex.GetType().Name)],
                ["检查文件权限、是否被其他进程锁定。"]);
            return FileEditResult.FailureResult(normalizedPath2, oldString, newString, diagnostic);
        }
    }

    /// <summary>
    /// 通过 edit 创建新文件（old_string 为空时的分支，提取以扁平化嵌套）
    /// </summary>
    private async Task<FileEditResult> CreateNewFileViaEditAsync(
        string filePath, string oldString, string newString, CancellationToken cancellationToken) {
        if (string.IsNullOrEmpty(newString)) {
            return FileEditResult.FailureResult(filePath, oldString, newString, "old_string and new_string are both empty");
        }

        var normalizedPath = NormalizePath(filePath);
        try {
            if (_fs.FileExists(normalizedPath)) {
                var (existingContent, _) = await ReadFileWithEncodingAsync(normalizedPath, cancellationToken).ConfigureAwait(false);
                if (existingContent.Trim() != string.Empty)
                    return FileEditResult.FailureResult(normalizedPath, oldString, newString,
                        "Cannot create new file - file already exists and is not empty");
                // Empty file with empty old_string is valid - replacing empty with content
            }

            // Ensure parent directory exists
            var dir = Path.GetDirectoryName(normalizedPath);
            if (!string.IsNullOrEmpty(dir) && !_fs.DirectoryExists(dir)) {
                _fs.CreateDirectory(dir);
            }

            var normalizedNew = newString.Replace("\r\n", "\n");
            await WriteFileWithLockAsync(normalizedPath, normalizedNew, cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("File created via edit: {FilePath}", normalizedPath);

            return FileEditResult.SuccessResult(normalizedPath, oldString, newString, string.Empty, normalizedNew, 1,
                StructuredPatchGenerator.Generate(normalizedPath, string.Empty, normalizedNew, cancellationToken: cancellationToken));
        } catch (Exception ex) {
            _logger?.LogError(ex, "Create file via edit failed: {FilePath}", normalizedPath);
            var diagnostic = ToolDiagnostic.Create("EditFailed",
                $"创建文件失败: {ex.Message}",
                [new DiagnosticDetail("filePath", normalizedPath), new DiagnosticDetail("exceptionType", ex.GetType().Name)],
                ["检查文件路径是否有效、目录是否存在、是否有写入权限。"]);
            return FileEditResult.FailureResult(normalizedPath, oldString, newString, diagnostic);
        }
    }

    /// <summary>
    /// 尝试反规范化匹配 old_string（提取以扁平化嵌套）
    /// </summary>
    /// <returns>(匹配到的实际字符串, 更新后的 normalizedOld, 更新后的 normalizedNew)</returns>
    private static (string? ActualOldString, string NormalizedOld, string NormalizedNew) TryDesanitizeMatch(
        string normalizedContent, string normalizedOld, string normalizedNew) {
        var (desanitizedOld, appliedReplacements) = DesanitizeMatchString(normalizedOld);
        if (desanitizedOld == normalizedOld)
            return (null, normalizedOld, normalizedNew);

        var actualOldString = FindActualString(normalizedContent, desanitizedOld);
        if (actualOldString is null)
            return (null, normalizedOld, normalizedNew);

        foreach (var (from, to) in appliedReplacements) {
            normalizedNew = normalizedNew.Replace(from, to);
        }
        return (actualOldString, desanitizedOld, normalizedNew);
    }

    /// <summary>
    /// Edit file content by line range
    /// </summary>
    public async Task<FileLineEditResult> EditByLineRangeAsync(
        LineRangeEditRequest request,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);

        var (filePath, startLine, endLine, newContent) = (request.FilePath, request.StartLine, request.EndLine, request.NewContent);

        var rangeError = ValidateLineRange(startLine, endLine);
        if (rangeError is not null) {
            return FileLineEditResult.FailureResult(filePath, startLine, endLine, rangeError);
        }

        var normalizedPath = NormalizePath(filePath);

        try {
            if (!_fs.FileExists(normalizedPath)) {
                return FileLineEditResult.FailureResult(normalizedPath, startLine, endLine,
                    FileSuggestionHelper.BuildFileNotFoundDiagnostic(normalizedPath, _fs));
            }

            // 对齐 TS: 检测 BOM 编码
            var fileEncoding = await FileEncodingDetector.DetectFromFileAsync(normalizedPath, _fs, cancellationToken).ConfigureAwait(false);

            // Read all lines — UTF-8 用 mmap + LineSpanIndexer，其他编码走 StreamReader
            List<string> allLines;
            if (fileEncoding is UTF8Encoding) {
                var content = await _fs.ReadAllTextAsync(normalizedPath, cancellationToken).ConfigureAwait(false);
                var ranges = LineSpanIndexer.BuildLineRanges(content.AsSpan(), cancellationToken);
                allLines = new List<string>(ranges.Count);
                foreach (var (start, length) in ranges)
                    allLines.Add(content.Substring(start, length));
            } else {
                allLines = new List<string>();
                await using var stream = _fs.CreateStream(normalizedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, fileEncoding);
                string? line;
                while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
                    allLines.Add(line);
            }
            var totalLines = allLines.Count;

            if (startLine > totalLines) {
                return FileLineEditResult.FailureResult(normalizedPath, startLine, endLine, $"Start line ({startLine}) exceeds total line count ({totalLines})");
            }

            var (actualEndLine, replacedLinesCount) = ComputeActualEndLine(startLine, endLine, totalLines);
            var originalContent = ExtractOriginalContent(allLines, startLine, replacedLinesCount);
            var updatedFileContent = BuildUpdatedFileContent(allLines, startLine, actualEndLine, totalLines, newContent);

            // Write file — 保持原始编码
            await WriteFileWithLockAsync(normalizedPath, updatedFileContent, cancellationToken, fileEncoding).ConfigureAwait(false);

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
        } catch (Exception ex) {
            _logger?.LogError(ex, "Edit file by line range failed: {FilePath}", normalizedPath);
            var diagnostic = ToolDiagnostic.Create("LineEditFailed",
                $"按行范围编辑失败: {ex.Message}",
                [new DiagnosticDetail("filePath", normalizedPath), new DiagnosticDetail("exceptionType", ex.GetType().Name)],
                ["检查文件权限、是否被其他进程锁定。"]);
            return FileLineEditResult.FailureResult(normalizedPath, startLine, endLine, diagnostic);
        }
    }

    internal static int CountOccurrences(string text, string substring) {
        if (string.IsNullOrEmpty(substring) || string.IsNullOrEmpty(text))
            return 0;

        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(substring, index, StringComparison.Ordinal)) != -1) {
            count++;
            index += substring.Length;
        }

        return count;
    }

    private string NormalizePath(string path) {
        if (Path.IsPathFullyQualified(path)) {
            return _fs.GetFullPath(path);
        }

        return _fs.GetFullPath(_fs.CombinePath(_fs.GetCurrentDirectory(), path));
    }

    private async Task<(string Content, bool HasCrlf, Encoding Encoding)> ReadFileWithLineEndingDetectionAsync(string path, CancellationToken ct) {
        var timeout = IsTestEnvironment() ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(30);
        var result = await FileLockService.AcquireAsync(path, timeout, ct).ConfigureAwait(false);
        if (!result.Success)
            throw new TimeoutException($"Lock acquisition timed out: {path}");

        await using (result.GetLock()) {
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

    private async Task<(string Content, Encoding Encoding)> ReadFileWithEncodingAsync(string path, CancellationToken ct) {
        var timeout = IsTestEnvironment() ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(30);
        var result = await FileLockService.AcquireAsync(path, timeout, ct).ConfigureAwait(false);
        if (!result.Success)
            throw new TimeoutException($"Lock acquisition timed out: {path}");

        await using (result.GetLock()) {
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

    private async Task WriteFileWithLockAsync(string path, string content, CancellationToken ct, Encoding? encoding = null) {
        var timeout = IsTestEnvironment() ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(30);
        var result = await FileLockService.AcquireAsync(path, timeout, ct).ConfigureAwait(false);
        if (!result.Success)
            throw new TimeoutException($"Lock acquisition timed out: {path}");

        await using (result.GetLock()) {
            var effectiveEncoding = encoding ?? Encoding.UTF8;
            var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                await _fs.WriteAllTextAsync(tempPath, content, effectiveEncoding, ct).ConfigureAwait(false);
                _fs.MoveFile(tempPath, path, overwrite: true);
            } catch {
                if (_fs.FileExists(tempPath)) _fs.DeleteFile(tempPath);
                throw;
            }
        }
    }

    private static bool IsTestEnvironment() {
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
    internal static string? FindActualString(string fileContent, string searchString) {
        // First try exact match
        if (fileContent.Contains(searchString))
            return searchString;

        // Try with normalized quotes (curly → straight)
        var normalizedSearch = NormalizeQuotes(searchString);
        var normalizedFile = NormalizeQuotes(fileContent);

        var searchIndex = normalizedFile.IndexOf(normalizedSearch, StringComparison.Ordinal);
        if (searchIndex >= 0) {
            // Return the actual string from the file (preserving curly quotes)
            return fileContent.Substring(searchIndex, searchString.Length);
        }

        return null;
    }

    /// <summary>
    /// 剥离多行文本中每行的行号前缀（^\s*\d+[\u2192\t]）。
    /// 对齐 TS: stripLineNumberPrefix — 兼容紧凑(行号+\t)和宽(空格填充+行号+→)两种格式。
    /// 若没有任何行被剥离，返回原 text 避免无谓分配。
    /// </summary>
    internal static string StripLineNumberPrefixes(string text) {
        if (string.IsNullOrEmpty(text)) {
            return text;
        }

        var lines = text.Split('\n');
        var anyStripped = false;
        for (var i = 0; i < lines.Length; i++) {
            var stripped = StripPrefixFromLine(lines[i]);
            if (stripped.Length != lines[i].Length) {
                anyStripped = true;
                lines[i] = stripped.ToString();
            }
        }

        return anyStripped ? string.Join('\n', lines) : text;
    }

    /// <summary>
    /// 剥离单行行号前缀：^\s*\d+[\u2192\t]，返回前缀之后的内容。无前缀则原样返回。
    /// </summary>
    private static ReadOnlySpan<char> StripPrefixFromLine(ReadOnlySpan<char> line) {
        var i = 0;
        while (i < line.Length && line[i] == ' ') {
            i++;
        }

        var digitStart = i;
        while (i < line.Length && char.IsDigit(line[i])) {
            i++;
        }

        if (i == digitStart || i >= line.Length) {
            return line;
        }

        var sep = line[i];
        return sep == '\u2192' || sep == '\t' ? line.Slice(i + 1) : line;
    }

    /// <summary>
    /// Normalize curly quotes to straight quotes for matching.
    /// </summary>
    private static string NormalizeQuotes(string str) {
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
    internal static string PreserveQuoteStyle(string oldString, string actualOldString, string newString) {
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

    private static bool IsOpeningContext(ReadOnlySpan<char> chars, int index) {
        if (index == 0)
            return true;

        var prev = chars[index - 1];
        return prev is ' ' or '\t' or '\n' or '\r' or '(' or '[' or '{'
            or '\u2014'   // em dash
            or '\u2013';  // en dash
    }

    private static string ApplyCurlyDoubleQuotes(string str) {
        var chars = str.AsSpan();
        var result = new StringBuilder(str.Length);
        for (var i = 0; i < chars.Length; i++) {
            if (chars[i] == '"') {
                result.Append(IsOpeningContext(chars, i)
                    ? LeftDoubleCurlyQuote
                    : RightDoubleCurlyQuote);
            } else {
                result.Append(chars[i]);
            }
        }
        return result.ToString();
    }

    private static string ApplyCurlySingleQuotes(string str) {
        var chars = str.AsSpan();
        var result = new StringBuilder(str.Length);
        for (var i = 0; i < chars.Length; i++) {
            if (chars[i] == '\'') {
                // Don't convert apostrophes in contractions (e.g., "don't", "it's")
                var prev = i > 0 ? chars[i - 1] : '\0';
                var next = i < chars.Length - 1 ? chars[i + 1] : '\0';
                var prevIsLetter = char.IsLetter(prev);
                var nextIsLetter = char.IsLetter(next);

                if (prevIsLetter && nextIsLetter) {
                    // Apostrophe in a contraction — use right single curly quote
                    result.Append(RightSingleCurlyQuote);
                } else {
                    result.Append(IsOpeningContext(chars, i)
                        ? LeftSingleCurlyQuote
                        : RightSingleCurlyQuote);
                }
            } else {
                result.Append(chars[i]);
            }
        }
        return result.ToString();
    }

    /// <summary>
    /// Strip trailing whitespace from each line. Mirrors TS stripTrailingWhitespace.
    /// Preserves line endings (CRLF, LF, CR).
    /// </summary>
    internal static string StripTrailingWhitespace(string str) {
        if (string.IsNullOrEmpty(str))
            return str;

        // Split preserving line endings
        var result = new StringBuilder(str.Length);
        var lineStart = 0;

        for (var i = 0; i < str.Length; i++) {
            if (str[i] == '\n' || str[i] == '\r') {
                // Trim trailing whitespace from the line content
                var lineEnd = i;
                while (lineEnd > lineStart && char.IsWhiteSpace(str[lineEnd - 1]))
                    lineEnd--;

                result.Append(str, lineStart, lineEnd - lineStart);

                // Preserve the line ending
                result.Append(str[i]);
                if (str[i] == '\r' && i + 1 < str.Length && str[i + 1] == '\n') {
                    result.Append('\n');
                    i++;
                }

                lineStart = i + 1;
            }
        }

        // Handle last line (no trailing newline)
        if (lineStart < str.Length) {
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
    internal static (string Result, (string From, string To)[] AppliedReplacements) DesanitizeMatchString(string matchString) {
        var result = matchString;
        var applied = new List<(string From, string To)>();

        foreach (var (from, to) in DesanitizationMap) {
            var before = result;
            result = result.Replace(from, to);
            if (before != result) {
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

    /// <summary>
    /// 规范化三字符串用于匹配:CRLF→LF + 剥离行号前缀(old/new)。
    /// <para>纯计算:对齐 TS stripLineNumberPrefix + CRLF 规范化。content 仅做 CRLF→LF(无行号前缀)。</para>
    /// </summary>
    /// <param name="oldString">用户输入的 old_string(可能含 CRLF 和行号前缀)。</param>
    /// <param name="newString">用户输入的 new_string(可能含 CRLF 和行号前缀)。</param>
    /// <param name="originalContent">文件原始内容(仅 CRLF→LF 规范化)。</param>
    /// <returns>(规范化后的 old, 规范化后的 new, 规范化后的 content)。</returns>
    internal static (string NormalizedOld, string NormalizedNew, string NormalizedContent) NormalizeForMatch(
        string oldString, string newString, string originalContent) {
        var normalizedOld = StripLineNumberPrefixes(oldString.Replace("\r\n", "\n"));
        var normalizedNew = StripLineNumberPrefixes(newString.Replace("\r\n", "\n"));
        var normalizedContent = originalContent.Replace("\r\n", "\n");
        return (normalizedOld, normalizedNew, normalizedContent);
    }

    /// <summary>
    /// 判断路径是否为 Markdown 文件(.md/.mdx,忽略大小写)。
    /// <para>纯计算:Markdown 文件不剥离尾部空白(对齐 TS 行为)。</para>
    /// </summary>
    /// <param name="path">文件路径(已规范化)。</param>
    /// <returns>true 表示 .md 或 .mdx 文件。</returns>
    internal static bool IsMarkdownFile(string path) {
        var ext = Path.GetExtension(path);
        return ext.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mdx", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 检查 replaceAll=false 时 oldString 在 content 中是否唯一。
    /// <para>纯计算:返回错误消息(null 表示通过,非 null 表示应返回 FailureResult)。</para>
    /// </summary>
    /// <param name="content">规范化后的文件内容。</param>
    /// <param name="oldString">规范化后的 old_string(用于计数,非 actualOldString)。</param>
    /// <param name="replaceAll">是否全部替换(true 时跳过检查返回 null)。</param>
    /// <returns>null 表示通过;非 null 表示唯一性失败的错误消息。</returns>
    internal static string? CheckUniqueOccurrence(string content, string oldString, bool replaceAll) {
        if (replaceAll) return null;
        var occurrenceCount = CountOccurrences(content, oldString);
        if (occurrenceCount > 1)
            return $"old_string matched {occurrenceCount} times in the file, but replace_all is false. " +
                   "Provide more context to make old_string unique, or set replace_all to true to replace all occurrences.";
        return null;
    }

    /// <summary>
    /// 应用替换:replaceAll=true 全部替换,false 单次替换。
    /// <para>纯计算:返回 (更新后内容, 替换次数)。oldString 未找到时返回 (原内容, 0)。</para>
    /// </summary>
    /// <param name="content">规范化后的文件内容。</param>
    /// <param name="oldString">实际匹配到的 old_string(可能含 curly quotes)。</param>
    /// <param name="newString">实际应用的 new_string(已 PreserveQuoteStyle + StripTrailingWhitespace)。</param>
    /// <param name="replaceAll">是否全部替换。</param>
    /// <returns>(更新后内容, 替换次数)。</returns>
    internal static (string UpdatedContent, int ReplaceCount) ApplyReplacement(
        string content, string oldString, string newString, bool replaceAll) {
        if (replaceAll) {
            var count = CountOccurrences(content, oldString);
            return (content.Replace(oldString, newString), count);
        }
        var index = content.IndexOf(oldString, StringComparison.Ordinal);
        if (index < 0)
            return (content, 0);
        var sb = new StringBuilder(content.Length + newString.Length - oldString.Length);
        sb.Append(content, 0, index);
        sb.Append(newString);
        sb.Append(content, index + oldString.Length, content.Length - index - oldString.Length);
        return (sb.ToString(), 1);
    }

    /// <summary>
    /// 恢复行尾:hasCrlf=true 时将 LF 转回 CRLF,否则原样返回。
    /// <para>纯计算:与 NormalizeForMatch 的 CRLF→LF 规范化互逆。</para>
    /// </summary>
    /// <param name="content">规范化后的内容(LF 行尾)。</param>
    /// <param name="hasCrlf">原文件是否使用 CRLF 行尾。</param>
    /// <returns>恢复行尾后的内容。</returns>
    internal static string RestoreLineEndings(string content, bool hasCrlf) =>
        hasCrlf ? content.Replace("\n", "\r\n") : content;

    /// <summary>
    /// 验证行范围:startLine ≥ 1 且 endLine ≥ startLine。
    /// <para>纯计算:返回错误消息(null 表示通过,非 null 表示应返回 FailureResult)。</para>
    /// </summary>
    /// <param name="startLine">起始行(1-based)。</param>
    /// <param name="endLine">结束行(1-based)。</param>
    /// <returns>null 表示通过;非 null 表示验证失败的错误消息。</returns>
    internal static string? ValidateLineRange(int startLine, int endLine) {
        if (startLine < 1)
            return "Start line must be at least 1";
        if (endLine < startLine)
            return "End line must not be less than start line";
        return null;
    }

    /// <summary>
    /// 计算实际结束行(不超过总行数)和替换行数。
    /// <para>纯计算:actualEndLine = Min(endLine, totalLines),replacedLinesCount = actualEndLine - startLine + 1。</para>
    /// </summary>
    /// <param name="startLine">起始行(1-based,已验证 ≥ 1)。</param>
    /// <param name="endLine">结束行(1-based,已验证 ≥ startLine)。</param>
    /// <param name="totalLines">文件总行数。</param>
    /// <returns>(实际结束行, 替换行数)。</returns>
    internal static (int ActualEndLine, int ReplacedLinesCount) ComputeActualEndLine(int startLine, int endLine, int totalLines) {
        var actualEndLine = Math.Min(endLine, totalLines);
        var replacedLinesCount = actualEndLine - startLine + 1;
        return (actualEndLine, replacedLinesCount);
    }

    /// <summary>
    /// 提取被替换行的原始内容(用 \n join)。
    /// <para>纯计算:allLines.Skip(startLine-1).Take(replacedLinesCount) join 为字符串。</para>
    /// </summary>
    /// <param name="allLines">文件所有行。</param>
    /// <param name="startLine">起始行(1-based)。</param>
    /// <param name="replacedLinesCount">替换行数。</param>
    /// <returns>被替换行的原始内容(\n 分隔)。</returns>
    internal static string ExtractOriginalContent(List<string> allLines, int startLine, int replacedLinesCount) {
        ArgumentNullException.ThrowIfNull(allLines);
        if (startLine < 1) throw new ArgumentOutOfRangeException(nameof(startLine), startLine, "startLine must be at least 1");
        if (replacedLinesCount < 0) throw new ArgumentOutOfRangeException(nameof(replacedLinesCount), replacedLinesCount, "replacedLinesCount must be non-negative");
        if (startLine - 1 + replacedLinesCount > allLines.Count) throw new ArgumentOutOfRangeException(nameof(replacedLinesCount), replacedLinesCount, "startLine + replacedLinesCount exceeds allLines count");
        var originalLines = allLines.Skip(startLine - 1).Take(replacedLinesCount).ToList();
        return string.Join("\n", originalLines);
    }

    /// <summary>
    /// 组装更新后的文件内容:替换行前 + newContent + 替换行后,用 \n join。
    /// <para>纯计算:不依赖 IO,仅列表拼接。newContent 内的 \n 决定新行数。</para>
    /// </summary>
    /// <param name="allLines">文件所有行。</param>
    /// <param name="startLine">起始行(1-based)。</param>
    /// <param name="actualEndLine">实际结束行(已 Min(endLine, totalLines))。</param>
    /// <param name="totalLines">文件总行数。</param>
    /// <param name="newContent">新内容(可能含 \n 表示多行)。</param>
    /// <returns>更新后的完整文件内容(\n 分隔)。</returns>
    internal static string BuildUpdatedFileContent(List<string> allLines, int startLine, int actualEndLine, int totalLines, string newContent) {
        ArgumentNullException.ThrowIfNull(allLines);
        ArgumentNullException.ThrowIfNull(newContent);
        if (startLine < 1) throw new ArgumentOutOfRangeException(nameof(startLine), startLine, "startLine must be at least 1");
        if (totalLines < 0) throw new ArgumentOutOfRangeException(nameof(totalLines), totalLines, "totalLines must be non-negative");
        if (totalLines != allLines.Count) throw new ArgumentException("totalLines must equal allLines.Count", nameof(totalLines));
        if (actualEndLine < startLine) throw new ArgumentOutOfRangeException(nameof(actualEndLine), actualEndLine, "actualEndLine must be >= startLine");
        if (actualEndLine > totalLines) throw new ArgumentOutOfRangeException(nameof(actualEndLine), actualEndLine, "actualEndLine must be <= totalLines");
        var newLines = newContent.Split('\n').ToList();
        var resultLines = new List<string>();

        if (startLine > 1) {
            resultLines.AddRange(allLines.Take(startLine - 1));
        }
        resultLines.AddRange(newLines);
        if (actualEndLine < totalLines) {
            resultLines.AddRange(allLines.Skip(actualEndLine));
        }

        return string.Join("\n", resultLines);
    }

    #endregion
}