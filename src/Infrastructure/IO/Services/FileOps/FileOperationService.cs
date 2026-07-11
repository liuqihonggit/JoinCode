
using JoinCode.Abstractions.Attributes;

namespace IO;

/// <summary>
/// 文件操作服务实现，提供文件读写功能
/// 作为 FileReader、FileWriter、FileEditor 的外观
/// </summary>
[Register(typeof(IFileOperationService))]
public sealed partial class FileOperationService : IFileOperationService
{
    private readonly IFileSystem _fs;
    private readonly FileReader _fileReader;
    private readonly FileWriter _fileWriter;
    private readonly FileEditor _fileEditor;
    [Inject] private readonly ILogger<FileOperationService>? _logger;

    public FileOperationService(IFileSystem fs, FileOperationConfig config, ILogger<FileOperationService>? logger = null)
    {
        _fs = fs;
        _fileReader = new FileReader(fs, config, logger);
        _fileWriter = new FileWriter(fs, config, logger);
        _fileEditor = new FileEditor(fs, config, logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<FileReadResult> ReadFileAsync(
        string filePath,
        int? offset = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
        => _fileReader.ReadFileAsync(filePath, offset, limit, cancellationToken);

    /// <inheritdoc />
    public Task<FileWriteResult> WriteFileAsync(
        string filePath,
        string content,
        CancellationToken cancellationToken = default)
        => _fileWriter.WriteFileAsync(filePath, content, cancellationToken);

    /// <inheritdoc />
    public Task<FileEditResult> EditFileAsync(
        string filePath,
        string oldString,
        string newString,
        bool replaceAll = false,
        CancellationToken cancellationToken = default)
        => _fileEditor.EditFileAsync(filePath, oldString, newString, replaceAll, cancellationToken);

    /// <inheritdoc />
    public Task<FileLineEditResult> EditByLineRangeAsync(
        LineRangeEditRequest request,
        CancellationToken cancellationToken = default)
        => _fileEditor.EditByLineRangeAsync(request, cancellationToken);

    /// <inheritdoc />
    public Task<bool> DeleteFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
        => _fileWriter.DeleteFileAsync(filePath, cancellationToken);

    /// <inheritdoc />
    public async Task<DirectoryListResult> ListDirectoryAsync(
        string directoryPath,
        bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizePath(directoryPath);

        try
        {
            if (!_fs.DirectoryExists(normalizedPath))
            {
                return DirectoryListResult.FailureResult(normalizedPath, "目录不存在");
            }

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = new List<FileEntry>();
            var directories = new List<DirectoryEntry>();

            // 获取文件
            foreach (var file in _fs.EnumerateFiles(normalizedPath, "*", searchOption))
            {
                using var stream = _fs.OpenRead(file);
                files.Add(new FileEntry
                {
                    Name = Path.GetFileName(file),
                    FullPath = file,
                    Size = stream.Length,
                    LastModified = _fs.GetLastWriteTime(file)
                });
            }

            files = files.OrderBy(f => f.Name).ToList();

            // 获取目录
            foreach (var dir in _fs.EnumerateDirectories(normalizedPath, "*", searchOption))
            {
                directories.Add(new DirectoryEntry
                {
                    Name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                    FullPath = dir,
                    LastModified = _fs.GetDirectoryLastWriteTimeUtc(dir).ToLocalTime()
                });
            }

            directories = directories.OrderBy(d => d.Name).ToList();

            return DirectoryListResult.SuccessResult(normalizedPath, files, directories);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "列出目录失败: {DirectoryPath}", normalizedPath);
            return DirectoryListResult.FailureResult(normalizedPath, ex.Message);
        }
    }

    /// <inheritdoc />
    public bool FileExists(string filePath) => _fileReader.FileExists(filePath);

    /// <inheritdoc />
    public Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default)
        => _fileReader.FileExistsAsync(filePath, cancellationToken);

    /// <inheritdoc />
    public bool DirectoryExists(string directoryPath)
    {
        var normalizedPath = NormalizePath(directoryPath);
        return _fs.DirectoryExists(normalizedPath);
    }

    /// <inheritdoc />
    public Task<bool> DirectoryExistsAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizePath(directoryPath);
        return Task.FromResult(_fs.DirectoryExists(normalizedPath));
    }

    /// <inheritdoc />
    public DirectoryInfo CreateDirectory(string directoryPath)
    {
        var normalizedPath = NormalizePath(directoryPath);
        return _fs.CreateDirectory(normalizedPath);
    }

    /// <inheritdoc />
    public Task<bool> CopyFileAsync(string sourcePath, string destPath, bool overwrite = false, CancellationToken cancellationToken = default)
        => _fileWriter.CopyFileAsync(sourcePath, destPath, overwrite, cancellationToken);

    /// <inheritdoc />
    public Task<bool> MoveFileAsync(string sourcePath, string destPath, bool overwrite = false, CancellationToken cancellationToken = default)
        => _fileWriter.MoveFileAsync(sourcePath, destPath, overwrite, cancellationToken);

    /// <inheritdoc />
    public bool CreateSymbolicLink(string linkPath, string targetPath)
        => _fileWriter.CreateSymbolicLink(linkPath, targetPath);

