
namespace IO;

/// <summary>
/// 文件读取服务 - 提供文件读取功能
/// </summary>
public sealed class FileReader
{
    private readonly IFileSystem _fs;
    private readonly ILogger? _logger;
    private readonly FileOperationConfig _config;

    public FileReader(IFileSystem fs, FileOperationConfig config, ILogger? logger = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<FileReadResult> ReadFileAsync(
        string filePath,
        int? offset = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizePath(filePath);

        try
        {
            if (_fs.DirectoryExists(normalizedPath))
            {
                var dirDiagnostic = ToolDiagnostic.Create(
                    "IsDirectoryNotFile",
                    $"Cannot read '{normalizedPath}': it is a directory, not a file.",
                    [new DiagnosticDetail("filePath", normalizedPath), new DiagnosticDetail("type", "directory")],
                    [$"使用 {FileToolNameConstants.DirectoryList} 工具列出目录内容，或指定一个文件路径。"]);
                return FileReadResult.FailureResult(normalizedPath, dirDiagnostic);
            }

            if (!_fs.FileExists(normalizedPath))
            {
                var diagnostic = FileSuggestionHelper.BuildFileNotFoundDiagnostic(normalizedPath, _fs);
                return FileReadResult.FailureResult(normalizedPath, diagnostic);
            }

            var fileLength = _fs.GetFileLength(normalizedPath);
            if (fileLength > _config.MaxReadSize)
            {
                var sizeDiagnostic = ToolDiagnostic.Create(
                    "FileTooLarge",
                    $"File content ({fileLength} bytes) exceeds maximum allowed size ({_config.MaxReadSize} bytes).",
                    [
                        new DiagnosticDetail("filePath", normalizedPath),
                        new DiagnosticDetail("fileSize", fileLength.ToString()),
                        new DiagnosticDetail("maxSize", _config.MaxReadSize.ToString()),
                    ],
                    ["使用 offset 和 limit 参数读取文件的部分内容。", $"使用 {SearchToolNameConstants.Grep} 工具搜索特定内容而非读取整个文件。"]);
                return FileReadResult.FailureResult(normalizedPath, sizeDiagnostic);
            }

            var (isBinary, binaryReason) = await IsBinaryFileAsync(normalizedPath, cancellationToken).ConfigureAwait(false);
            if (isBinary)
            {
                var binaryDiagnostic = ToolDiagnostic.Create(
                    "BinaryFileDetected",
                    binaryReason,
                    [new DiagnosticDetail("filePath", normalizedPath)],
                    [$"使用适当的工具分析二进制文件（如 {FileToolNameConstants.FileRead} 读取图片、{FileToolNameConstants.FileRead} 读取 PDF）。"]);
                return FileReadResult.FailureResult(normalizedPath, binaryDiagnostic);
            }

            var (selectedContent, numLines, startLine, totalLines) = await ReadFileRangeAsync(
                normalizedPath, offset, limit, cancellationToken).ConfigureAwait(false);

            return FileReadResult.SuccessResult(
                normalizedPath,
                selectedContent,
                numLines,
                startLine,
                totalLines);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to read file: {FilePath}", normalizedPath);
            var exDiagnostic = ToolDiagnostic.Create(
                "ReadFailed",
                $"读取文件失败: {ex.Message}",
                [
                    new DiagnosticDetail("filePath", normalizedPath),
                    new DiagnosticDetail("exceptionType", ex.GetType().Name),
                ],
                ["检查文件权限、是否被其他进程锁定。"]);
            return FileReadResult.FailureResult(normalizedPath, exDiagnostic);
        }
    }

    private async Task<(string Content, int NumLines, int StartLine, int TotalLines)> ReadFileRangeAsync(
        string filePath,
        int? offset,
        int? limit,
        CancellationToken cancellationToken)
    {
        var startIndex = offset ?? 0;
        if (startIndex < 0) startIndex = 0;

        var lines = new List<string>();
        int totalLines = 0;
        int linesToSkip = startIndex;
        int? linesToTake = limit;
        bool isFirstLine = true;

        using var stream = _fs.CreateStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        // 接入编码检测 — 对齐 FileOperationService.ReadFileWithMetadataAsync
        var encoding = await FileEncodingDetector.DetectFromFileAsync(filePath, _fs, cancellationToken, _logger).ConfigureAwait(false);
        using var reader = new StreamReader(stream, encoding);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
        {
            totalLines++;

            // Strip BOM from first line
            if (isFirstLine && line.Length > 0 && line[0] == '\uFEFF')
            {
                line = line[1..];
            }
            isFirstLine = false;

            // Trim trailing \r (CRLF normalization)
            line = line.TrimEnd('\r');

            // Skip lines before offset
            if (linesToSkip > 0)
            {
                linesToSkip--;
                continue;
            }

            // Collect needed lines
            if (linesToTake.HasValue)
            {
                if (linesToTake.Value > 0)
                {
                    lines.Add(line);
                    linesToTake--;
                }
            }
            else
            {
                lines.Add(line);
            }
        }

        // 重新计算实际的行号范围
        var actualStartLine = Math.Min(startIndex, totalLines);
        var actualEndLine = limit.HasValue
            ? Math.Min(actualStartLine + limit.Value, totalLines)
            : totalLines;
        var actualNumLines = actualEndLine - actualStartLine;

        // 如果超出了实际行数，调整结果
        if (lines.Count > actualNumLines)
        {
            lines = lines.Take(actualNumLines).ToList();
        }

        var content = string.Join("\n", lines);
        return (content, lines.Count, actualStartLine + 1, totalLines);
    }

    /// <inheritdoc />
    public bool FileExists(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);
        return _fs.FileExists(normalizedPath);
    }

