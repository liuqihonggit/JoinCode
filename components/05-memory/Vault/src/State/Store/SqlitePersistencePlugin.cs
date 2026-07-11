
namespace State;

public sealed partial class SqlitePersistencePlugin : IStorePersistence<AppState>, IDisposable
{
    private readonly string _dbPath;
    private readonly IFileSystem _fs;
    private readonly IClockService _clock;
    [Inject] private readonly ILogger<SqlitePersistencePlugin>? _logger;
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _persistenceLock;

    private const string TableName = "app_state";
    private const string StateId = "current";

    private const int MaxSqliteRetries = 5;
    private const int SqliteRetryBaseMs = 100;
    private const int SqliteRetryMaxMs = 5000;

    private static readonly StateJsonContext JsonContext = StateJsonContext.Default;

    public SqlitePersistencePlugin(string dbPath, IFileSystem fs, IClockService clock, ILogger<SqlitePersistencePlugin>? logger = null)
    {
        _dbPath = dbPath ?? throw new ArgumentNullException(nameof(dbPath));
        _fs = fs;
        _clock = clock;
        _logger = logger;

        var directory = Path.GetDirectoryName(dbPath);
        DirectoryHelper.EnsureDirectoryExists(_fs, directory);

        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();

        // Enable WAL mode + concurrency tuning (aligned with IndexDbContext + StateService)
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

        _logger?.LogInformation(L.T(StringKey.VaultLogSqliteInitialized), dbPath);
    }

    public static SqlitePersistencePlugin FromConfig(
        string? configuredPath,
        IFileSystem fs,
        IClockService clock,
        ILogger<SqlitePersistencePlugin>? logger = null)
    {
        var dbPath = DatabasePathResolver.Resolve(configuredPath);
        return new SqlitePersistencePlugin(dbPath, fs, clock, logger);
    }

    public async Task SaveAsync(AppState state, CancellationToken cancellationToken = default)
    {
        await _persistenceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = AppStateConverter.ToDocument(state, _clock.GetUtcNow());
            var json = JsonSerializer.Serialize(document, JsonContext.AppStateDocument);

            await ExecuteWithSqliteRetryAsync(async () =>
            {
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
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);

            _logger?.LogDebug(L.T(StringKey.VaultLogSqlitePersisted));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.VaultLogSqlitePersistFailed));
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
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT data FROM {TableName} WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", StateId);

            var json = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
            if (json == null)
            {
                _logger?.LogInformation(L.T(StringKey.VaultLogSqliteNoState));
                return null;
            }

            var document = JsonSerializer.Deserialize(json, JsonContext.AppStateDocument);
            if (document == null)
            {
                _logger?.LogWarning(L.T(StringKey.VaultLogSqliteDeserializeFailed));
                return null;
            }

            var loadedState = AppStateConverter.FromDocument(document);
            _logger?.LogInformation(L.T(StringKey.VaultLogSqliteLoaded));
            return loadedState;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.VaultLogSqliteLoadFailed));
            return null;
        }
        finally
        {
            _persistenceLock.Release();
        }
    }

    /// <summary>
    /// Retries a SQLite command with exponential backoff on SQLITE_BUSY / SqliteException.
    /// </summary>
    private static async Task ExecuteWithSqliteRetryAsync(Func<Task> action, CancellationToken ct)
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
