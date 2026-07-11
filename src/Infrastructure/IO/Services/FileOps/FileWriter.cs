
namespace IO;

/// <summary>
/// 文件写入服务 - 提供文件写入、删除功能
/// </summary>
public sealed class FileWriter
{
    private readonly IFileSystem _fs;
    private readonly ILogger? _logger;
    private readonly FileOperationConfig _config;

    public FileWriter(IFileSystem fs, FileOperationConfig config, ILogger? logger = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
    }

    /// <summary>
    /// 写入文件内容
    /// </summary>
    public async Task<FileWriteResult> WriteFileAsync(
        string filePath,
        string content,
        CancellationToken cancellationToken = default)
    {
        if (content.Length > _config.MaxWriteSize)
        {
            return FileWriteResult.FailureResult(filePath, $"内容太大 ({content.Length} 字节, 最大 {_config.MaxWriteSize} 字节)");
        }

        var normalizedPath = NormalizePath(filePath);

        try
        {
            var operation = _fs.FileExists(normalizedPath) ? "update" : "create";
            string? originalContent = null;
            Encoding? fileEncoding = null;

            if (operation == "update")
            {
                var (existingContent, encoding) = await ReadFileWithEncodingAsync(normalizedPath, cancellationToken);
                originalContent = existingContent;
                fileEncoding = encoding;
            }

            // 确保目录存在
            var directory = Path.GetDirectoryName(normalizedPath);
            DirectoryHelper.EnsureDirectoryExists(_fs, directory);

            // 对齐 TS: writeTextContent — 保持原始编码写入
            await WriteFileWithLockAsync(normalizedPath, content, cancellationToken, fileEncoding);

            _logger?.LogInformation("文件已写入: {FilePath} (操作: {Operation})", normalizedPath, operation);

            // 生成 structuredPatch — 对齐 TS FileWriteTool: 更新时生成 patch，创建时为空
            var patch = operation == "update" && originalContent is not null
                ? StructuredPatchGenerator.Generate(normalizedPath, originalContent, content, cancellationToken: cancellationToken)
                : [];

            return FileWriteResult.SuccessResult(normalizedPath, content, operation, originalContent, patch);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "写入文件失败: {FilePath}", normalizedPath);
            return FileWriteResult.FailureResult(normalizedPath, ex.Message);
        }
    }

    /// <summary>
    /// 删除文件
    /// </summary>
    public async Task<bool> DeleteFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizePath(filePath);

        try
        {
            if (!_fs.FileExists(normalizedPath))
            {
                return false;
            }

            await DeleteFileWithLockAsync(normalizedPath, cancellationToken);
            _logger?.LogInformation("文件已删除: {FilePath}", normalizedPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "删除文件失败: {FilePath}", normalizedPath);
            return false;
        }
    }

    /// <summary>
    /// 复制文件
    /// </summary>
    public async Task<bool> CopyFileAsync(string sourcePath, string destPath, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        var normalizedSource = NormalizePath(sourcePath);
        var normalizedDest = NormalizePath(destPath);

        try
        {
            if (!_fs.FileExists(normalizedSource))
            {
                return false;
            }

            var destDir = Path.GetDirectoryName(normalizedDest);
            if (!string.IsNullOrEmpty(destDir) && !_fs.DirectoryExists(destDir))
            {
                _fs.CreateDirectory(destDir);
            }

            await CopyFileWithLockAsync(normalizedSource, normalizedDest, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "复制文件失败: {Source} -> {Dest}", normalizedSource, normalizedDest);
            return false;
        }
    }

    /// <summary>
    /// 移动文件
    /// </summary>
    public async Task<bool> MoveFileAsync(string sourcePath, string destPath, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        var normalizedSource = NormalizePath(sourcePath);
        var normalizedDest = NormalizePath(destPath);

        try
        {
            if (!_fs.FileExists(normalizedSource))
            {
                return false;
            }

            var destDir = Path.GetDirectoryName(normalizedDest);
            if (!string.IsNullOrEmpty(destDir) && !_fs.DirectoryExists(destDir))
            {
                _fs.CreateDirectory(destDir);
            }

            await MoveFileWithLockAsync(normalizedSource, normalizedDest, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "移动文件失败: {Source} -> {Dest}", normalizedSource, normalizedDest);
            return false;
        }
    }

    /// <summary>
    /// 创建符号链接
    /// </summary>
    public bool CreateSymbolicLink(string linkPath, string targetPath)
    {
        var normalizedLink = NormalizePath(linkPath);
        var normalizedTarget = NormalizePath(targetPath);

        try
        {
            if (_fs.DirectoryExists(normalizedTarget) || _fs.FileExists(normalizedTarget))
            {
                if (_fs.DirectoryExists(normalizedTarget))
                {
                    Directory.CreateSymbolicLink(normalizedLink, normalizedTarget);
                }
                else
                {
                    File.CreateSymbolicLink(normalizedLink, normalizedTarget);
                }
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "创建符号链接失败: {Link} -> {Target}", normalizedLink, normalizedTarget);
            return false;
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

    private async Task<(string Content, Encoding Encoding)> ReadFileWithEncodingAsync(string path, CancellationToken ct)
    {
        var timeout = IsTestEnvironment() ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(30);
        var result = await FileLockService.AcquireAsync(path, timeout, ct);
        if (!result.Success)
            throw new TimeoutException($"获取锁超时: {path}");

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
            throw new TimeoutException($"获取锁超时: {path}");

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

    private async Task DeleteFileWithLockAsync(string path, CancellationToken ct)
    {
        var timeout = IsTestEnvironment() ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(30);
        var result = await FileLockService.AcquireAsync(path, timeout, ct);
        if (!result.Success)
            throw new TimeoutException($"获取锁超时: {path}");

        await using (result.Lock!)
        {
            _fs.DeleteFile(path);
        }
    }

    private async Task CopyFileWithLockAsync(string source, string dest, CancellationToken ct)
    {
        var timeout = IsTestEnvironment() ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(30);
        var result = await FileLockService.AcquireBatchAsync([source, dest], timeout, ct);
        if (!result.Success)
            throw new TimeoutException($"获取锁超时");

        await using (result.Lock!)
        {
            _fs.CopyFile(source, dest, overwrite: true);
        }
    }

    private async Task MoveFileWithLockAsync(string source, string dest, CancellationToken ct)
    {
        var timeout = IsTestEnvironment() ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(30);
        var result = await FileLockService.AcquireBatchAsync([source, dest], timeout, ct);
        if (!result.Success)
            throw new TimeoutException($"获取锁超时");

        await using (result.Lock!)
        {
            _fs.MoveFile(source, dest, overwrite: true);
        }
    }

    private static bool IsTestEnvironment()
    {
        return TestEnvironmentDetector.IsTestEnvironment;
    }
}
