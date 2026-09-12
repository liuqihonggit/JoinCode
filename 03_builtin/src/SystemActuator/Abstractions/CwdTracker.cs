namespace Services.SystemActuator;

internal sealed class CwdTracker : IAsyncDisposable
{
    private readonly IFileSystem _fs;
    private readonly ILogger? _logger;
    private readonly string? _cwdFilePath;
    private readonly string _workingDirectory;
    private int _isDisposed;

    internal CwdTracker(IFileSystem fs, ILogger? logger, string? cwdFilePath, string workingDirectory)
    {
        _fs = fs;
        _logger = logger;
        _cwdFilePath = cwdFilePath;
        _workingDirectory = workingDirectory;
    }

    internal bool TryUpdateCwdFromTrackingFile()
    {
        if (string.IsNullOrEmpty(_cwdFilePath)) return false;

        try
        {
            if (!_fs.FileExists(_cwdFilePath)) return false;

            var newCwd = _fs.ReadAllText(_cwdFilePath).Trim();
            if (string.IsNullOrEmpty(newCwd)) return false;

            try { _fs.DeleteFile(_cwdFilePath); }
            catch (Exception ex) { _logger?.LogDebug(ex, "清理 CWD 追踪文件失败: {Path}", _cwdFilePath); }

            if (!string.Equals(newCwd, _workingDirectory, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _fs.SetCurrentDirectory(newCwd);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "设置工作目录失败: {Cwd}，回退到原始目录", newCwd);
                    try { _fs.SetCurrentDirectory(_workingDirectory); }
                    catch (Exception innerEx) { _logger?.LogDebug(innerEx, "回退到原始目录也失败: {Cwd}", _workingDirectory); }
                    return false;
                }
            }

            return false;
        }
        catch (Exception ex) { _logger?.LogDebug(ex, "读取 CWD 追踪文件失败: {Path}", _cwdFilePath); return false; }
    }

    internal bool CleanupCwdTrackingFile()
    {
        if (string.IsNullOrEmpty(_cwdFilePath)) return false;

        try
        {
            if (_fs.FileExists(_cwdFilePath))
            {
                _fs.DeleteFile(_cwdFilePath);
            }
        }
        catch (Exception ex) { _logger?.LogDebug(ex, "清理 CWD 追踪文件失败: {Path}", _cwdFilePath); }

        return false;
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1) return ValueTask.CompletedTask;
        CleanupCwdTrackingFile();
        return ValueTask.CompletedTask;
    }
}