    /// <summary>
    /// 异步检查文件是否存在
    /// </summary>
    public Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizePath(filePath);
        return Task.FromResult(File.Exists(normalizedPath));
    }

    /// <summary>
    /// 检测文件是否为二进制文件。
    /// 返回 (isBinary, reason) — reason 描述检测结论或失败原因。
    /// 不再吞异常：IO 失败时返回 (false, reason) 让上层报告真正的 IO 错误，而非误报为二进制。
    /// </summary>
    private async Task<(bool IsBinary, string Reason)> IsBinaryFileAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = _fs.CreateStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[_config.BinaryDetectionBufferSize];
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

            if (bytesRead == 0) return (false, "Empty file");

            int nonPrintableCount = 0;
            for (var i = 0; i < bytesRead; i++)
            {
                var b = buffer[i];
                // Null byte is always binary
                if (b == 0) return (true, $"Null byte detected at offset {i} (checked {bytesRead} bytes).");
                // Count non-printable characters (excluding common whitespace: TAB=9, LF=10, CR=13)
                if (b < 0x20 && b is not (9 or 10 or 13))
                {
                    nonPrintableCount++;
                }
            }

            // If more than 10% non-printable characters, treat as binary
            if (nonPrintableCount > bytesRead / 10)
            {
                var ratio = (double)nonPrintableCount / bytesRead * 100;
                return (true, $"High non-printable ratio: {ratio:F1}% ({nonPrintableCount}/{bytesRead} bytes in first {bytesRead} bytes).");
            }

            return (false, $"Text file (0 non-printable bytes in first {bytesRead} bytes).");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 不再吞异常误报为二进制 — 让上层报告真正的 IO 错误
            _logger?.LogWarning(ex, "二进制检测读取失败，跳过检测: {FilePath}", filePath);
            return (false, $"Binary detection skipped due to IO error: {ex.Message}");
        }
    }

    private string NormalizePath(string path)
    {
        if (Path.IsPathFullyQualified(path))
        {
            return _fs.GetFullPath(path);
        }

        return _fs.GetFullPath(_fs.CombinePath(_fs.GetCurrentDirectory(), path));
    }

    /// <summary>
    /// Find a file with the same base name but different extension in the same directory.
    /// </summary>
    private string? FindSimilarFile(string filePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(dir) || !_fs.DirectoryExists(dir))
                return null;

            var fileBaseName = Path.GetFileNameWithoutExtension(filePath);
            var extension = Path.GetExtension(filePath);

            foreach (var file in _fs.EnumerateFiles(dir, $"{fileBaseName}*", SearchOption.TopDirectoryOnly))
            {
                if (string.Equals(file, filePath, StringComparison.OrdinalIgnoreCase))
                    continue;

                var fileExt = Path.GetExtension(file);
                if (!string.Equals(fileExt, extension, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFileName(file);
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Suggest a corrected path under the current working directory when a file
    /// is not found. Detects the "dropped repo folder" pattern where the path
    /// is missing the repo directory component.
    /// </summary>
    private string? SuggestPathUnderCwd(string requestedPath)
    {
        try
        {
            var cwd = _fs.GetCurrentDirectory();
            var cwdParent = Path.GetDirectoryName(cwd);
            if (string.IsNullOrEmpty(cwdParent))
                return null;

            // Only check if the requested path is under cwd's parent but not under cwd itself
            var cwdParentPrefix = cwdParent.EndsWith(Path.DirectorySeparatorChar)
                ? cwdParent
                : cwdParent + Path.DirectorySeparatorChar;

            if (!requestedPath.StartsWith(cwdParentPrefix, StringComparison.OrdinalIgnoreCase) ||
                requestedPath.StartsWith(cwd + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(requestedPath, cwd, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Get the relative path from the parent directory
            var relFromParent = Path.GetRelativePath(cwdParent, requestedPath);

            // Check if the same relative path exists under cwd
            var correctedPath = Path.GetFullPath(Path.Combine(cwd, relFromParent));
            if (_fs.FileExists(correctedPath))
            {
                return correctedPath;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
