
namespace State;

public sealed record StateServiceConfig
{
    public string DbPath { get; init; } = string.Empty;

    public static StateServiceConfig FromPath(string? stateFilePath)
    {
        return new StateServiceConfig
        {
            DbPath = DatabasePathResolver.Resolve(stateFilePath)
        };
    }
}

[Register(typeof(StateService))]
[Register(typeof(IStateService))]
[Register(typeof(IStorePersistence<AppState>))]
public sealed partial class StateService : IStateService, IStorePersistence<AppState>, IDisposable
{
    private readonly string _dbPath;
    private readonly IFileSystem _fs;
    private readonly IClockService _clock;
    [Inject] private readonly ILogger<StateService>? _logger;
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _persistenceLock;

    private const string TableName = "app_state";
    private const string StateId = "current";

    private const int MaxSqliteRetries = 5;
    private const int SqliteRetryBaseMs = 100;
    private const int SqliteRetryMaxMs = 5000;

    private static readonly StateJsonContext JsonContext = StateJsonContext.Default;

    public StateService(WorkflowConfig config, IFileSystem fs, IClockService clock, ILogger<StateService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var stateConfig = StateServiceConfig.FromPath(config.StateFilePath);
        _dbPath = stateConfig.DbPath;
        _fs = fs;
        _clock = clock;
        _logger = logger;

        var directory = Path.GetDirectoryName(_dbPath);
        DirectoryHelper.EnsureDirectoryExists(_fs, directory);

        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();

        // Enable WAL mode + concurrency tuning (aligned with IndexDbContext)
        using var pragmaCmd = _connection.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA journal_mode=WAL";
        pragmaCmd.ExecuteNonQuery();
        pragmaCmd.CommandText = "PRAGMA busy_timeout=30000";
        pragmaCmd.ExecuteNonQuery();
        pragmaCmd.CommandText = "PRAGMA journal_size_limit=67108864";
        pragmaCmd.ExecuteNonQuery();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"""
            CREATE TABLE IF NOT EXISTS {TableName} (
                id TEXT PRIMARY KEY,
                data TEXT NOT NULL,
                saved_at TEXT NOT NULL,
                version INTEGER NOT NULL DEFAULT 1
            )
            """;
        cmd.ExecuteNonQuery();

        _persistenceLock = new SemaphoreSlim(1, 1);

        _logger?.LogInformation(L.T(StringKey.VaultLogStateServiceInitialized), _dbPath);
    }

    #region IStateService Implementation

