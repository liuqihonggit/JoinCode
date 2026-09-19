namespace Core.Prompts.Utils;

/// <summary>
/// 动态关键词词表服务 — 从 ~/.jcc/keyword-sections.json 加载关键词配置，支持文件监控热加载
/// </summary>
[Register(typeof(IDynamicKeywordConfigService), ServiceLifetime.Singleton)]
public sealed partial class DynamicKeywordConfigService : ServiceEntity, IDynamicKeywordConfigService, IDisposable {
    private readonly IFileSystem _fs;
    private readonly ILogger<DynamicKeywordConfigService>? _logger;

    private readonly ReloadActor _actor;
    private volatile DynamicKeywordConfig _config = new();
    private IFileSystemWatcher? _watcher;
    private int _disposed;

    /// <summary>
    /// 配置文件名 — 位于 ~/.jcc/ 目录下
    /// </summary>
    private const string ConfigFileName = "keyword-sections.json";

    /// <summary>
    /// 构造动态关键词配置服务 — 加载配置文件并启动文件变更监控。
    /// </summary>
    /// <param name="fs">文件系统抽象。</param>
    /// <param name="logger">日志记录器，可为 null。</param>
    public DynamicKeywordConfigService(IFileSystem fs, ILogger<DynamicKeywordConfigService>? logger = null) {
        _fs = fs;
        _logger = logger;
        _actor = new ReloadActor(this, logger);
        LoadConfig();
        StartWatching();
    }

    /// <inheritdoc/>
    public DynamicKeywordConfig Config => _config;

    /// <inheritdoc/>
    public DynamicKeywordMatchResult? TryMatch(string input) => DynamicKeywordMatcher.TryMatch(input, _config);

    /// <inheritdoc/>
    public event EventHandler? ConfigChanged;

    /// <summary>
    /// 获取配置文件完整路径
    /// </summary>
    private string GetConfigFilePath() {
        return AppDataConstants.Paths.KeywordSectionsFilePath;
    }

    /// <summary>
    /// 加载配置文件，失败时保留旧配置
    /// </summary>
    private void LoadConfig() {
        try {
            var filePath = GetConfigFilePath();
            if (!_fs.FileExists(filePath)) {
                _logger?.LogDebug("动态关键词配置文件不存在: {Path}，使用空配置", filePath);
                return;
            }

            var json = _fs.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json))
                return;

            var config = RelaxedJsonSerializer.Deserialize(json, DynamicKeywordConfigJsonContext.Default.DynamicKeywordConfig);
            if (config is not null) {
                _config = config;
                _logger?.LogInformation("动态关键词配置已加载: {Count} 个 Section", config.Sections.Count);
            }
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "加载动态关键词配置失败，保留旧配置");
        }
    }

    /// <summary>
    /// 启动文件监控，配置文件修改时自动重载
    /// </summary>
    private void StartWatching() {
        try {
            var filePath = GetConfigFilePath();
            var dir = Path.GetDirectoryName(filePath);
            if (dir is null || !_fs.DirectoryExists(dir)) {
                _logger?.LogDebug("动态关键词配置目录不存在，跳过文件监控: {Dir}", dir);
                return;
            }

            _watcher = _fs.Watch(dir, ConfigFileName);
            _watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
            _watcher.DebounceInterval = TimeSpan.FromMilliseconds(200);
            _watcher.DebouncedChanged += async (_, _) => await ReloadOnFileChangeAsync();
            _watcher.EnableRaisingEvents = true;
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "启动动态关键词配置文件监控失败");
        }
    }

    private async Task ReloadOnFileChangeAsync() {
        var reply = new TaskCompletionSource();
        await _actor.SendAsync(new ReloadConfigCmd(reply), CancellationToken.None).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 重载配置内部实现 — 由 ReloadActor Consumer 串行调用，无锁安全。
    /// <para>TASK001: 原 AsyncLock 保护逻辑迁移到 Actor 邮箱管道，Consumer 单线程串行处理。</para>
    /// </summary>
    private void ReloadConfigInternal() {
        LoadConfig();
        ConfigChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 异步释放资源 — 停止文件监听并 await Actor 完全退出 — TASK001
    /// </summary>
    public override async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _watcher?.Dispose();
        await _actor.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 配置重载 Actor — 串行化 ReloadOnFileChangeAsync，消除显式锁 — TASK001
    /// <para>命令通过 Channel 投递，Consumer 单线程串行处理，天然无竞态。</para>
    /// </summary>
    private sealed class ReloadActor : ActorBase<ReloadConfigCmd, Unit> {
        private readonly DynamicKeywordConfigService _owner;
        private readonly ILogger<DynamicKeywordConfigService>? _logger;

        public ReloadActor(DynamicKeywordConfigService owner, ILogger<DynamicKeywordConfigService>? logger) : base() {
            _owner = owner;
            _logger = logger;
        }

        /// <summary>Ask 模式等待回复 — 暴露 protected AskAwait 供 DynamicKeywordConfigService 调用</summary>
        public async Task AskReplyAsync(TaskCompletionSource tcs, CancellationToken ct = default)
            => await base.AskAwait(tcs, ct).ConfigureAwait(false);

        protected override ValueTask HandleAsync(ReloadConfigCmd cmd, CancellationToken ct) {
            try {
                _owner.ReloadConfigInternal();
                cmd.Reply.SetResult();
            } catch (OperationCanceledException) { throw; } catch (Exception ex) { cmd.Reply.SetException(ex); }
            return default;
        }

        protected override void OnConsumerError(Exception ex)
            => _logger?.LogWarning(ex, "ReloadActor 命令处理异常");
    }
}

/// <summary>
/// 动态关键词匹配结果
/// </summary>
public sealed record DynamicKeywordMatchResult {
    /// <summary>
    /// 匹配到的 Section 名称
    /// </summary>
    public string SectionName { get; init; } = "";

    /// <summary>
    /// 匹配到的关键词
    /// </summary>
    public string MatchedKeyword { get; init; } = "";

    /// <summary>
    /// 自定义注入内容（可为空，为空时使用内置 Section 内容）
    /// </summary>
    public string? CustomContent { get; init; }

    /// <summary>
    /// 是否有自定义内容
    /// </summary>
    public bool HasCustomContent => !string.IsNullOrEmpty(CustomContent);
}

/// <summary>
/// 动态关键词配置服务接口
/// </summary>
public interface IDynamicKeywordConfigService {
    /// <summary>
    /// 当前配置
    /// </summary>
    DynamicKeywordConfig Config { get; }

    /// <summary>
    /// 尝试匹配用户输入中的动态关键词
    /// </summary>
    DynamicKeywordMatchResult? TryMatch(string input);

    /// <summary>
    /// 配置变更事件
    /// </summary>
    event EventHandler ConfigChanged;
}

/// <summary>
/// 动态关键词配置 JSON 序列化上下文（NativeAOT 兼容）
/// </summary>
[JsonSerializable(typeof(DynamicKeywordConfig))]
[JsonSerializable(typeof(DynamicKeywordSection))]
[JsonSerializable(typeof(Dictionary<string, DynamicKeywordSection>))]
internal sealed partial class DynamicKeywordConfigJsonContext : JsonSerializerContext;

/// <summary>
/// 配置重载命令 — 对应 ReloadOnFileChangeAsync，由 ReloadActor Consumer 串行处理。
/// <para>TASK001: AsyncLock+文件 I/O 迁移到 Actor 邮箱管道，消除显式锁。</para>
/// </summary>
public sealed record ReloadConfigCmd(TaskCompletionSource Reply);