
namespace Core.Scheduling;

/// <summary>
/// 高水位标记管理器 - 管理任务ID的单调递增
/// 使用 FileLock 进行文件级并发保护
/// </summary>
public sealed class HighWaterMarkManager
{
    private readonly IFileSystem _fs;
    private readonly string _highWaterMarkPath;

    public HighWaterMarkManager(IFileSystem fs, TaskDirectoryOptions options)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _highWaterMarkPath = options.GetHighWaterMarkPath();
    }

    /// <summary>
    /// 读取当前高水位标记值
    /// </summary>
    public async Task<int> ReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var content = await ReadFileWithLockAsync(_highWaterMarkPath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content))
            {
                return 0;
            }

            if (int.TryParse(content.Trim(), out var value))
            {
                return value;
            }
            return 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// 更新高水位标记值（原子操作）
    /// </summary>
    /// <param name="newValue">新值</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task UpdateAsync(int newValue, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_highWaterMarkPath);
        if (!string.IsNullOrEmpty(directory) && !_fs.DirectoryExists(directory))
        {
            _fs.CreateDirectory(directory);
        }

        await WriteFileWithLockAsync(_highWaterMarkPath, newValue.ToString(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 原子递增高水位标记并返回新值
    /// 在同一个文件锁内完成读-改-写，避免竞态条件
    /// </summary>
    public async Task<int> IncrementAndGetAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_highWaterMarkPath);
        DirectoryHelper.EnsureDirectoryExists(_fs, directory);

        var timeout = TimeSpan.FromSeconds(5);
        var result = await FileLockService.AcquireAsync(_highWaterMarkPath, timeout, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
            throw new TimeoutException(L.T(StringKey.LockAcquireTimeout, _highWaterMarkPath));

        await using (result.GetLock())
        {
            // 在同一个锁内完成读-改-写，保证原子性
            var content = string.Empty;
            if (_fs.FileExists(_highWaterMarkPath))
            {
                content = await _fs.ReadAllTextAsync(_highWaterMarkPath, cancellationToken).ConfigureAwait(false);
            }

            var current = 0;
            if (!string.IsNullOrWhiteSpace(content))
            {
                int.TryParse(content.Trim(), out current);
            }

            var newValue = current + 1;

            var tempPath = _highWaterMarkPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await _fs.WriteAllTextAsync(tempPath, newValue.ToString(), cancellationToken).ConfigureAwait(false);
                _fs.MoveFile(tempPath, _highWaterMarkPath, overwrite: true);
            }
            catch
            {
                if (_fs.FileExists(tempPath)) _fs.DeleteFile(tempPath);
                throw;
            }

            return newValue;
        }
    }

    /// <summary>
    /// 从高水位标记生成任务ID字符串
    /// </summary>
    public static string GenerateTaskId(int value)
    {
        return value.ToString("D4");
    }

    /// <summary>
    /// 从任务ID解析整数值
    /// </summary>
    public static int ParseTaskId(string taskId)
    {
        if (int.TryParse(taskId, out var value))
        {
            return value;
        }
        return 0;
    }

    private async Task<string> ReadFileWithLockAsync(string path, CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(5);
        var result = await FileLockService.AcquireAsync(path, timeout, ct).ConfigureAwait(false);
        if (!result.Success)
            return string.Empty;

        await using (result.GetLock())
        {
            if (!_fs.FileExists(path))
                return string.Empty;

            return await _fs.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        }
    }

    private async Task WriteFileWithLockAsync(string path, string content, CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(5);
        var result = await FileLockService.AcquireAsync(path, timeout, ct).ConfigureAwait(false);
        if (!result.Success)
            throw new TimeoutException(L.T(StringKey.LockAcquireTimeout, path));

        await using (result.GetLock())
        {
            var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await _fs.WriteAllTextAsync(tempPath, content, ct).ConfigureAwait(false);
                _fs.MoveFile(tempPath, path, overwrite: true);
            }
            catch
            {
                if (_fs.FileExists(tempPath)) _fs.DeleteFile(tempPath);
                throw;
            }
        }
    }
}
