
namespace IO.Services;

/// <summary>
/// 带限流的文件操作服务
/// 所有文件操作都经过全局 IO 限流器控制
/// </summary>
public sealed partial class ThrottledFileService : IFileOperationService, IDisposable
{
    private readonly IFileSystem _fs;
    private readonly IIOThrottleService _throttleService;
    [Inject] private readonly ILogger<ThrottledFileService>? _logger;
    private readonly ITelemetryService? _telemetryService;

    public ThrottledFileService(
        IFileSystem fs,
        IIOThrottleService throttleService,
        ILogger<ThrottledFileService>? logger = null,
        ITelemetryService? telemetryService = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _throttleService = throttleService;
        _logger = logger;
        _telemetryService = telemetryService;
    }

    /// <inheritdoc />
    public async Task<FileReadResult> ReadFileAsync(
        string filePath,
        int? offset = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        using var lease = await _throttleService.AcquireAsync(IOOperationType.Read, cancellationToken)
            .ConfigureAwait(false);

        var span = _telemetryService?.StartSpan("file.read", TelemetrySpanKind.Server);
        span?.SetTag("path", filePath);
        try
        {
            _logger?.LogDebug("Reading file: {FilePath}", filePath);

            var normalizedPath = NormalizePath(filePath);

            if (!_fs.FileExists(normalizedPath))
            {
                RecordFileMetrics(FileOperationType.Read, FileOperationResult.Failed);
                return FileReadResult.FailureResult(normalizedPath, "文件不存在");
            }

            var allLines = await _fs.ReadAllLinesAsync(normalizedPath, cancellationToken)
                .ConfigureAwait(false);
            var totalLines = allLines.Length;

            var startLine = offset ?? 0;
            var count = limit ?? totalLines;

            if (startLine < 0) startLine = 0;
            if (startLine > totalLines) startLine = totalLines;
            if (count < 0) count = 0;
            if (startLine + count > totalLines) count = totalLines - startLine;

            var selectedLines = allLines.Skip(startLine).Take(count);
            var content = string.Join(Environment.NewLine, selectedLines);

            RecordFileMetrics(FileOperationType.Read, FileOperationResult.Ok);
            return FileReadResult.SuccessResult(
                normalizedPath,
                content,
                count,
                startLine,
                totalLines);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to read file: {FilePath}", filePath);
            RecordFileMetrics(FileOperationType.Read, FileOperationResult.Failed);
            return FileReadResult.FailureResult(filePath, ex.Message);
        }
        finally
        {
            if (span is not null) await span.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<FileWriteResult> WriteFileAsync(
        string filePath,
        string content,
        CancellationToken cancellationToken = default)
    {
        using var lease = await _throttleService.AcquireAsync(IOOperationType.Write, cancellationToken)
            .ConfigureAwait(false);

        var span = _telemetryService?.StartSpan("file.write", TelemetrySpanKind.Server);
        span?.SetTag("path", filePath);
        try
        {
            _logger?.LogDebug("Writing file: {FilePath}", filePath);

            var normalizedPath = NormalizePath(filePath);
            var directory = Path.GetDirectoryName(normalizedPath);

            DirectoryHelper.EnsureDirectoryExists(_fs, directory);

            string? originalContent = null;
            var operation = _fs.FileExists(normalizedPath) ? "update" : "create";

            if (_fs.FileExists(normalizedPath))
            {
                originalContent = await _fs.ReadAllTextAsync(normalizedPath, cancellationToken)
                    .ConfigureAwait(false);
            }

            var timeout = TimeSpan.FromSeconds(30);
            var lockResult = await FileLockService.AcquireAsync(normalizedPath, timeout, cancellationToken)
                .ConfigureAwait(false);
            if (!lockResult.Success)
                throw new TimeoutException($"获取锁超时: {normalizedPath}");

            await using (lockResult.GetLock())
            {
                var tempPath = normalizedPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await _fs.WriteAllTextAsync(tempPath, content, cancellationToken)
                        .ConfigureAwait(false);
                    _fs.MoveFile(tempPath, normalizedPath, overwrite: true);
                }
                catch
                {
                    if (_fs.FileExists(tempPath)) _fs.DeleteFile(tempPath);
                    throw;
                }
            }

            RecordFileMetrics(FileOperationType.Write, FileOperationResult.Ok, operation);
            return FileWriteResult.SuccessResult(normalizedPath, content, operation, originalContent);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to write file: {FilePath}", filePath);
            RecordFileMetrics(FileOperationType.Write, FileOperationResult.Failed);
            return FileWriteResult.FailureResult(filePath, ex.Message);
        }
        finally
        {
            if (span is not null) await span.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<FileEditResult> EditFileAsync(
        string filePath,
        string oldString,
        string newString,
        bool replaceAll = false,
        CancellationToken cancellationToken = default)
    {
        using var lease = await _throttleService.AcquireAsync(IOOperationType.Write, cancellationToken)
            .ConfigureAwait(false);

        var span = _telemetryService?.StartSpan("file.edit", TelemetrySpanKind.Server);
        span?.SetTag("path", filePath);
        try
        {
            _logger?.LogDebug("Editing file: {FilePath}", filePath);

            var normalizedPath = NormalizePath(filePath);

            if (!_fs.FileExists(normalizedPath))
            {
                RecordFileMetrics(FileOperationType.Edit, FileOperationResult.Failed);
                return FileEditResult.FailureResult(normalizedPath, oldString, newString, "文件不存在");
            }

            var originalContent = await _fs.ReadAllTextAsync(normalizedPath, cancellationToken)
                .ConfigureAwait(false);

            var comparison = StringComparison.Ordinal;
            var replaceCount = 0;
            string updatedContent;

            if (replaceAll)
            {
                updatedContent = originalContent.Replace(oldString, newString, comparison);
                replaceCount = (originalContent.Length - updatedContent.Length) / (oldString.Length - newString.Length);
                if (replaceCount < 0) replaceCount = 0;
            }
            else
            {
                var index = originalContent.IndexOf(oldString, comparison);
                if (index == -1)
                {
                    RecordFileMetrics(FileOperationType.Edit, FileOperationResult.Failed);
                    return FileEditResult.FailureResult(normalizedPath, oldString, newString, "未找到匹配的字符串");
                }
                var sb = new StringBuilder(originalContent.Length - oldString.Length + newString.Length);
                sb.Append(originalContent, 0, index);
                sb.Append(newString);
                sb.Append(originalContent, index + oldString.Length, originalContent.Length - index - oldString.Length);
                updatedContent = sb.ToString();
                replaceCount = 1;
            }

            await _fs.WriteAllTextAsync(normalizedPath, updatedContent, cancellationToken)
                .ConfigureAwait(false);

            RecordFileMetrics(FileOperationType.Edit, FileOperationResult.Ok);
            return FileEditResult.SuccessResult(
                normalizedPath,
                oldString,
                newString,
                originalContent,
                updatedContent,
                replaceCount);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to edit file: {FilePath}", filePath);
            RecordFileMetrics(FileOperationType.Edit, FileOperationResult.Failed);
            return FileEditResult.FailureResult(filePath, oldString, newString, ex.Message);
        }
        finally
        {
            if (span is not null) await span.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<FileLineEditResult> EditByLineRangeAsync(
        LineRangeEditRequest request,
        CancellationToken cancellationToken = default)
    {
        using var lease = await _throttleService.AcquireAsync(IOOperationType.Write, cancellationToken)
            .ConfigureAwait(false);

        var span = _telemetryService?.StartSpan("file.edit_line_range", TelemetrySpanKind.Server);
        span?.SetTag("path", request.FilePath);
        try
        {
            _logger?.LogDebug("Editing file by line range: {FilePath}", request.FilePath);

            var normalizedPath = NormalizePath(request.FilePath);

            if (!_fs.FileExists(normalizedPath))
            {
                RecordFileMetrics(FileOperationType.EditLineRange, FileOperationResult.Failed);
                return FileLineEditResult.FailureResult(normalizedPath, request.StartLine, request.EndLine, "文件不存在");
            }

            var allLines = (await _fs.ReadAllLinesAsync(normalizedPath, cancellationToken)
                .ConfigureAwait(false)).ToList();

            var startLine = Math.Max(0, request.StartLine);
            var endLine = Math.Min(allLines.Count - 1, request.EndLine);

            if (startLine > endLine)
            {
                RecordFileMetrics(FileOperationType.EditLineRange, FileOperationResult.Failed);
                return FileLineEditResult.FailureResult(normalizedPath, request.StartLine, request.EndLine, "无效的行范围");
            }

            var originalLines = allLines.Skip(startLine).Take(endLine - startLine + 1);
            var originalContent = string.Join(Environment.NewLine, originalLines);

            allLines.RemoveRange(startLine, endLine - startLine + 1);
            var newLines = request.NewContent.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            allLines.InsertRange(startLine, newLines);

            var updatedContent = string.Join(Environment.NewLine, allLines);
            await _fs.WriteAllTextAsync(normalizedPath, updatedContent, cancellationToken)
                .ConfigureAwait(false);

            RecordFileMetrics(FileOperationType.EditLineRange, FileOperationResult.Ok);
            return FileLineEditResult.SuccessResult(
                normalizedPath,
                startLine,
                endLine,
                originalContent,
                request.NewContent,
                updatedContent,
                endLine - startLine + 1);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to edit file by line range: {FilePath}", request.FilePath);
            RecordFileMetrics(FileOperationType.EditLineRange, FileOperationResult.Failed);
            return FileLineEditResult.FailureResult(request.FilePath, request.StartLine, request.EndLine, ex.Message);
        }
        finally
        {
            if (span is not null) await span.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        using var lease = await _throttleService.AcquireAsync(IOOperationType.Delete, cancellationToken)
            .ConfigureAwait(false);

        _logger?.LogDebug("Deleting file: {FilePath}", filePath);

        try
        {
            var normalizedPath = NormalizePath(filePath);

            if (!_fs.FileExists(normalizedPath))
            {
                return false;
            }

            _fs.DeleteFile(normalizedPath);
            RecordFileMetrics(FileOperationType.Delete, FileOperationResult.Ok);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to delete file: {FilePath}", filePath);
            RecordFileMetrics(FileOperationType.Delete, FileOperationResult.Failed);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<DirectoryListResult> ListDirectoryAsync(
        string directoryPath,
        bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        using var lease = await _throttleService.AcquireAsync(IOOperationType.Read, cancellationToken)
            .ConfigureAwait(false);

        _logger?.LogDebug("Listing directory: {DirectoryPath}", directoryPath);

        try
        {
            var normalizedPath = NormalizePath(directoryPath);

            if (!_fs.DirectoryExists(normalizedPath))
            {
                RecordFileMetrics(FileOperationType.List, FileOperationResult.Failed);
                return DirectoryListResult.FailureResult(normalizedPath, "目录不存在");
            }

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            var files = _fs.EnumerateFiles(normalizedPath, "*", searchOption)
                .Select(file => new FileEntry
                {
                    Name = Path.GetFileName(file),
                    FullPath = file,
                    Size = _fs.GetFileLength(file),
                    LastModified = _fs.GetLastWriteTime(file)
                })
                .OrderBy(f => f.Name)
                .ToList();

            var directories = _fs.EnumerateDirectories(normalizedPath, "*", searchOption)
                .Select(dir => new DirectoryEntry
                {
                    Name = _fs.GetDirectoryName(dir),
                    FullPath = dir,
                    LastModified = _fs.GetDirectoryLastWriteTimeUtc(dir).ToLocalTime()
                })
                .OrderBy(d => d.Name)
                .ToList();

            RecordFileMetrics(FileOperationType.List, FileOperationResult.Ok);
            return DirectoryListResult.SuccessResult(normalizedPath, files, directories);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to list directory: {DirectoryPath}", directoryPath);
            RecordFileMetrics(FileOperationType.List, FileOperationResult.Failed);
            return DirectoryListResult.FailureResult(directoryPath, ex.Message);
        }
    }

    /// <inheritdoc />
    public bool FileExists(string filePath)
    {
        return _fs.FileExists(NormalizePath(filePath));
    }

    /// <inheritdoc />
    public Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(FileExists(filePath));
    }

    /// <inheritdoc />
    public bool DirectoryExists(string directoryPath)
    {
        return _fs.DirectoryExists(NormalizePath(directoryPath));
    }

    /// <inheritdoc />
    public Task<bool> DirectoryExistsAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(DirectoryExists(directoryPath));
    }

    /// <inheritdoc />
    public DirectoryInfo CreateDirectory(string directoryPath)
    {
        return _fs.CreateDirectory(NormalizePath(directoryPath));
    }

    /// <inheritdoc />
    public async Task<bool> CopyFileAsync(string sourcePath, string destPath, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        using var lease = await _throttleService.AcquireAsync(IOOperationType.Write, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var normalizedSource = NormalizePath(sourcePath);
            var normalizedDest = NormalizePath(destPath);

            _fs.CopyFile(normalizedSource, normalizedDest, overwrite);
            RecordFileMetrics(FileOperationType.Copy, FileOperationResult.Ok);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to copy file from {SourcePath} to {DestPath}", sourcePath, destPath);
            RecordFileMetrics(FileOperationType.Copy, FileOperationResult.Failed);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> MoveFileAsync(string sourcePath, string destPath, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        using var lease = await _throttleService.AcquireAsync(IOOperationType.Write, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var normalizedSource = NormalizePath(sourcePath);
            var normalizedDest = NormalizePath(destPath);

            if (overwrite && _fs.FileExists(normalizedDest))
            {
                _fs.DeleteFile(normalizedDest);
            }

            _fs.MoveFile(normalizedSource, normalizedDest);
            RecordFileMetrics(FileOperationType.Move, FileOperationResult.Ok);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to move file from {SourcePath} to {DestPath}", sourcePath, destPath);
            RecordFileMetrics(FileOperationType.Move, FileOperationResult.Failed);
            return false;
        }
    }

    /// <inheritdoc />
    public bool CreateSymbolicLink(string linkPath, string targetPath)
    {
        try
        {
            var normalizedLink = NormalizePath(linkPath);
            var normalizedTarget = NormalizePath(targetPath);

            File.CreateSymbolicLink(normalizedLink, normalizedTarget);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create symbolic link from {LinkPath} to {TargetPath}", linkPath, targetPath);
            return false;
        }
    }

    /// <inheritdoc />
    public DateTime GetDirectoryLastWriteTimeUtc(string directoryPath)
    {
        return _fs.GetDirectoryLastWriteTimeUtc(NormalizePath(directoryPath));
    }

    /// <inheritdoc />
    public void SetDirectoryLastWriteTimeUtc(string directoryPath, DateTime utcTime)
    {
        _fs.SetDirectoryLastWriteTimeUtc(NormalizePath(directoryPath), utcTime);
    }

    /// <inheritdoc />
    public DateTime GetFileLastWriteTime(string filePath)
    {
        return _fs.GetLastWriteTime(NormalizePath(filePath));
    }

    /// <inheritdoc />
    public Task<DateTime> GetLastWriteTimeUtcAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_fs.GetLastWriteTimeUtc(NormalizePath(filePath)));
    }

    /// <inheritdoc />
    public string GetCurrentDirectory()
    {
        return _fs.GetCurrentDirectory();
    }

    /// <inheritdoc />
    public string GetFullPath(string path)
    {
        return _fs.GetFullPath(path);
    }

    /// <inheritdoc />
    public string CombinePath(params string[] paths)
    {
        return _fs.CombinePath(paths);
    }

    /// <inheritdoc />
    public IEnumerable<string> EnumerateFiles(string directoryPath, string searchPattern, SearchOption searchOption)
    {
        return _fs.EnumerateFiles(NormalizePath(directoryPath), searchPattern, searchOption);
    }

    /// <inheritdoc />
    public IEnumerable<string> EnumerateDirectories(string directoryPath, string searchPattern, SearchOption searchOption)
    {
        return _fs.EnumerateDirectories(NormalizePath(directoryPath), searchPattern, searchOption);
    }

    /// <inheritdoc />
    public string[] GetFiles(string directoryPath, string searchPattern, SearchOption searchOption)
    {
        return _fs.GetFiles(NormalizePath(directoryPath), searchPattern, searchOption);
    }

    /// <inheritdoc />
    public string[] GetDirectories(string directoryPath, string searchPattern, SearchOption searchOption)
    {
        return _fs.GetDirectories(NormalizePath(directoryPath), searchPattern, searchOption);
    }

    /// <inheritdoc />
    public async Task<FileMetadataResult> ReadFileWithMetadataAsync(string filePath, CancellationToken cancellationToken = default)
    {
        using var lease = await _throttleService.AcquireAsync(IOOperationType.Read, cancellationToken).ConfigureAwait(false);
        var normalizedPath = NormalizePath(filePath);
        try
        {
            if (!_fs.FileExists(normalizedPath))
                return FileMetadataResult.FailureResult(normalizedPath, "File does not exist");

            var encoding = await FileEncodingDetector.DetectFromFileAsync(normalizedPath, _fs, cancellationToken).ConfigureAwait(false);
            await using var stream = _fs.CreateStream(normalizedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, encoding);
            var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            var lineEndingType = LineEndingDetector.DetectFromString(content.AsSpan());
            var lineEndings = lineEndingType == LineEndingDetector.LineEndingType.CRLF ? "CRLF" : "LF";
            var normalizedContent = content.Replace("\r\n", "\n");

            return FileMetadataResult.SuccessResult(normalizedPath, normalizedContent, encoding, lineEndings);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to read file metadata: {FilePath}", filePath);
            return FileMetadataResult.FailureResult(normalizedPath, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<FileWriteResult> WriteFileWithEncodingAsync(string filePath, string content, Encoding? encoding = null, string? lineEndings = null, CancellationToken cancellationToken = default)
    {
        using var lease = await _throttleService.AcquireAsync(IOOperationType.Write, cancellationToken).ConfigureAwait(false);
        var normalizedPath = NormalizePath(filePath);
        try
        {
            var effectiveEncoding = encoding ?? Encoding.UTF8;
            var contentToWrite = content;
            if (string.Equals(lineEndings, "CRLF", StringComparison.OrdinalIgnoreCase))
                contentToWrite = LineEndingDetector.RestoreLineEndings(content, LineEndingDetector.LineEndingType.CRLF);

            var directory = Path.GetDirectoryName(normalizedPath);
            DirectoryHelper.EnsureDirectoryExists(_fs, directory);

            var tempPath = normalizedPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await _fs.WriteAllTextAsync(tempPath, contentToWrite, effectiveEncoding, cancellationToken).ConfigureAwait(false);
                _fs.MoveFile(tempPath, normalizedPath, overwrite: true);
            }
            catch
            {
                if (_fs.FileExists(tempPath)) _fs.DeleteFile(tempPath);
                throw;
            }

            RecordFileMetrics(FileOperationType.Write, FileOperationResult.Ok, "update");
            return FileWriteResult.SuccessResult(normalizedPath, contentToWrite, "update");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to write file with encoding: {FilePath}", filePath);
            RecordFileMetrics(FileOperationType.Write, FileOperationResult.Failed);
            return FileWriteResult.FailureResult(normalizedPath, ex.Message);
        }
    }

    private void RecordFileMetrics(FileOperationType operation, FileOperationResult result, string? detail = null)
    {
        var tags = new Dictionary<string, string> { ["operation"] = operation.ToValue(), ["result"] = result.ToValue() };
        if (detail != null) tags["detail"] = detail;
        _telemetryService?.RecordCount("file.operation.count", tags, "count", "File operation count");
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("路径不能为空", nameof(path));

        return Path.GetFullPath(path);
    }

    public void Dispose()
    {
        // ThrottledFileService 不拥有 ThrottleService 的生命周期
    }
}
