using JoinCode.Abstractions.Attributes;

namespace JoinCode.CodeIndex;

[Register]
public sealed partial class FileWatcherIntegration : IAsyncDisposable, IDisposable
{
    private readonly ICodeIndexer _indexer;
    private readonly IFileSystem _fs;
    private readonly string _workspaceRoot;
    private readonly HashSet<string> _excludedDirs;
    private readonly TimeSpan _debounceInterval;
    private readonly Action<Exception>? _onError;
    private readonly List<Task> _pendingUpdates = [];
    private readonly SemaphoreSlim _pendingLock = new(1, 1);
    private CancellationTokenSource? _updateCts;
    private IFileSystemWatcher? _watcher;
    private int _disposed;

    private static readonly string[] DefaultExcludedDirs = ["bin", "obj", ".git", ".x"];

    private static void Log(string message)
    {
        System.Diagnostics.Trace.WriteLine(message);
    }

    public FileWatcherIntegration(ICodeIndexer indexer, string workspaceRoot, TimeSpan? debounceInterval = null)
        : this(indexer, workspaceRoot, null!, null, debounceInterval)
    {
    }

    public FileWatcherIntegration(ICodeIndexer indexer, string workspaceRoot, Action<Exception>? onError, TimeSpan? debounceInterval = null)
        : this(indexer, workspaceRoot, null!, onError, debounceInterval)
    {
    }

    public FileWatcherIntegration(ICodeIndexer indexer, string workspaceRoot, IFileSystem fs, Action<Exception>? onError, TimeSpan? debounceInterval = null)
    {
        ArgumentNullException.ThrowIfNull(indexer);
        ArgumentNullException.ThrowIfNull(workspaceRoot);

        _indexer = indexer;
        _fs = fs;
        _workspaceRoot = workspaceRoot;
        _onError = onError;
        _debounceInterval = debounceInterval ?? TimeSpan.FromMilliseconds(500);
        _excludedDirs = new HashSet<string>(DefaultExcludedDirs, StringComparer.OrdinalIgnoreCase);
        _updateCts = new CancellationTokenSource();
    }

    /// <summary>
    /// DI 构造函数 — 从 CodeIndexOptions 获取 workspaceRoot，注入 IFileSystem
    /// </summary>
    public FileWatcherIntegration(ICodeIndexer indexer, CodeIndexOptions options, IFileSystem fs)
        : this(indexer, options.WorkspaceRoot, fs, null)
    {
    }

    public Task StartAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        ArgumentNullException.ThrowIfNull(_fs);

        _watcher = _fs.Watch(_workspaceRoot, "*.cs");
        _watcher.IncludeSubdirectories = true;
        _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
        _watcher.DebounceInterval = _debounceInterval;

        _watcher.DebouncedCreated += OnFileChanged;
        _watcher.DebouncedChanged += OnFileChanged;
        _watcher.DebouncedDeleted += OnFileDeleted;
        _watcher.DebouncedRenamed += OnFileRenamed;

        _watcher.EnableRaisingEvents = true;

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        _updateCts?.Cancel();

        Task[] pending;
        await _pendingLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            pending = [.. _pendingUpdates];
        }
        finally
        {
            _pendingLock.Release();
        }

        if (pending.Length > 0)
        {
            try
            {
                await Task.WhenAll(pending).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"FileWatcherIntegration: Error waiting for pending updates during stop: {ex.Message}");
            }
        }
    }

    private void OnFileChanged(object? sender, FileChangedEventArgs e)
    {
        if (!IsCsFile(e.FullPath) || IsInExcludedDirectory(e.FullPath))
        {
            return;
        }

        _ = ProcessFileChangeAsync(e.FullPath);
    }

    private void OnFileDeleted(object? sender, FileChangedEventArgs e)
    {
        if (!IsCsFile(e.FullPath) || IsInExcludedDirectory(e.FullPath))
        {
            return;
        }

        _ = ProcessFileChangeAsync(e.FullPath);
    }

    private void OnFileRenamed(object? sender, FileRenamedEventArgs e)
    {
        if (IsCsFile(e.OldFullPath) && !IsInExcludedDirectory(e.OldFullPath))
        {
            _ = ProcessFileChangeAsync(e.OldFullPath);
        }

        if (IsCsFile(e.FullPath) && !IsInExcludedDirectory(e.FullPath))
        {
            _ = ProcessFileChangeAsync(e.FullPath);
        }
    }

    /// <summary>
    /// 处理文件变更 — 异步更新索引，异常在内部捕获，不会终止进程
    /// </summary>
    private async Task ProcessFileChangeAsync(string filePath)
    {
        try
        {
            var ct = _updateCts?.Token ?? CancellationToken.None;
            var task = SafeUpdateAsync(filePath, ct);
            await _pendingLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _pendingUpdates.Add(task);
            }
            finally
            {
                _pendingLock.Release();
            }

            _ = WatchTaskAsync(task);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"FileWatcherIntegration: Error in ProcessFileChangeAsync: {ex.Message}");
        }
    }

    private async Task WatchTaskAsync(Task task)
    {
        try
        {
#pragma warning disable VSTHRD003 // 任务由调用方启动，此处仅等待完成
            await task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"FileWatcherIntegration: Error watching task completion: {ex.Message}");
        }
        finally
        {
            await _pendingLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _pendingUpdates.Remove(task);
            }
            finally
            {
                _pendingLock.Release();
            }
        }
    }

    private async Task SafeUpdateAsync(string filePath, CancellationToken ct)
    {
        try
        {
            await _indexer.UpdateFileAsync(filePath, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _onError?.Invoke(ex);
        }
    }

    private bool IsCsFile(string filePath)
    {
        return filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsInExcludedDirectory(string filePath)
    {
        var relativePath = Path.GetRelativePath(_workspaceRoot, filePath);
        var span = relativePath.AsSpan();

        while (!span.IsEmpty)
        {
            var idx = span.IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var segment = idx < 0 ? span : span[..idx];

            if (!segment.IsEmpty && _excludedDirs.Contains(segment.ToString()))
            {
                return true;
            }

            span = idx < 0 ? [] : span[(idx + 1)..];
        }

        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _disposed))
        {
            return;
        }

        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _updateCts?.Dispose();
        _updateCts = null;
        _pendingLock.Dispose();
    }

    public void Dispose()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _disposed))
        {
            return;
        }

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        _updateCts?.Cancel();
        _updateCts?.Dispose();
        _updateCts = null;
        _pendingLock.Dispose();
    }
}
