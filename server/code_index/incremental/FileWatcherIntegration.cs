namespace JoinCode.CodeIndex;

/// <summary>
/// 代码索引增量更新 Actor — 基于 FileWatcherActorBase,监控 *.cs 文件变更触发索引更新。
/// <para>对齐 ADR 0101: 消除 _pendingLock/_pendingUpdates,Actor Consumer 串行化索引更新。</para>
/// <para>排除目录: bin, obj, .git, .x — 用 Span&lt;char&gt; 零 GC 检查。</para>
/// </summary>
[Register(typeof(FileWatcherIntegration), ServiceLifetime.Singleton)]
public sealed partial class FileWatcherIntegration : FileWatcherActorBase
{
    private readonly ICodeIndexer _indexer;
    private readonly string _workspaceRoot;
    private readonly HashSet<string> _excludedDirs;
    private readonly TimeSpan _debounceInterval;
    private readonly Action<Exception>? _onError;
    private readonly ILogger<FileWatcherIntegration>? _logger;

    private static readonly string[] DefaultExcludedDirs = new[] { "bin", "obj", ".git", ".x" };

    /// <summary>
    /// 构造文件监控集成 — 不带错误回调和文件系统抽象
    /// </summary>
    /// <param name="indexer">代码索引器</param>
    /// <param name="workspaceRoot">工作区根目录</param>
    /// <param name="debounceInterval">去抖间隔（可选）</param>
    public FileWatcherIntegration(ICodeIndexer indexer, string workspaceRoot, TimeSpan? debounceInterval = null)
        : this(indexer, workspaceRoot, null, null, debounceInterval)
    {
    }

    /// <summary>
    /// 构造文件监控集成 — 带错误回调但不带文件系统抽象
    /// </summary>
    /// <param name="indexer">代码索引器</param>
    /// <param name="workspaceRoot">工作区根目录</param>
    /// <param name="onError">错误回调（可选）</param>
    /// <param name="debounceInterval">去抖间隔（可选）</param>
    public FileWatcherIntegration(ICodeIndexer indexer, string workspaceRoot, Action<Exception>? onError, TimeSpan? debounceInterval = null)
        : this(indexer, workspaceRoot, null, onError, debounceInterval)
    {
    }

    /// <summary>
    /// 构造文件监控集成 — 完整参数版本
    /// </summary>
    /// <param name="indexer">代码索引器</param>
    /// <param name="workspaceRoot">工作区根目录</param>
    /// <param name="fs">文件系统抽象（可选，默认 PhysicalFileSystem）</param>
    /// <param name="onError">错误回调（可选）</param>
    /// <param name="debounceInterval">去抖间隔（可选，默认 500ms）</param>
    /// <param name="logger">日志记录器（可选）</param>
    public FileWatcherIntegration(ICodeIndexer indexer, string workspaceRoot, IFileSystem? fs, Action<Exception>? onError, TimeSpan? debounceInterval = null, ILogger<FileWatcherIntegration>? logger = null)
        : base(fs ?? new PhysicalFileSystem(), 1000)
    {
        ArgumentNullException.ThrowIfNull(indexer);
        ArgumentNullException.ThrowIfNull(workspaceRoot);

        _indexer = indexer;
        _workspaceRoot = workspaceRoot;
        _onError = onError;
        _logger = logger;
        _debounceInterval = debounceInterval ?? TimeSpan.FromMilliseconds(500);
        _excludedDirs = new HashSet<string>(DefaultExcludedDirs, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>DI 构造函数 — 从 CodeIndexOptions 获取 workspaceRoot,注入 IFileSystem</summary>
    public FileWatcherIntegration(ICodeIndexer indexer, CodeIndexOptions options, IFileSystem fs)
        : this(indexer, options.WorkspaceRoot, fs, null)
    {
    }

    /// <summary>
    /// 启动文件监控 — 发送 FileWatcherStartCmd 到 Actor 邮箱
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public Task StartAsync(CancellationToken ct)
    {
        TrySend(new FileWatcherStartCmd(
            _workspaceRoot, "*.cs", _debounceInterval,
            IncludeSubdirectories: true,
            NotifyFilter: NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        ));
        return Task.CompletedTask;
    }

    /// <summary>
    /// 停止文件监控 — 发送 FileWatcherStopCmd 到 Actor 邮箱
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task StopAsync(CancellationToken ct = default)
    {
        await SendAsync(new FileWatcherStopCmd(), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 处理文件变更事件 — 过滤非 .cs 文件和排除目录后触发索引更新
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="kind">变更类型</param>
    /// <param name="timestamp">变更时间戳</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的值任务</returns>
    protected override async ValueTask HandleFileChangedAsync(string filePath, WatcherChangeTypes kind, DateTimeOffset timestamp, CancellationToken ct)
    {
        if (!IsCsFile(filePath) || IsInExcludedDirectory(filePath)) return;
        await SafeUpdateAsync(filePath, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 处理文件重命名事件 — 对旧路径和新路径分别触发索引更新
    /// </summary>
    /// <param name="oldPath">旧文件路径</param>
    /// <param name="newPath">新文件路径</param>
    /// <param name="timestamp">变更时间戳</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的值任务</returns>
    protected override async ValueTask HandleFileRenamedAsync(string oldPath, string newPath, DateTimeOffset timestamp, CancellationToken ct)
    {
        if (IsCsFile(oldPath) && !IsInExcludedDirectory(oldPath))
            await SafeUpdateAsync(oldPath, ct).ConfigureAwait(false);
        if (IsCsFile(newPath) && !IsInExcludedDirectory(newPath))
            await SafeUpdateAsync(newPath, ct).ConfigureAwait(false);
    }

    private async ValueTask SafeUpdateAsync(string filePath, CancellationToken ct)
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
            _logger?.LogWarning(ex, "FileWatcherIntegration: 更新索引失败 {Path}", filePath);
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
}
