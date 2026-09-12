namespace JoinCode.CodeIndex.Threading;

/// <summary>
/// Singleton pool that reuses <see cref="TreeSitterParser"/> instances to avoid concurrent <c>new()</c> calls.
/// The pool lazily creates parsers on first use and reuses a fixed set for the lifetime of the application.
/// </summary>
public static class TreeSitterParserPool
{
    private static readonly Lazy<TreeSitterParser> _lazyParser = new(
        static () => new TreeSitterParser("c-sharp"),
        isThreadSafe: true);

    /// <summary>
    /// Global lock protecting <see cref="Shared"/> because TreeSitter Parser is NOT thread-safe.
    /// Different CSharpSymbolExtractor instances have their own _parseLock but all access Shared.Parse().
    /// </summary>
    private static readonly AsyncLock _sharedLock = new("TreeSitterParserPool.Shared");

    /// <summary>
    /// Gets a shared, process-wide parser instance. The same parser is returned for the lifetime
    /// of the application. Callers must NOT dispose it.
    /// </summary>
    public static TreeSitterParser Shared => _lazyParser.Value;

    /// <summary>
    /// Acquires exclusive access to the shared parser. Dispose the returned value to release.
    /// </summary>
    public static Task<IDisposable> AcquireSharedAsync(CancellationToken ct = default)
    {
        var guard = _sharedLock.TryLock(ct) ?? throw new System.TimeoutException($"锁 '{_sharedLock.Name}' 等待超时");
        return Task.FromResult(guard);
    }

    /// <summary>
    /// Acquires exclusive access to the shared parser synchronously. Dispose the returned value to release.
    /// </summary>
    public static IDisposable AcquireShared()
    {
        return _sharedLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_sharedLock.Name}' 等待超时");
    }

    /// <summary>
    /// Creates a disposable parser instance. Use only in parallel paths where the pool's
    /// single shared parser would become a bottleneck. The returned instance MUST be disposed.
    /// </summary>
    public static TreeSitterParser CreateDisposable() => new("c-sharp");

}
