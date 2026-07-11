
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
                return FileReadResult.FailureResult(normalizedPath, $"Cannot read '{normalizedPath}': it is a directory, not a file");
            }

            if (!_fs.FileExists(normalizedPath))
            {
                var suggestion = FindSimilarFile(normalizedPath) ?? SuggestPathUnderCwd(normalizedPath);
                var message = $"File does not exist. Note: your current working directory is {_fs.GetCurrentDirectory()}.";
                if (suggestion is not null)
                {
                    message += $" Did you mean {suggestion}?";
                }
                return FileReadResult.FailureResult(normalizedPath, message);
            }

            var fileLength = _fs.GetFileLength(normalizedPath);
            if (fileLength > _config.MaxReadSize)
            {
                return FileReadResult.FailureResult(normalizedPath,
                    $"File content ({fileLength} bytes) exceeds maximum allowed size ({_config.MaxReadSize} bytes). Use offset and limit parameters to read specific portions of the file.");
            }

            if (await IsBinaryFileAsync(normalizedPath, cancellationToken).ConfigureAwait(false))
            {
                return FileReadResult.FailureResult(normalizedPath, "Cannot read binary file. The file appears to be a binary file.");
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
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to read file: {FilePath}", normalizedPath);
            return FileReadResult.FailureResult(normalizedPath, ex.Message);
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
        using var reader = new StreamReader(stream, Encoding.UTF8);

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

    private async Task<bool> IsBinaryFileAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = _fs.CreateStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[_config.BinaryDetectionBufferSize];
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

            if (bytesRead == 0) return false;

            int nonPrintableCount = 0;
            for (var i = 0; i < bytesRead; i++)
            {
                var b = buffer[i];
                // Null byte is always binary
                if (b == 0) return true;
                // Count non-printable characters (excluding common whitespace: TAB=9, LF=10, CR=13)
                if (b < 0x20 && b is not (9 or 10 or 13))
                {
                    nonPrintableCount++;
                }
            }

            // If more than 10% non-printable characters, treat as binary
            return nonPrintableCount > bytesRead / 10;
        }
        catch
        {
            return true;
        }
    }

    private string NormalizePath(string path)
    {
        if (Path.IsPathFullyQualified(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(_fs.GetCurrentDirectory(), path));
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
