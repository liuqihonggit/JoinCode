namespace Services.SystemActuator;

internal sealed class ProcessOutputCollector : IAsyncDisposable
{
    private readonly StringBuilder _stdoutBuilder = new();
    private readonly StringBuilder _stderrBuilder = new();
    private readonly IFileSystem _fs;
    private readonly ILogger? _logger;
    private readonly string _taskId;
    private string? _spillFilePath;
    private int _isDisposed;

    private const int SpillThresholdChars = 100_000;

    internal ProcessOutputCollector(IFileSystem fs, ILogger? logger, string taskId)
    {
        _fs = fs;
        _logger = logger;
        _taskId = taskId;
    }

    internal string? SpillFilePath => _spillFilePath;

    internal void OnOutputDataReceived(string data)
    {
        if (_spillFilePath is not null)
        {
            try { _fs.AppendAllText(_spillFilePath, data + Environment.NewLine); }
            catch (Exception ex) { _logger?.LogDebug(ex, "追加溢出输出失败"); }
        }
        else
        {
            _stdoutBuilder.AppendLine(data);
            if (_stdoutBuilder.Length > SpillThresholdChars)
            {
                SpillToDisk();
            }
        }
    }

    internal void OnErrorDataReceived(string data)
    {
        _stderrBuilder.AppendLine(data);
    }

    internal void SpillToDisk()
    {
        if (_spillFilePath is not null) return;

        try
        {
            var tempDir = JoinCode.Abstractions.Configuration.AppData.AppDataConstants.UserRuntimeToolResultsDirectory;
            DirectoryHelper.EnsureDirectoryExists(_fs, tempDir);

            _spillFilePath = Path.Combine(tempDir, $"spill-{_taskId}.txt");

            if (_stdoutBuilder.Length > 0)
            {
                _fs.WriteAllText(_spillFilePath, _stdoutBuilder.ToString());
                _stdoutBuilder.Clear();
            }

            _logger?.LogDebug("任务输出已溢出到磁盘: {Path}", _spillFilePath);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "输出溢出到磁盘失败，保留内存缓冲区");
        }
    }

    internal string GetCurrentStdout()
    {
        if (_spillFilePath is not null && _fs.FileExists(_spillFilePath))
        {
            try { return _fs.ReadAllText(_spillFilePath); }
            catch { return _stdoutBuilder.ToString(); }
        }
        return _stdoutBuilder.ToString();
    }

    internal string GetCurrentStderr() => _stderrBuilder.ToString();

    internal long GetCurrentStdoutLength()
    {
        if (_spillFilePath is not null && _fs.FileExists(_spillFilePath))
        {
            return _fs.GetFileLength(_spillFilePath);
        }
        return _stdoutBuilder.Length;
    }

    internal void TruncateStdout(int startIndex)
    {
        if (_stdoutBuilder.Length > startIndex)
        {
            _stdoutBuilder.Remove(startIndex, _stdoutBuilder.Length - startIndex);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1) return ValueTask.CompletedTask;

        if (_spillFilePath is not null)
        {
            try { if (_fs.FileExists(_spillFilePath)) _fs.DeleteFile(_spillFilePath); }
            catch (Exception ex) { _logger?.LogDebug(ex, "清理溢出文件失败: {Path}", _spillFilePath); }
        }

        return ValueTask.CompletedTask;
    }
}