    public void SaveState(string systemPrompt, MessageList chatHistory)
    {
        try
        {
            var existingDoc = FindById(StateId);
            var state = existingDoc != null
                ? AppStateConverter.FromDocument(existingDoc)
                : AppState.Default;

            var updatedState = state with
            {
                Session = state.Session with
                {
                    SystemPrompt = systemPrompt,
                    MessageList = chatHistory
                        .Select(m => new ApiMessageState
                        {
                            // 使用 ToValue() 保存小写角色名（对齐 [EnumValue] 定义），
                            // 避免 ToString() 返回 PascalCase 导致 FromValue() 反查失败
                            Role = m.Role.ToValue(),
                            Content = m.Content ?? string.Empty,
                            Timestamp = _clock.GetUtcNow(),
                            // 保存 Metadata（JsonElement → string），避免工具调用信息丢失导致前缀缓存破坏
                            Metadata = SerializeMetadata(m.Metadata)
                        })
                        .ToList()
                        .ToImmutableList()
                }
            };

            var document = AppStateConverter.ToDocument(updatedState, _clock.GetUtcNow());
            Upsert(document);
            _logger?.LogInformation(L.T(StringKey.VaultLogStateSaveSuccess));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.VaultLogStateSaveFailed));
        }
    }

    /// <summary>
    /// 将 ApiMessage.Metadata（IReadOnlyDictionary&lt;string, JsonElement&gt;）序列化为
    /// ApiMessageState.Metadata（ImmutableDictionary&lt;string, string&gt;）。
    /// JsonElement.GetRawText() 保留完整 JSON 结构，加载时反序列化回 JsonElement。
    /// </summary>
    private static ImmutableDictionary<string, string> SerializeMetadata(IReadOnlyDictionary<string, JsonElement>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
            return ImmutableDictionary<string, string>.Empty;

        var builder = ImmutableDictionary.CreateBuilder<string, string>();
        foreach (var kvp in metadata)
        {
            // GetRawText() 对所有 ValueKind 都有效：String 返回带引号的字符串，Array/Object 返回原始 JSON
            builder[kvp.Key] = kvp.Value.GetRawText();
        }
        return builder.ToImmutable();
    }

    /// <summary>
    /// 将 ApiMessageState.Metadata（IReadOnlyDictionary&lt;string, string&gt;）反序列化为
    /// ApiMessage.Metadata（IReadOnlyDictionary&lt;string, JsonElement&gt;）。
    /// 使用 JsonDocument.Parse 解析回 JsonElement，Clone() 确保脱离 JsonDocument 生命周期。
    /// </summary>
    private static IReadOnlyDictionary<string, JsonElement>? DeserializeMetadata(IReadOnlyDictionary<string, string>? stored)
    {
        if (stored is null || stored.Count == 0) return null;

        var dict = new Dictionary<string, JsonElement>(stored.Count);
        foreach (var kvp in stored)
        {
            // 存储时用 GetRawText()，可能是带引号的字符串或原始 JSON
            dict[kvp.Key] = JsonDocument.Parse(kvp.Value).RootElement.Clone();
        }
        return dict;
    }

    public Task SaveStateAsync(string systemPrompt, MessageList chatHistory, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => SaveState(systemPrompt, chatHistory), cancellationToken);
    }

    public (string SystemPrompt, MessageList MessageList) LoadState()
    {
        try
        {
            var document = FindById(StateId);
            if (document != null)
            {
                var chatHistory = new MessageList();
                // 去重: 基于非空 Content 去重，相同内容保留 Role 优先级更高的版本（Tool > Assistant > User > System）
                // 原因: 早期版本可能把 Tool 消息保存为 User 角色，导致同一内容出现两次（user + tool）
                // 注意: 空 content 不去重（assistant 工具调用消息 content 为 null/空，但有不同 metadata）
                var seenContent = new HashSet<string>(StringComparer.Ordinal);
                var rolePriority = new Dictionary<MessageRole, int>
                {
                    [MessageRole.System] = 0,
                    [MessageRole.User] = 1,
                    [MessageRole.Assistant] = 2,
                    [MessageRole.Tool] = 3,
                };

                foreach (var message in document.Session.MessageList)
                {
                    // FromValue 返回可空，未知角色回退为 User（对齐原 default 分支行为）
                    var role = MessageRoleExtensions.FromValue(message.Role) ?? MessageRole.User;
                    // 恢复 Metadata（string → JsonElement），避免工具调用信息丢失导致前缀缓存破坏
                    var metadata = DeserializeMetadata(message.Metadata);

                    var content = message.Content ?? string.Empty;
                    var isToolMessage = role == MessageRole.Tool;
                    // 只对非空 content 去重（空 content 的 assistant 工具调用消息有不同 metadata，不应合并）
                    if (!string.IsNullOrEmpty(content) && seenContent.Contains(content))
                    {
                        // 已存在相同内容：检查是否需要替换为更高优先级的角色
                        var replaced = false;
                        for (var i = 0; i < chatHistory.Count; i++)
                        {
                            if ((chatHistory[i].Content ?? string.Empty) == content)
                            {
                                var existingPriority = rolePriority.GetValueOrDefault(chatHistory[i].Role, 0);
                                var newPriority = rolePriority.GetValueOrDefault(role, 0);
                                if (newPriority > existingPriority)
                                {
                                    // 替换为更高优先级的角色（如 Tool 替换 User — 修复 legacy 数据）
                                    chatHistory[i] = new ApiMessage(role, content, metadata);
                                    replaced = true;
                                }
                                break;
                            }
                        }
                        // Tool 消息只有在未替换低优先级消息时才作为独立消息添加
                        // （同内容不同 tool_call_id 的 Tool 结果必须保留，否则会导致孤立 tool_call → API 400 + 永久数据丢失）
                        // 非 Tool 消息或已替换的消息跳过添加（去重）
                        if (!isToolMessage || replaced)
                            continue;
                        // Tool 消息未替换（如 Tool+Tool 同内容不同 tool_call_id）→ 作为独立消息保留
                    }

                    if (!string.IsNullOrEmpty(content) && !seenContent.Contains(content))
                        seenContent.Add(content);
                    // 直接构造 ApiMessage 保留 Metadata，不使用 AddSystemMessage 等便捷方法（它们不传 Metadata）
                    chatHistory.Add(new ApiMessage(role, content, metadata));
                }
                _logger?.LogInformation(L.T(StringKey.VaultLogStateLoadSuccess));
                return (document.Session.SystemPrompt, chatHistory);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.VaultLogStateLoadFailed));
        }

        return (string.Empty, new MessageList());
    }

    public Task<(string SystemPrompt, MessageList MessageList)> LoadStateAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => LoadState(), cancellationToken);
    }

    public bool ClearState()
    {
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"DELETE FROM {TableName} WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", StateId);
            var result = cmd.ExecuteNonQuery() > 0;
            if (result)
            {
                _logger?.LogInformation(L.T(StringKey.VaultLogStateClearSuccess));
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.VaultLogStateClearFailed));
            return false;
        }
    }

    public Task<bool> ClearStateAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ClearState(), cancellationToken);
    }

    #endregion

    #region IStorePersistence<AppState> Implementation

    public async Task SaveAsync(AppState state, CancellationToken cancellationToken = default)
    {
        await _persistenceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = AppStateConverter.ToDocument(state, _clock.GetUtcNow());
            await ExecuteWithSqliteRetryAsync(() => Task.Run(() => Upsert(document), cancellationToken), cancellationToken).ConfigureAwait(false);
            _logger?.LogDebug(L.T(StringKey.VaultLogStatePersisted));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.VaultLogPersistFailed));
            throw;
        }
        finally
        {
            _persistenceLock.Release();
        }
    }

    public async Task<AppState?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _persistenceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = FindById(StateId);
            if (document == null)
            {
                _logger?.LogInformation(L.T(StringKey.VaultLogNoPersistedState));
                return null;
            }

            var loadedState = AppStateConverter.FromDocument(document);
            _logger?.LogInformation(L.T(StringKey.VaultLogStateLoadedFromPersist));
            return loadedState;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.VaultLogStateLoadFromPersistFailed));
            return null;
        }
        finally
        {
            _persistenceLock.Release();
        }
    }

    #endregion

    private AppStateDocument? FindById(string id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"SELECT data FROM {TableName} WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        var json = cmd.ExecuteScalar() as string;
        if (json == null) return null;

        return JsonSerializer.Deserialize(json, JsonContext.AppStateDocument);
    }

    private void Upsert(AppStateDocument document)
    {
        var json = JsonSerializer.Serialize(document, JsonContext.AppStateDocument);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {TableName} (id, data, saved_at, version)
            VALUES (@id, @data, @savedAt, @version)
            ON CONFLICT(id) DO UPDATE SET data = @data, saved_at = @savedAt, version = @version
            """;
        cmd.Parameters.AddWithValue("@id", document.Id);
        cmd.Parameters.AddWithValue("@data", json);
        cmd.Parameters.AddWithValue("@savedAt", _clock.GetUtcNowOffset().ToString("O"));
        cmd.Parameters.AddWithValue("@version", document.Version);

        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Retries a SQLite command with exponential backoff on SQLITE_BUSY / SqliteException.
    /// </summary>
    private static void ExecuteWithSqliteRetry(Action action)
    {
        for (var attempt = 0; attempt <= MaxSqliteRetries; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (IsSqliteBusy(ex))
            {
                if (attempt == MaxSqliteRetries)
                {
                    throw;
                }

                var delay = SqliteRetryBaseMs * (1 << attempt);
                delay = Math.Min(delay, SqliteRetryMaxMs);
                Task.Delay(delay).Wait();
            }
        }
    }

    private static Task ExecuteWithSqliteRetryAsync(Func<Task> action, CancellationToken ct)
    {
        // Delegate to the async retry helper - run on thread pool
        return Task.Run(async () =>
        {
            for (var attempt = 0; attempt <= MaxSqliteRetries; attempt++)
            {
                try
                {
                    await action().ConfigureAwait(false);
                    return;
                }
                catch (Microsoft.Data.Sqlite.SqliteException ex) when (IsSqliteBusy(ex))
                {
                    if (attempt == MaxSqliteRetries)
                    {
                        throw;
                    }

                    var delay = SqliteRetryBaseMs * (1 << attempt);
                    delay = Math.Min(delay, SqliteRetryMaxMs);
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
            }
        }, ct);
    }

    private static bool IsSqliteBusy(Microsoft.Data.Sqlite.SqliteException ex)
    {
        return ex.SqliteErrorCode is (5 or 6 or 11 or 26);
    }

    public void Dispose()
    {
        _persistenceLock.Dispose();
        _connection?.Close();
        _connection?.Dispose();
    }
}
