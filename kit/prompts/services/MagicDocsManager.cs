
namespace Core.Prompts.Services;

/// <summary>
/// MagicDocs 管理服务 — 追踪已注册的 Magic Doc 文件，提供 FileRead 监听器
/// 对齐 TS magicDocs.ts::trackedMagicDocs + registerFileReadListener
/// 核心消费点：MagicDocsPromptTemplate.BuildMagicDocsUpdatePrompt()
/// 使用 Actor 邮箱管道串行化 _trackedDocs 访问，消除显式锁 — ADR 0115
/// </summary>
[Register(typeof(IFileReadListener), ServiceLifetime.Singleton)]
[Register(typeof(IPostSamplingCallback), ServiceLifetime.Singleton)]
public sealed partial class MagicDocsManager : ServiceEntity, IFileReadListener, IPostSamplingCallback
{
    private readonly IFileSystem _fileSystem;
    private readonly IForkSubAgentManager? _forkManager;
    private readonly ILogger<MagicDocsManager>? _logger;
    private readonly MagicDocsActor _actor;
    private readonly Dictionary<string, MagicDocEntry> _trackedDocs = new(StringComparer.OrdinalIgnoreCase);
    private IDisposable? _fileReadSubscription;

    /// <summary>
    /// 构造 MagicDocs 管理服务。
    /// </summary>
    /// <param name="fileSystem">文件系统抽象。</param>
    /// <param name="fileReadListenerRegistry">FileRead 监听器注册表，可选。</param>
    /// <param name="postSamplingCallbacks">采样后回调注册表，可选。</param>
    /// <param name="forkManager">子智能体分叉管理器，可选。</param>
    /// <param name="logger">日志记录器，可选。</param>
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
        _actor = new MagicDocsActor(this, logger);

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

