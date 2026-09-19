namespace IO.Services;

/// <summary>
/// MCP 认证持久化服务 — 负责将 MCP 认证条目（名称、类型、序列化数据）持久化到配置存储，
/// 并通过 Actor 邮箱管道保证并发读写安全。
/// </summary>
[Register(typeof(IMcpAuthPersistenceService), ServiceLifetime.Singleton)]
public sealed partial class McpAuthPersistenceService : ServiceEntity, IMcpAuthPersistenceService
{
    private readonly IConfigurationService? _configService;
    private readonly ILogger<McpAuthPersistenceService>? _logger;
    private readonly McpAuthPersistenceActor _actor;

    /// <summary>
    /// 构造 MCP 认证持久化服务。
    /// </summary>
    /// <param name="configService">配置服务（可选，为 null 时所有操作变为空操作）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public McpAuthPersistenceService(IConfigurationService? configService = null, ILogger<McpAuthPersistenceService>? logger = null)
    {
        _configService = configService;
        _logger = logger;
        _actor = new McpAuthPersistenceActor(this, logger);
    }

    /// <summary>
    /// 异步保存一条 MCP 认证条目。若同名条目已存在则覆盖。
    /// </summary>
    /// <param name="authName">认证名称（唯一键）。</param>
    /// <param name="authType">认证类型。</param>
    /// <param name="serializedData">序列化后的认证数据。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>表示异步保存操作的任务。</returns>
    public async Task SaveAsync(string authName, string authType, string serializedData, CancellationToken ct = default)
    {
        if (_configService == null) return;

        var reply = new TaskCompletionSource();
        await _actor.SendAsync(new SaveAuthCmd(authName, authType, serializedData, reply), ct).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步加载指定名称的 MCP 认证条目。
    /// </summary>
    /// <param name="authName">认证名称。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>匹配的认证条目；若不存在则返回 <c>null</c>。</returns>
    public async Task<AuthConfigEntry?> LoadAsync(string authName, CancellationToken ct = default)
    {
        if (_configService == null) return null;

        var reply = new TaskCompletionSource<AuthConfigEntry?>();
        await _actor.SendAsync(new LoadAuthCmd(authName, reply), ct).ConfigureAwait(false);
        return await _actor.AskReplyAsync(reply, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步列出全部 MCP 认证条目。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>认证条目只读列表；若无任何条目则返回空列表。</returns>
    public async Task<IReadOnlyList<AuthConfigEntry>> ListAsync(CancellationToken ct = default)
    {
        if (_configService == null) return Array.Empty<AuthConfigEntry>();

        var reply = new TaskCompletionSource<IReadOnlyList<AuthConfigEntry>>();
        await _actor.SendAsync(new ListAuthCmd(reply), ct).ConfigureAwait(false);
        return await _actor.AskReplyAsync(reply, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步移除指定名称的 MCP 认证条目。若条目不存在则静默忽略。
    /// </summary>
    /// <param name="authName">认证名称。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>表示异步移除操作的任务。</returns>
    public async Task RemoveAsync(string authName, CancellationToken ct = default)
    {
        if (_configService == null) return;

        var reply = new TaskCompletionSource();
        await _actor.SendAsync(new RemoveAuthCmd(authName, reply), ct).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply, ct).ConfigureAwait(false);
    }

    private async Task SaveInternalAsync(string authName, string authType, string serializedData, CancellationToken ct)
    {
        var entries = await LoadEntriesAsync(ct).ConfigureAwait(false);
        var entry = new AuthConfigEntry
        {
            Name = authName,
            AuthType = authType,
            Data = serializedData,
            SavedAt = DateTime.UtcNow
        };

        entries[authName] = entry;

        await SaveEntriesAsync(entries, ct).ConfigureAwait(false);
    }

    private async Task<AuthConfigEntry?> LoadInternalAsync(string authName, CancellationToken ct)
    {
        var entries = await LoadEntriesAsync(ct).ConfigureAwait(false);
        return entries.TryGetValue(authName, out var entry) ? entry : null;
    }

    private async Task<IReadOnlyList<AuthConfigEntry>> ListInternalAsync(CancellationToken ct)
        => (await LoadEntriesAsync(ct).ConfigureAwait(false)).Values.ToList();

    private async Task RemoveInternalAsync(string authName, CancellationToken ct)
    {
        var entries = await LoadEntriesAsync(ct).ConfigureAwait(false);
        entries.Remove(authName);
        await SaveEntriesAsync(entries, ct).ConfigureAwait(false);
    }

    private async Task<Dictionary<string, AuthConfigEntry>> LoadEntriesAsync(CancellationToken ct)
    {
        var configService = _configService ?? throw new InvalidOperationException("Config service not available.");
        try
        {
            var json = await configService.GetAsync("mcp.auth_entries", ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(json)) return new Dictionary<string, AuthConfigEntry>();

            var entries = RelaxedJsonSerializer.Deserialize(json, AuthEntryContext.Default.ListAuthConfigEntry);
            if (entries == null) return new Dictionary<string, AuthConfigEntry>();
            return entries.ToDictionary(e => e.Name);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "加载 MCP 认证配置失败");
            return new Dictionary<string, AuthConfigEntry>();
        }
    }

    private async Task SaveEntriesAsync(Dictionary<string, AuthConfigEntry> entries, CancellationToken ct)
    {
        var configService = _configService ?? throw new InvalidOperationException("Config service not available.");
        try
        {
            var list = entries.Values.ToList();
            var json = RelaxedJsonSerializer.Serialize(list, AuthEntryContext.Default);
            await configService.SetAsync("mcp.auth_entries", json, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "保存 MCP 认证配置失败");
        }
    }

    /// <summary>
    /// 异步释放资源 — await Actor 完全退出。
    /// </summary>
    public override async ValueTask DisposeAsync()
    {
        await _actor.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// MCP 认证持久化 Actor — 串行化所有读写操作，消除显式锁 — TASK001
    /// <para>命令通过 Channel 投递，Consumer 单线程串行处理，天然无竞态。</para>
    /// </summary>
    private sealed class McpAuthPersistenceActor : ActorBase<McpAuthPersistenceCommand, Unit>
    {
        private readonly McpAuthPersistenceService _owner;
        private readonly ILogger<McpAuthPersistenceService>? _logger;

        public McpAuthPersistenceActor(McpAuthPersistenceService owner, ILogger<McpAuthPersistenceService>? logger) : base()
        {
            _owner = owner;
            _logger = logger;
        }

        /// <summary>Ask 模式等待回复 — 暴露 protected AskAwait 供 McpAuthPersistenceService 调用</summary>
        public async Task AskReplyAsync(TaskCompletionSource tcs, CancellationToken ct = default)
            => await base.AskAwait(tcs, ct).ConfigureAwait(false);

        /// <summary>Ask 模式等待回复 — 暴露 protected AskAwait 供 McpAuthPersistenceService 调用</summary>
        public async Task<T> AskReplyAsync<T>(TaskCompletionSource<T> tcs, CancellationToken ct = default)
            => await base.AskAwait(tcs, ct).ConfigureAwait(false);

        protected override async ValueTask HandleAsync(McpAuthPersistenceCommand cmd, CancellationToken ct)
        {
            switch (cmd)
            {
                case SaveAuthCmd(var authName, var authType, var serializedData, var reply):
                    try
                    {
                        await _owner.SaveInternalAsync(authName, authType, serializedData, ct).ConfigureAwait(false);
                        reply.SetResult();
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { reply.SetException(ex); }
                    break;
                case LoadAuthCmd(var authName, var reply):
                    try
                    {
                        reply.SetResult(await _owner.LoadInternalAsync(authName, ct).ConfigureAwait(false));
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { reply.SetException(ex); }
                    break;
                case ListAuthCmd(var reply):
                    try
                    {
                        reply.SetResult(await _owner.ListInternalAsync(ct).ConfigureAwait(false));
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { reply.SetException(ex); }
                    break;
                case RemoveAuthCmd(var authName, var reply):
                    try
                    {
                        await _owner.RemoveInternalAsync(authName, ct).ConfigureAwait(false);
                        reply.SetResult();
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { reply.SetException(ex); }
                    break;
            }
        }

        protected override void OnConsumerError(Exception ex)
            => _logger?.LogWarning(ex, "McpAuthPersistenceActor 命令处理异常");
    }
}
