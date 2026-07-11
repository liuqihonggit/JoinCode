
namespace Testing.Common.Services;

/// <summary>
/// 基于内存文件系统的文件操作服务 - 用于高速测试
/// </summary>
public sealed class InMemoryFileOperationService : IFileOperationService, IDisposable
{
    private readonly InMemoryFileSystem _fileSystem;
    private readonly ILogger<InMemoryFileOperationService>? _logger;
    private string _currentDirectory = "/test";

    public InMemoryFileOperationService(InMemoryFileSystem? fileSystem = null, ILogger<InMemoryFileOperationService>? logger = null)
    {
        _fileSystem = fileSystem ?? new InMemoryFileSystem();
        _logger = logger;
        // 确保当前目录存在
        _fileSystem.CreateDirectory(_currentDirectory);
    }

    public InMemoryFileSystem FileSystem => _fileSystem;

    public Task<FileReadResult> ReadFileAsync(string filePath, int? offset = null, int? limit = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_fileSystem.FileExists(filePath))
            {
                return Task.FromResult(FileReadResult.FailureResult(filePath, "文件不存在"));
            }

            var allLines = _fileSystem.ReadAllLines(filePath);
            var totalLines = allLines.Length;

            if (offset.HasValue || limit.HasValue)
            {
                var skip = offset ?? 0;
                var take = limit ?? totalLines;
                var lines = allLines.Skip(skip).Take(take).ToArray();
                var content = string.Join("\n", lines);
                return Task.FromResult(FileReadResult.SuccessResult(filePath, content, lines.Length, skip + 1, totalLines));
            }