        var reply = new TaskCompletionSource();
        if (!_actor.TrySend(new OnFileReadCmd(e.FilePath, detection, reply)))
        {
            _logger?.LogWarning("MagicDocsManager.OnFileRead Actor 已释放，跳过注册: {FilePath}", e.FilePath);
        }
    }

    /// <summary>
    /// PostSampling 回调 — 在对话空闲时更新 Magic Doc
    /// </summary>
    public async Task OnPostSamplingAsync(PostSamplingContext context)
    {
        if (context.QuerySource != "repl_main_thread") return;

        var reply = new TaskCompletionSource<IReadOnlyList<MagicDocEntry>>();
        await _actor.SendAsync(new PostSamplingCmd(reply), context.CancellationToken).ConfigureAwait(false);
        var docsToUpdate = await _actor.AskReplyAsync(reply, context.CancellationToken).ConfigureAwait(false);

        if (docsToUpdate.Count == 0) return;

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
                AllowedTools = [FileToolNameEnumConstants.FileEdit],
                UseExactTools = true,
                RunInBackground = true,
                ShareCache = false,
                ShareContext = false,
                MaxIterations = 3,
                SystemPrompt = $"你是一个文档更新助手。你的唯一任务是使用 {FileToolNameEnumConstants.FileEdit} 工具更新 Magic Doc 文件，然后停止。不要调用任何其他工具。"
            };

            await _forkManager.ForkAsync(forkOptions, context.CancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RemoveTrackedDocAsync(string filePath)
    {
        var reply = new TaskCompletionSource();
        await _actor.SendAsync(new RemoveTrackedCmd(filePath, reply)).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取当前追踪的 Magic Doc 数量
    /// </summary>
    public int TrackedCount
    {
        get
        {
            var reply = new TaskCompletionSource<int>();
            if (!_actor.TrySend(new GetTrackedCountCmd(reply)))
            {
                _logger?.LogWarning("MagicDocsManager.TrackedCount Actor 已释放，返回 0");
                return 0;
            }
            try
            {
                return _actor.AskReplyAsync(reply).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "MagicDocsManager.TrackedCount Actor Ask 失败，返回 0");
                return 0;
            }
        }
    }

    /// <summary>
    /// 清除所有追踪的 Magic Doc
    /// </summary>
    public void Clear()
    {
        var reply = new TaskCompletionSource();
        if (!_actor.TrySend(new ClearCmd(reply)))
        {
            _logger?.LogWarning("MagicDocsManager.Clear Actor 已释放，跳过清除");
        }
    }

    /// <summary>
    /// 异步释放资源 — await Actor 完全退出
    /// </summary>
    public override async ValueTask DisposeAsync()
    {
        await _actor.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    // === 锁内逻辑（由 Actor Consumer 串行调用，无需锁）===

    private void OnFileReadInternal(string filePath, MagicDocDetection detection)
    {
        _trackedDocs[filePath] = new MagicDocEntry
        {
            FilePath = filePath,
            Title = detection.Title,
            CustomInstructions = detection.CustomInstructions
        };

        _logger?.LogDebug("Magic Doc 已注册: {FilePath} (标题: {Title})", filePath, detection.Title);
    }

    private IReadOnlyList<MagicDocEntry> GetTrackedDocsSnapshot()
        => [.. _trackedDocs.Values];

    private void RemoveTrackedDocInternal(string filePath)
        => _trackedDocs.Remove(filePath);

    private int GetTrackedCountInternal()
        => _trackedDocs.Count;

    private void ClearInternal()
        => _trackedDocs.Clear();

    /// <summary>
    /// MagicDocs 管理 Actor — 串行化所有 _trackedDocs 访问，消除显式锁 — ADR 0115
    /// <para>命令通过 Channel 投递，Consumer 单线程串行处理，天然无竞态。</para>
    /// </summary>
    private sealed class MagicDocsActor : ActorBase<MagicDocsCommand, Unit>
    {
        private readonly MagicDocsManager _owner;
        private readonly ILogger<MagicDocsManager>? _logger;

        public MagicDocsActor(MagicDocsManager owner, ILogger<MagicDocsManager>? logger) : base()
        {
            _owner = owner;
            _logger = logger;
        }

        /// <summary>Ask 模式等待回复（泛型）— 暴露 protected AskAwait 供 MagicDocsManager 调用</summary>
        public async Task<T> AskReplyAsync<T>(TaskCompletionSource<T> tcs, CancellationToken ct = default)
            => await base.AskAwait(tcs, ct).ConfigureAwait(false);

        /// <summary>Ask 模式等待回复（非泛型）— 暴露 protected AskAwait 供 MagicDocsManager 调用</summary>
        public async Task AskReplyAsync(TaskCompletionSource tcs, CancellationToken ct = default)
            => await base.AskAwait(tcs, ct).ConfigureAwait(false);

        protected override async ValueTask HandleAsync(MagicDocsCommand cmd, CancellationToken ct)
        {
            try
            {
                switch (cmd)
                {
                    case OnFileReadCmd(var filePath, var detection, var reply):
                        _owner.OnFileReadInternal(filePath, detection);
                        reply.SetResult();
                        break;
                    case PostSamplingCmd(var reply):
                        reply.SetResult(_owner.GetTrackedDocsSnapshot());
                        break;
                    case RemoveTrackedCmd(var filePath, var reply):
                        _owner.RemoveTrackedDocInternal(filePath);
                        reply.SetResult();
                        break;
                    case GetTrackedCountCmd(var reply):
                        reply.SetResult(_owner.GetTrackedCountInternal());
                        break;
                    case ClearCmd(var reply):
                        _owner.ClearInternal();
                        reply.SetResult();
                        break;
                    default:
                        throw new InvalidOperationException($"未知 MagicDocs 命令类型: {cmd.GetType().Name}");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                SetReplyException(cmd, ex);
            }
        }

        private static void SetReplyException(MagicDocsCommand cmd, Exception ex)
        {
            switch (cmd)
            {
                case OnFileReadCmd(_, _, var reply): reply.TrySetException(ex); break;
                case PostSamplingCmd(var reply): reply.TrySetException(ex); break;
                case RemoveTrackedCmd(_, var reply): reply.TrySetException(ex); break;
                case GetTrackedCountCmd(var reply): reply.TrySetException(ex); break;
                case ClearCmd(var reply): reply.TrySetException(ex); break;
            }
        }

        protected override void OnConsumerError(Exception ex)
            => _logger?.LogWarning(ex, "MagicDocsActor 命令处理异常");
    }
}

// === Actor 命令类型 ===

/// <summary>
/// MagicDocs 管理 Actor 命令类型 — 每个命令对应一个 MagicDocsManager 操作，由 MagicDocsActor Consumer 串行处理。
/// <para>ADR 0115: AsyncLock 迁移到 Actor 邮箱管道，消除 5 处显式锁。</para>
/// </summary>
internal abstract record MagicDocsCommand;

/// <summary>FileRead 监听 — 注册 Magic Doc 到 _trackedDocs</summary>
internal sealed record OnFileReadCmd(
    string FilePath,
    MagicDocDetection Detection,
    TaskCompletionSource Reply) : MagicDocsCommand;

/// <summary>PostSampling — 拷贝 _trackedDocs 快照供锁外文件更新</summary>
internal sealed record PostSamplingCmd(
    TaskCompletionSource<IReadOnlyList<MagicDocEntry>> Reply) : MagicDocsCommand;

/// <summary>移除追踪文档 — 从 _trackedDocs 删除指定路径</summary>
internal sealed record RemoveTrackedCmd(
    string FilePath,
    TaskCompletionSource Reply) : MagicDocsCommand;

/// <summary>获取追踪文档数量 — 返回 _trackedDocs.Count</summary>
internal sealed record GetTrackedCountCmd(
    TaskCompletionSource<int> Reply) : MagicDocsCommand;

/// <summary>清除所有追踪文档 — 清空 _trackedDocs</summary>
internal sealed record ClearCmd(
    TaskCompletionSource Reply) : MagicDocsCommand;
