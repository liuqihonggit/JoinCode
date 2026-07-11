namespace Memdir.Sync;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// 同步启动管道共享上下文 — 在中间件各阶段间传递状态
/// </summary>
public sealed class SyncStartContext : INullCheckContext, IMetricsContext
{
    // === 输入 ===

    /// <summary>文件系统</summary>
    public required IFileSystem FileSystem { get; init; }

    /// <summary>文件操作服务</summary>
    public required IFileOperationService FileOperationService { get; init; }

    /// <summary>同步选项</summary>
    public required TeamMemorySyncOptions Options { get; init; }

    /// <summary>取消令牌</summary>
    public CancellationToken CancellationToken { get; init; }

    // === 服务状态（由 TeamMemorySyncService 提供） ===

    /// <summary>是否已释放</summary>
    public required bool IsDisposed { get; init; }

    /// <summary>是否已运行</summary>
    public required bool IsAlreadyRunning { get; init; }

    /// <summary>本地文件条目</summary>
    public required ConcurrentDictionary<string, SyncFileEntry> LocalEntries { get; init; }

    /// <summary>远程文件条目</summary>
    public required ConcurrentDictionary<string, SyncFileEntry> RemoteEntries { get; init; }

    /// <summary>同步历史队列</summary>
    public required ConcurrentQueue<MemorySyncEvent> SyncHistory { get; init; }

    // === Step 6: AutoSyncMiddleware 填充 ===

    /// <summary>同步定时器（由 AutoSyncMiddleware 配置）</summary>
    public System.Threading.Timer? SyncTimer { get; set; }

    // === Step 5: FileWatcherMiddleware 填充 ===

    /// <summary>文件监控器（由 FileWatcherMiddleware 创建）</summary>
    public IFileSystemWatcher? Watcher { get; set; }

    // === 输出 ===

    /// <summary>是否应标记为运行中</summary>
    public bool MarkAsRunning { get; set; }

    // === INullCheckContext ===

    public IEnumerable<(string Name, object? Value)> RequiredParameters =>
    [
        (nameof(FileSystem), FileSystem),
        (nameof(FileOperationService), FileOperationService),
        (nameof(Options), Options),
    ];

    // === IMetricsContext ===

    public string MetricsPrefix => "sync.memory";
    public bool IsMetricsSuccess => !Failed;
    public long? MetricsDurationMs => null;

    public Dictionary<string, string> BuildMetricsTags() => new()
    {
        ["operation"] = "start",
        ["success"] = IsMetricsSuccess.ToString()
    };

    // === IPipelineContext ===

    public bool Failed { get; set; }
    public string? ErrorMessage { get; set; }
    public void Fail(string message) { Failed = true; ErrorMessage = message; }
}
