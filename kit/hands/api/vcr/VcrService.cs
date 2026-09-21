
namespace Services.Api.Vcr;

/// <summary>
/// VCR（录像/回放）服务实现，负责 cassette 加载、保存、交互录制与回放匹配
/// </summary>
[Register(typeof(IVcrService), ServiceLifetime.Singleton)]
[Register(typeof(JoinCode.Abstractions.Interfaces.IVcrService), ServiceLifetime.Singleton)]
public sealed partial class VcrService : ServiceEntity, IVcrService, JoinCode.Abstractions.Interfaces.IVcrService, IDisposable {
    private readonly VcrOptions _options;
    private readonly ILogger<VcrService>? _logger;
    private readonly IFileSystem _fs;
    private readonly VcrActor _actor;
    private readonly ConcurrentDictionary<string, VcrCassette> _cassetteCache = new(StringComparer.OrdinalIgnoreCase);

    private VcrMode _currentMode;

    /// <summary>
    /// 当前 VCR 模式
    /// </summary>
    public VcrMode CurrentMode => _currentMode;

    /// <summary>
    /// cassette 默认存放目录
    /// </summary>
    public string CassettesDirectory => _options.CassettesDirectory;

    /// <summary>
    /// 构造 VcrService
    /// </summary>
    /// <param name="options">VCR 配置选项</param>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="logger">可选日志记录器</param>
    public VcrService(VcrOptions options, IFileSystem fs, ILogger<VcrService>? logger = null) {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fs);
        _options = options;
        _fs = fs;
        _logger = logger;
        _currentMode = options.Mode;
        _actor = new VcrActor(this, logger);
    }

    /// <summary>
    /// 异步加载 cassette，若文件不存在则创建空 cassette；命中缓存直接返回
    /// </summary>
    /// <param name="name">cassette 名称</param>
    /// <param name="directory">可选目录覆盖</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>加载到的 cassette 实例</returns>
    public async Task<VcrCassette> LoadCassetteAsync(string name, string? directory = null, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var cacheKey = GetCassettePath(name, directory);
        if (_cassetteCache.TryGetValue(cacheKey, out var cached)) {
            return cached;
        }

        var reply = new TaskCompletionSource<VcrCassette>();
        await _actor.SendAsync(new LoadCassetteCmd(cacheKey, name, reply), cancellationToken).ConfigureAwait(false);
        return await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 加载 cassette 锁内逻辑 — 由 VcrActor Consumer 串行调用，无锁访问共享状态
    /// </summary>
    /// <param name="filePath">cassette 文件路径</param>
    /// <param name="name">cassette 名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>加载到的 cassette 实例</returns>
    private async Task<VcrCassette> LoadCassetteInternalAsync(string filePath, string name, CancellationToken cancellationToken) {
        if (!_fs.FileExists(filePath)) {
            var cassette = new VcrCassette { Name = name };
            _cassetteCache[filePath] = cassette;
            _logger?.LogDebug("创建新 cassette: {Name}", name);
            return cassette;
        }

        var loaded = await _fs.ReadAndDeserializeAsync(filePath, VcrJsonContext.Default.VcrCassette, cancellationToken).ConfigureAwait(false);
        if (loaded == null) {
            loaded = new VcrCassette { Name = name };
        }

        _cassetteCache[filePath] = loaded;
        _logger?.LogDebug("加载 cassette: {Name}, 交互数={Count}", name, loaded.Interactions.Count);
        return loaded;
    }

    /// <summary>
    /// 异步保存 cassette 到磁盘并更新缓存
    /// </summary>
    /// <param name="cassette">cassette 实例</param>
    /// <param name="directory">可选目录覆盖</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步保存操作的任务</returns>
    public async Task SaveCassetteAsync(VcrCassette cassette, string? directory = null, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(cassette);
        ArgumentException.ThrowIfNullOrEmpty(cassette.Name);

        var filePath = GetCassettePath(cassette.Name, directory);
        var reply = new TaskCompletionSource();
        await _actor.SendAsync(new SaveCassetteCmd(filePath, cassette, reply), cancellationToken).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 保存 cassette 锁内逻辑 — 由 VcrActor Consumer 串行调用，无锁访问共享状态
    /// </summary>
    /// <param name="filePath">cassette 文件路径</param>
    /// <param name="cassette">cassette 实例</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步保存操作的任务</returns>
    private async Task SaveCassetteInternalAsync(string filePath, VcrCassette cassette, CancellationToken cancellationToken) {
        var dir = Path.GetDirectoryName(filePath);
        DirectoryHelper.EnsureDirectoryExists(_fs, dir);

        var json = JsonSerializer.Serialize(cassette, VcrJsonContext.Default.VcrCassette);
        await _fs.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);

        _cassetteCache[filePath] = cassette;
        _logger?.LogDebug("保存 cassette: {Name}, 交互数={Count}", cassette.Name, cassette.Interactions.Count);
    }

    /// <summary>
    /// 录制一次 HTTP 交互；仅在录制模式下生效，根据配置决定是否保留头与体
    /// </summary>
    /// <param name="cassetteName">cassette 名称</param>
    /// <param name="request">请求记录</param>
    /// <param name="response">响应记录</param>
    /// <param name="directory">可选目录覆盖</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步录制操作的任务</returns>
    public async Task RecordInteractionAsync(string cassetteName, VcrRequest request, VcrResponse response, string? directory = null, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrEmpty(cassetteName);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);

        if (_currentMode != VcrMode.Record) {
            _logger?.LogWarning("当前模式非录制模式，跳过录制");
            return;
        }

        var cassette = await LoadCassetteAsync(cassetteName, directory, cancellationToken).ConfigureAwait(false);

        var interaction = new VcrInteraction {
            Request = _options.RecordHeaders ? request : request with { Headers = new Dictionary<string, string>() },
            Response = _options.RecordHeaders ? response : response with { Headers = new Dictionary<string, string>() },
            RecordedAt = DateTime.UtcNow
        };

        if (!_options.RecordContent) {
            interaction.Request.Body = null;
            interaction.Response.Body = null;
        }

        cassette.Interactions.Add(interaction);
        await SaveCassetteAsync(cassette, directory, cancellationToken).ConfigureAwait(false);

        _logger?.LogDebug("录制交互: {Method} {Uri} -> {Status}", request.Method, request.Uri, response.Status);
    }

    /// <summary>
    /// 在回放模式下查找匹配的录制交互；严格模式未匹配时抛出异常
    /// </summary>
    /// <param name="cassetteName">cassette 名称</param>
    /// <param name="request">待匹配的请求</param>
    /// <param name="directory">可选目录覆盖</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>匹配到的响应；非回放模式或未匹配时返回 null</returns>
    public async Task<VcrResponse?> FindMatchingInteractionAsync(string cassetteName, VcrRequest request, string? directory = null, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrEmpty(cassetteName);
        ArgumentNullException.ThrowIfNull(request);

        if (_currentMode != VcrMode.Playback) {
            _logger?.LogWarning("当前模式非回放模式，返回 null");
            return null;
        }

        var cassette = await LoadCassetteAsync(cassetteName, directory, cancellationToken).ConfigureAwait(false);

        foreach (var interaction in cassette.Interactions) {
            if (MatchesRequest(interaction.Request, request)) {
                _logger?.LogDebug("回放匹配: {Method} {Uri} -> {Status}", request.Method, request.Uri, interaction.Response.Status);
                return interaction.Response;
            }
        }

        if (_options.StrictPlayback) {
            throw new InvalidOperationException($"[HND002] 未找到匹配的录制交互: {request.Method} {request.Uri}");
        }

        _logger?.LogWarning("未找到匹配的录制交互: {Method} {Uri}", request.Method, request.Uri);
        return null;
    }

    /// <summary>
    /// 切换 VCR 运行模式
    /// </summary>
    /// <param name="mode">目标模式</param>
    public void SetMode(VcrMode mode) {
        _currentMode = mode;
        _logger?.LogInformation("VCR 模式切换为: {Mode}", mode);
    }

    /// <summary>
    /// 获取 cassette 文件完整路径，并对路径越界做安全校验
    /// </summary>
    /// <param name="name">cassette 名称</param>
    /// <param name="directory">可选目录覆盖，为 null 时使用默认目录</param>
    /// <returns>cassette 文件绝对路径</returns>
    public string GetCassettePath(string name, string? directory = null) {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var baseDir = directory ?? _options.CassettesDirectory;
        var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        var fullPath = Path.GetFullPath(Path.Combine(baseDir, $"{safeName}.json"));
        var baseFull = Path.GetFullPath(baseDir);
        if (!fullPath.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"Cassette path escapes directory: {name}");
        return fullPath;
    }

    private static bool MatchesRequest(VcrRequest recorded, VcrRequest incoming) {
        if (!string.Equals(recorded.Method, incoming.Method, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (!string.Equals(recorded.Uri, incoming.Uri, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 异步释放资源 — await Actor 完全退出
    /// </summary>
    public override async ValueTask DisposeAsync() {
        await _actor.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// VCR 文件操作 Actor — 串行化 cassette 加载/保存，消除显式锁 — TASK001
    /// <para>命令通过 Channel 投递，Consumer 单线程串行处理，天然无竞态。</para>
    /// </summary>
    private sealed class VcrActor : ActorBase<VcrCommand, Unit> {
        private readonly VcrService _owner;
        private readonly ILogger<VcrService>? _logger;

        /// <summary>构造 VCR Actor。</summary>
        /// <param name="owner">所属 VCR 服务。</param>
        /// <param name="logger">日志记录器。</param>
        public VcrActor(VcrService owner, ILogger<VcrService>? logger) : base() {
            _owner = owner;
            _logger = logger;
        }

        /// <summary>Ask 模式等待回复 — 暴露 protected AskAwait 供 VcrService 调用</summary>
        public async Task<T> AskReplyAsync<T>(TaskCompletionSource<T> tcs, CancellationToken ct = default)
            => await base.AskAwait(tcs, ct).ConfigureAwait(false);

        /// <summary>Ask 模式等待回复（无返回值） — 暴露 protected AskAwait 供 VcrService 调用</summary>
        public async Task AskReplyAsync(TaskCompletionSource tcs, CancellationToken ct = default)
            => await base.AskAwait(tcs, ct).ConfigureAwait(false);

        protected override async ValueTask HandleAsync(VcrCommand cmd, CancellationToken ct) {
            switch (cmd) {
                case LoadCassetteCmd(var filePath, var name, var reply):
                try {
                    reply.SetResult(await _owner.LoadCassetteInternalAsync(filePath, name, ct).ConfigureAwait(false));
                } catch (OperationCanceledException) { throw; } catch (Exception ex) { reply.SetException(ex); }
                break;
                case SaveCassetteCmd(var filePath, var cassette, var reply):
                try {
                    await _owner.SaveCassetteInternalAsync(filePath, cassette, ct).ConfigureAwait(false);
                    reply.SetResult();
                } catch (OperationCanceledException) { throw; } catch (Exception ex) { reply.SetException(ex); }
                break;
            }
        }

        protected override void OnConsumerError(Exception ex)
            => _logger?.LogWarning(ex, "VcrActor 命令处理异常");
    }

    /// <summary>
    /// 显式接口实现：获取 cassette 文件路径
    /// </summary>
    /// <param name="name">cassette 名称</param>
    /// <param name="directory">可选目录覆盖</param>
    /// <returns>cassette 文件绝对路径</returns>
    string JoinCode.Abstractions.Interfaces.IVcrService.GetCassettePath(string name, string? directory)
        => GetCassettePath(name, directory);

    /// <summary>
    /// 显式接口实现：cassette 默认存放目录
    /// </summary>
    string JoinCode.Abstractions.Interfaces.IVcrService.CassettesDirectory => CassettesDirectory;

    /// <summary>
    /// 显式接口实现：异步加载 cassette 并映射为抽象层模型
    /// </summary>
    /// <param name="name">cassette 名称</param>
    /// <param name="directory">可选目录覆盖</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>抽象层 cassette 模型</returns>
    async Task<JoinCode.Abstractions.Models.Vcr.VcrCassette> JoinCode.Abstractions.Interfaces.IVcrService.LoadCassetteAsync(string name, string? directory, CancellationToken cancellationToken) {
        var cassette = await LoadCassetteAsync(name, directory, cancellationToken).ConfigureAwait(false);
        return new JoinCode.Abstractions.Models.Vcr.VcrCassette {
            Name = cassette.Name,
            CreatedAt = cassette.RecordedAt,
            UpdatedAt = cassette.RecordedAt,
            InteractionCount = cassette.Interactions.Count
        };
    }

    /// <summary>
    /// 显式接口实现：当前 VCR 模式（映射为抽象层枚举）
    /// </summary>
    JoinCode.Abstractions.Models.Vcr.VcrMode JoinCode.Abstractions.Interfaces.IVcrService.CurrentMode =>
        (JoinCode.Abstractions.Models.Vcr.VcrMode)CurrentMode;

    /// <summary>
    /// 显式接口实现：切换 VCR 模式
    /// </summary>
    /// <param name="mode">抽象层模式</param>
    void JoinCode.Abstractions.Interfaces.IVcrService.SetMode(JoinCode.Abstractions.Models.Vcr.VcrMode mode) {
        SetMode((VcrMode)mode);
    }
}