    /// <inheritdoc />
    public DateTime GetDirectoryLastWriteTimeUtc(string directoryPath)
    {
        var normalizedPath = NormalizePath(directoryPath);
        return _fs.GetDirectoryLastWriteTimeUtc(normalizedPath);
    }

    /// <inheritdoc />
    public DateTime GetFileLastWriteTime(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);
        return _fs.GetLastWriteTime(normalizedPath);
    }

    /// <inheritdoc />
    public Task<DateTime> GetLastWriteTimeUtcAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizePath(filePath);
        return Task.FromResult(_fs.GetLastWriteTimeUtc(normalizedPath));
    }

    /// <inheritdoc />
    public string GetCurrentDirectory()
    {
        return _fs.GetCurrentDirectory();
    }

    /// <inheritdoc />
    public string GetFullPath(string path)
    {
        if (Path.IsPathFullyQualified(path))
        {
            return _fs.GetFullPath(path);
        }
        return _fs.GetFullPath(_fs.CombinePath(_fs.GetCurrentDirectory(), path));
    }

    /// <inheritdoc />
    public string CombinePath(params string[] paths)
    {
        return Path.Combine(paths);
    }

    /// <inheritdoc />
    public IEnumerable<string> EnumerateFiles(string directoryPath, string searchPattern, SearchOption searchOption)
    {
        var normalizedPath = NormalizePath(directoryPath);
        if (!_fs.DirectoryExists(normalizedPath))
        {
            return Enumerable.Empty<string>();
        }
        return _fs.EnumerateFiles(normalizedPath, searchPattern, searchOption);
    }

    /// <inheritdoc />
    public IEnumerable<string> EnumerateDirectories(string directoryPath, string searchPattern, SearchOption searchOption)
    {
        var normalizedPath = NormalizePath(directoryPath);
        if (!_fs.DirectoryExists(normalizedPath))
        {
            return Enumerable.Empty<string>();
        }
        return _fs.EnumerateDirectories(normalizedPath, searchPattern, searchOption);
    }

    /// <inheritdoc />
    public string[] GetFiles(string directoryPath, string searchPattern, SearchOption searchOption)
    {
        var normalizedPath = NormalizePath(directoryPath);
        if (!_fs.DirectoryExists(normalizedPath))
        {
            return Array.Empty<string>();
        }
        return _fs.GetFiles(normalizedPath, searchPattern, searchOption);
    }

    /// <inheritdoc />
    public string[] GetDirectories(string directoryPath, string searchPattern, SearchOption searchOption)
    {
        var normalizedPath = NormalizePath(directoryPath);
        if (!_fs.DirectoryExists(normalizedPath))
        {
            return Array.Empty<string>();
        }
        return _fs.GetDirectories(normalizedPath, searchPattern, searchOption);
    }

    /// <inheritdoc />
    public async Task<FileMetadataResult> ReadFileWithMetadataAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizePath(filePath);
        try
        {
            if (!_fs.FileExists(normalizedPath))
                return FileMetadataResult.FailureResult(normalizedPath, "File does not exist");

            // 对齐 TS: readFileSyncWithMetadata — 检测编码 + 换行符
            var encoding = await FileEncodingDetector.DetectFromFileAsync(normalizedPath, _fs, cancellationToken).ConfigureAwait(false);

            using var stream = _fs.OpenRead(normalizedPath);
            using var reader = new StreamReader(stream, encoding);
            var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            // 对齐 TS: 检测换行符（从原始内容前4096字符）
            var lineEndingType = LineEndingDetector.DetectFromString(content.AsSpan());
            var lineEndings = lineEndingType == LineEndingDetector.LineEndingType.CRLF ? "CRLF" : "LF";

            // 对齐 TS: 内容归一化为 LF
            var normalizedContent = content.Replace("\r\n", "\n");

            return FileMetadataResult.SuccessResult(normalizedPath, normalizedContent, encoding, lineEndings);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "读取文件元数据失败: {FilePath}", normalizedPath);
            return FileMetadataResult.FailureResult(normalizedPath, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<FileWriteResult> WriteFileWithEncodingAsync(
        string filePath,
        string content,
        Encoding? encoding = null,
        string? lineEndings = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizePath(filePath);
        try
        {
            var effectiveEncoding = encoding ?? Encoding.UTF8;

            // 对齐 TS: writeTextContent — 恢复换行符
            var contentToWrite = content;
            if (string.Equals(lineEndings, "CRLF", StringComparison.OrdinalIgnoreCase))
            {
                var lineEndingType = LineEndingDetector.LineEndingType.CRLF;
                contentToWrite = LineEndingDetector.RestoreLineEndings(content, lineEndingType);
            }

            // 确保目录存在
            var directory = Path.GetDirectoryName(normalizedPath);
            if (!string.IsNullOrEmpty(directory) && !_fs.DirectoryExists(directory))
                _fs.CreateDirectory(directory);

            // 原子写入（临时文件 + 重命名）
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

            var operation = "update";
            return FileWriteResult.SuccessResult(normalizedPath, contentToWrite, operation);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "写入文件失败(带编码): {FilePath}", normalizedPath);
            return FileWriteResult.FailureResult(normalizedPath, ex.Message);
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
}
