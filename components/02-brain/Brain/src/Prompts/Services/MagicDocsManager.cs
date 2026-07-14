
namespace Core.Prompts.Services;

/// <summary>
/// MagicDocs 管理服务 — 追踪已注册的 Magic Doc 文件，提供 FileRead 监听器
/// 对齐 TS magicDocs.ts::trackedMagicDocs + registerFileReadListener
/// 核心消费点：MagicDocsPromptTemplate.BuildMagicDocsUpdatePrompt()
/// </summary>
[Register]
public sealed class MagicDocsManager : IFileReadListener, IPostSamplingCallback
{
    private readonly IFileSystem _fileSystem;
    private readonly IForkSubAgentManager? _forkManager;
    private readonly ILogger<MagicDocsManager>? _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly Dictionary<string, MagicDocEntry> _trackedDocs = new(StringComparer.OrdinalIgnoreCase);
    private IDisposable? _fileReadSubscription;
    // P1-8: 信号量等待超时 — 防止持有方异常未释放导致永久阻塞
    private static readonly TimeSpan SemaphoreWaitTimeout = TimeSpan.FromSeconds(5);

    public MagicDocsManager(
        IFileSystem fileSystem,
        IFileReadListenerRegistry? fileReadListenerRegistry = null,
        IPostSamplingCallbackManager? postSamplingCallbacks = null,
        IForkSubAgentManager? forkManager = null,
        ILogger<MagicDocsManager>? logger = null)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _forkManager = forkManager;
        _logger = logger;

        if (fileReadListenerRegistry is not null)
        {
            _fileReadSubscription = fileReadListenerRegistry.Register(this);
        }

        if (postSamplingCallbacks is not null)
        {
            postSamplingCallbacks.Register(this);
        }
    }

    /// <summary>
    /// FileRead 监听器 — 检测读取的文件是否为 Magic Doc
    /// </summary>
    public void OnFileRead(FileReadEventArgs e)
    {
        var detection = MagicDocDetector.Detect(e.Content);
        if (detection is null) return;

        // P1-8: 添加超时，防止永久阻塞
        if (!_semaphore.Wait(SemaphoreWaitTimeout))
        {
            _logger?.LogWarning("MagicDocsManager.OnFileRead 信号量等待超时，跳过注册: {FilePath}", e.FilePath);
            return;
        }
        try
        {
            _trackedDocs[e.FilePath] = new MagicDocEntry
            {
                FilePath = e.FilePath,
                Title = detection.Title,
                CustomInstructions = detection.CustomInstructions
            };
        }
        finally
        {
            _semaphore.Release();
        }

        _logger?.LogDebug("Magic Doc 已注册: {FilePath} (标题: {Title})", e.FilePath, detection.Title);
    }

    /// <summary>
    /// PostSampling 回调 — 在对话空闲时更新 Magic Doc
    /// </summary>
    public async Task OnPostSamplingAsync(PostSamplingContext context)
    {
        if (context.QuerySource != "repl_main_thread") return;

        List<MagicDocEntry> docsToUpdate;
        await _semaphore.WaitAsync(context.CancellationToken).ConfigureAwait(false);
        try
        {
            if (_trackedDocs.Count == 0) return;
            docsToUpdate = [.. _trackedDocs.Values];
        }
        finally
        {
            _semaphore.Release();
        }

        foreach (var doc in docsToUpdate)
        {
            try
            {
                await UpdateMagicDocAsync(doc, context).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "更新 Magic Doc 失败: {FilePath}", doc.FilePath);
            }
        }
    }

    private async Task UpdateMagicDocAsync(MagicDocEntry doc, PostSamplingContext context)
    {
        if (!_fileSystem.FileExists(doc.FilePath))
        {
            await RemoveTrackedDocAsync(doc.FilePath).ConfigureAwait(false);
            return;
        }

        var content = await _fileSystem.ReadAllTextAsync(doc.FilePath, context.CancellationToken).ConfigureAwait(false);
        var detection = MagicDocDetector.Detect(content);
        if (detection is null)
        {
            await RemoveTrackedDocAsync(doc.FilePath).ConfigureAwait(false);
            return;
        }

        var prompt = MagicDocsPromptTemplate.BuildMagicDocsUpdatePrompt(
            content, doc.FilePath, doc.Title, doc.CustomInstructions);

        _logger?.LogDebug("Magic Docs 更新提示词已构建: {FilePath}", doc.FilePath);

        if (_forkManager is not null && context.SessionId is not null)
        {
            var forkOptions = new ForkOptions
            {
                ParentSessionId = context.SessionId,
                TaskDescription = "magic_docs",
                AllowedTools = ["Edit"],
                UseExactTools = true,
                RunInBackground = true,
                ShareCache = false,
                ShareContext = false,
                MaxIterations = 3,
                SystemPrompt = "你是一个文档更新助手。你的唯一任务是使用 Edit 工具更新 Magic Doc 文件，然后停止。不要调用任何其他工具。"
            };

            await _forkManager.ForkAsync(forkOptions, context.CancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RemoveTrackedDocAsync(string filePath)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            _trackedDocs.Remove(filePath);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// 获取当前追踪的 Magic Doc 数量
    /// </summary>
    public int TrackedCount
    {
        get
        {
            // P1-8: 添加超时，防止永久阻塞；超时返回当前未加锁计数（best-effort）
            if (!_semaphore.Wait(SemaphoreWaitTimeout))
            {
                _logger?.LogWarning("MagicDocsManager.TrackedCount 信号量等待超时，返回未加锁计数");
                return _trackedDocs.Count;
            }
            try { return _trackedDocs.Count; }
            finally { _semaphore.Release(); }
        }
    }

    /// <summary>
    /// 清除所有追踪的 Magic Doc
    /// </summary>
    public void Clear()
    {
        // P1-8: 添加超时，防止永久阻塞
        if (!_semaphore.Wait(SemaphoreWaitTimeout))
        {
            _logger?.LogWarning("MagicDocsManager.Clear 信号量等待超时，跳过清除");
            return;
        }
        try { _trackedDocs.Clear(); }
        finally { _semaphore.Release(); }
    }
}