            var fullContent = string.Join("\n", allLines);
            return Task.FromResult(FileReadResult.SuccessResult(filePath, fullContent, totalLines, 1, totalLines));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "读取文件失败: {FilePath}", filePath);
            return Task.FromResult(FileReadResult.FailureResult(filePath, ex.Message));
        }
    }

    public Task<FileWriteResult> WriteFileAsync(string filePath, string content, CancellationToken cancellationToken = default)
    {
        try
        {
            var exists = _fileSystem.FileExists(filePath);
            string? originalContent = null;

            if (exists)
            {
                originalContent = _fileSystem.ReadAllText(filePath);
            }

            _fileSystem.WriteAllText(filePath, content);

            return Task.FromResult(FileWriteResult.SuccessResult(filePath, content, exists ? "update" : "create", originalContent));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "写入文件失败: {FilePath}", filePath);
            return Task.FromResult(FileWriteResult.FailureResult(filePath, ex.Message));
        }
    }

    public Task<FileEditResult> EditFileAsync(string filePath, string oldString, string newString, bool replaceAll = false, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_fileSystem.FileExists(filePath))
            {
                return Task.FromResult(FileEditResult.FailureResult(filePath, oldString, newString, "文件不存在"));
            }

            var content = _fileSystem.ReadAllText(filePath);
            var originalContent = content;
            int replaceCount = 0;

            if (replaceAll)
            {
                replaceCount = content.Split(oldString).Length - 1;
                content = content.Replace(oldString, newString);
            }
            else
            {
                var index = content.IndexOf(oldString);
                if (index >= 0)
                {
                    content = content.Substring(0, index) + newString + content.Substring(index + oldString.Length);
                    replaceCount = 1;
                }
            }

            if (replaceCount == 0)
            {
                return Task.FromResult(FileEditResult.FailureResult(filePath, oldString, newString, $"未找到要替换的字符串: {oldString}"));
            }

            _fileSystem.WriteAllText(filePath, content);

            return Task.FromResult(FileEditResult.SuccessResult(filePath, oldString, newString, originalContent, content, replaceCount));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "编辑文件失败: {FilePath}", filePath);
            return Task.FromResult(FileEditResult.FailureResult(filePath, oldString, newString, ex.Message));
        }
    }

    public Task<FileLineEditResult> EditByLineRangeAsync(LineRangeEditRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        try
        {

            if (!_fileSystem.FileExists(request.FilePath))
            {
                return Task.FromResult(FileLineEditResult.FailureResult(request.FilePath, request.StartLine, request.EndLine, "文件不存在"));
            }

            var lines = _fileSystem.ReadAllLines(request.FilePath).ToList();
            var totalLines = lines.Count;

            if (request.StartLine < 1)
            {
                return Task.FromResult(FileLineEditResult.FailureResult(request.FilePath, request.StartLine, request.EndLine, "起始行号必须大于等于1"));
            }

            if (request.EndLine < request.StartLine)
            {
                return Task.FromResult(FileLineEditResult.FailureResult(request.FilePath, request.StartLine, request.EndLine, "结束行号不能小于起始行号"));
            }

            if (request.StartLine > totalLines)
            {
                return Task.FromResult(FileLineEditResult.FailureResult(request.FilePath, request.StartLine, request.EndLine, $"起始行号 {request.StartLine} 超出文件总行数 {totalLines}"));
            }

            var startIndex = request.StartLine - 1;
            var endIndex = Math.Min(request.EndLine - 1, totalLines - 1);
            var originalLines = lines.Skip(startIndex).Take(endIndex - startIndex + 1).ToList();
            var originalContent = string.Join("\n", originalLines);

            // 移除旧行
            lines.RemoveRange(startIndex, endIndex - startIndex + 1);

            // 插入新内容
            var newLines = request.NewContent.Split('\n').ToList();
            lines.InsertRange(startIndex, newLines);

            var newContent = string.Join("\n", lines);
            var updatedFileContent = newContent;
            _fileSystem.WriteAllText(request.FilePath, newContent);

            return Task.FromResult(FileLineEditResult.SuccessResult(
                request.FilePath,
                request.StartLine,
                endIndex + 1,
                originalContent,
                request.NewContent,
                updatedFileContent,
                originalLines.Count));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "按行范围编辑文件失败: {FilePath}", request?.FilePath);
            return Task.FromResult(FileLineEditResult.FailureResult(
                request?.FilePath ?? string.Empty,
                request?.StartLine ?? 0,
                request?.EndLine ?? 0,
                ex.Message));
        }
    }

    public Task<bool> DeleteFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_fileSystem.DeleteFile(filePath));
    }

    public Task<DirectoryListResult> ListDirectoryAsync(string directoryPath, bool recursive = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(directoryPath);
        try
        {
            if (!_fileSystem.DirectoryExists(directoryPath))
            {
                return Task.FromResult(DirectoryListResult.FailureResult(directoryPath, "目录不存在"));
            }

            // 使用 recursive 参数控制搜索范围
            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = _fileSystem.EnumerateFiles(directoryPath, "*", searchOption)
                .Select(path => _fileSystem.GetFileInfo(path)!)
                .Select(fileInfo => new FileEntry
                {
                    Name = Path.GetFileName(fileInfo.FullPath),
                    FullPath = fileInfo.FullPath,
                    Size = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTime
                })
                .OrderBy(f => f.Name)
                .ToList();

            var directories = _fileSystem.EnumerateDirectories(directoryPath, "*", searchOption)
                .Select(path => new DirectoryEntry
                {
                    Name = Path.GetFileName(path),
                    FullPath = path,
                    LastModified = DateTime.Now
                })
                .OrderBy(d => d.Name)
                .ToList();

            return Task.FromResult(DirectoryListResult.SuccessResult(directoryPath, files, directories));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "列出目录失败: {DirectoryPath}", directoryPath);
            return Task.FromResult(DirectoryListResult.FailureResult(directoryPath, ex.Message));
        }
    }

    public bool FileExists(string filePath)
    {
        return _fileSystem.FileExists(filePath);
    }

    public Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_fileSystem.FileExists(filePath));
    }

    public bool DirectoryExists(string directoryPath)
    {
        return _fileSystem.DirectoryExists(directoryPath);
    }

    public Task<bool> DirectoryExistsAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_fileSystem.DirectoryExists(directoryPath));
    }

    public DirectoryInfo CreateDirectory(string directoryPath)
    {
        _fileSystem.CreateDirectory(directoryPath);
        // 返回一个模拟的 DirectoryInfo
        return new DirectoryInfo(directoryPath);
    }

    public Task<bool> CopyFileAsync(string sourcePath, string destPath, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_fileSystem.FileExists(sourcePath))
            {
                return Task.FromResult(false);
            }

            if (_fileSystem.FileExists(destPath) && !overwrite)
            {
                return Task.FromResult(false);
            }

            var content = _fileSystem.ReadAllText(sourcePath);
            _fileSystem.WriteAllText(destPath, content);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<bool> MoveFileAsync(string sourcePath, string destPath, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        ArgumentException.ThrowIfNullOrEmpty(destPath);
        try
        {
            if (!_fileSystem.FileExists(sourcePath))
            {
                return Task.FromResult(false);
            }

            if (_fileSystem.FileExists(destPath) && !overwrite)
            {
                return Task.FromResult(false);
            }

            var content = _fileSystem.ReadAllText(sourcePath);
            _fileSystem.WriteAllText(destPath, content);
            _fileSystem.DeleteFile(sourcePath);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public bool CreateSymbolicLink(string linkPath, string targetPath)
    {
        // 内存文件系统不支持符号链接，直接复制内容
        try
        {
            if (!_fileSystem.FileExists(targetPath))
            {
                return false;
            }

            var content = _fileSystem.ReadAllText(targetPath);
            _fileSystem.WriteAllText(linkPath, content);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public DateTime GetDirectoryLastWriteTimeUtc(string directoryPath)
    {
        return DateTime.UtcNow;
    }

    public DateTime GetFileLastWriteTime(string filePath)
    {
        return _fileSystem.GetLastWriteTime(filePath);
    }

    public Task<DateTime> GetLastWriteTimeUtcAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_fileSystem.GetLastWriteTime(filePath).ToUniversalTime());
    }

    public string GetCurrentDirectory()
    {
        return _currentDirectory;
    }

    public string GetFullPath(string path)
    {
        if (Path.IsPathFullyQualified(path))
        {
            return path;
        }
        // 对于相对路径，基于内存文件系统的当前目录
        return Path.Combine(_currentDirectory, path).Replace('\\', '/');
    }

    public string CombinePath(params string[] paths)
    {
        return Path.Combine(paths).Replace('\\', '/');
    }

    public IEnumerable<string> EnumerateFiles(string directoryPath, string searchPattern, SearchOption searchOption)
    {
        return _fileSystem.EnumerateFiles(directoryPath, searchPattern, searchOption);
    }

    public IEnumerable<string> EnumerateDirectories(string directoryPath, string searchPattern, SearchOption searchOption)
    {
        return _fileSystem.EnumerateDirectories(directoryPath, searchPattern, searchOption);
    }

    public string[] GetFiles(string directoryPath, string searchPattern, SearchOption searchOption)
    {
        return _fileSystem.EnumerateFiles(directoryPath, searchPattern, searchOption).ToArray();
    }

    public string[] GetDirectories(string directoryPath, string searchPattern, SearchOption searchOption)
    {
        return _fileSystem.EnumerateDirectories(directoryPath, searchPattern, searchOption).ToArray();
    }

    /// <summary>
    /// 内存文件系统不支持编码检测，返回默认 UTF-8 + LF
    /// </summary>
    public Task<FileMetadataResult> ReadFileWithMetadataAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!_fileSystem.FileExists(filePath))
            return Task.FromResult(FileMetadataResult.FailureResult(filePath, "文件不存在"));

        var content = _fileSystem.ReadAllText(filePath);
        return Task.FromResult(FileMetadataResult.SuccessResult(filePath, content, System.Text.Encoding.UTF8, "LF"));
    }

    /// <summary>
    /// 内存文件系统写入时忽略编码和换行符（测试环境不需要保持）
    /// </summary>
    public Task<FileWriteResult> WriteFileWithEncodingAsync(string filePath, string content, System.Text.Encoding? encoding = null, string? lineEndings = null, CancellationToken cancellationToken = default)
        => WriteFileAsync(filePath, content, cancellationToken);

    public void Dispose()
    {
        _fileSystem.Clear();
    }
}
