
namespace Core.Memdir;

/// <summary>
/// 记忆扫描器接口
/// 发现和索引记忆文件
/// </summary>
public interface IMemoryScanner
{
    /// <summary>
    /// 扫描所有记忆
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>记忆条目列表</returns>
    Task<IReadOnlyList<MemoryEntry>> ScanAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 扫描特定类型的记忆
    /// </summary>
    /// <param name="type">记忆类型</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>记忆条目列表</returns>
    Task<IReadOnlyList<MemoryEntry>> ScanByTypeAsync(MemoryType type, CancellationToken cancellationToken = default);

    /// <summary>
    /// 扫描特定目录的记忆
    /// </summary>
    /// <param name="directory">目录路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>记忆条目列表</returns>
    Task<IReadOnlyList<MemoryEntry>> ScanDirectoryAsync(string directory, CancellationToken cancellationToken = default);
}

/// <summary>
/// 记忆扫描器实现
/// </summary>
[Register]
public sealed partial class MemoryScanner : IMemoryScanner
{
    private readonly IMemoryPaths _memoryPaths;
    [Inject] private readonly ILogger<MemoryScanner>? _logger;
    private readonly IFileSystem _fs;

    public MemoryScanner(IFileSystem fs, IMemoryPaths memoryPaths, ILogger<MemoryScanner>? logger = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _memoryPaths = memoryPaths ?? throw new ArgumentNullException(nameof(memoryPaths));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryEntry>> ScanAllAsync(CancellationToken cancellationToken = default)
    {
        var allMemories = new ConcurrentBag<MemoryEntry>();

        // 扫描所有记忆类型
        var types = Enum.GetValues<MemoryType>();
        var tasks = types.Select(type => Task.Run(async () =>
        {
            var memories = await ScanByTypeAsync(type, cancellationToken).ConfigureAwait(false);
            foreach (var memory in memories)
            {
                allMemories.Add(memory);
            }
        }, cancellationToken));

        await Task.WhenAll(tasks).ConfigureAwait(false);

        var result = allMemories.ToImmutableList();
        _logger?.LogInformation("Scanned {Count} memories from all types", result.Count);

        return result;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MemoryEntry>> ScanByTypeAsync(MemoryType type, CancellationToken cancellationToken = default)
    {
        var directory = _memoryPaths.GetMemoryDirectoryByType(type);
        return ScanDirectoryAsync(directory, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryEntry>> ScanDirectoryAsync(string directory, CancellationToken cancellationToken = default)
    {
        if (!_fs.DirectoryExists(directory))
        {
            _logger?.LogDebug("Memory directory does not exist: {Directory}", directory);
            return ImmutableList<MemoryEntry>.Empty;
        }

        var memories = new ConcurrentBag<MemoryEntry>();
        var files = _fs.GetFiles(directory, "*.json", SearchOption.AllDirectories);

        _logger?.LogDebug("Scanning {Count} memory files in {Directory}", files.Length, directory);

        var tasks = files.Select(file => Task.Run(async () =>
        {
            try
            {
                var memory = await LoadMemoryAsync(file, cancellationToken).ConfigureAwait(false);
                if (memory != null)
                {
                    memories.Add(memory);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load memory from {File}", file);
            }
        }, cancellationToken));

        await Task.WhenAll(tasks).ConfigureAwait(false);

        return memories.ToImmutableList();
    }

    /// <summary>
    /// 从文件加载记忆
    /// </summary>
    private async Task<MemoryEntry?> LoadMemoryAsync(string filePath, CancellationToken cancellationToken)
    {
        var memory = await _fs.ReadAndDeserializeAsync(filePath, MemdirJsonContext.Default.MemoryEntry, cancellationToken).ConfigureAwait(false);

        if (memory == null)
        {
            return null;
        }

        // 验证文件路径与记忆 ID 匹配
        var expectedFileName = $"{memory.Id}.json";
        var actualFileName = Path.GetFileName(filePath);

        if (!string.Equals(expectedFileName, actualFileName, StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogWarning(
                "Memory ID mismatch: expected {Expected}, found {Actual} in {File}",
                expectedFileName, actualFileName, filePath);
        }

        return memory;
    }

    /// <summary>
    /// 索引记忆（建立快速查找索引）
    /// </summary>
    public MemoryIndex BuildIndex(IEnumerable<MemoryEntry> memories)
    {
        var index = new MemoryIndex();

        foreach (var memory in memories)
        {
            // 按类型索引
            index.ByType.AddOrUpdate(
                memory.Type,
                new List<MemoryEntry> { memory },
                (_, list) => { list.Add(memory); return list; });

            // 按标签索引
            foreach (var tag in memory.Tags)
            {
                index.ByTag.AddOrUpdate(
                    tag,
                    new List<MemoryEntry> { memory },
                    (_, list) => { list.Add(memory); return list; });
            }

            // 按来源索引
            if (!string.IsNullOrEmpty(memory.Source))
            {
                index.BySource.AddOrUpdate(
                    memory.Source,
                    new List<MemoryEntry> { memory },
                    (_, list) => { list.Add(memory); return list; });
            }
        }

        _logger?.LogInformation(
            "Built memory index: {TypeCount} types, {TagCount} tags, {SourceCount} sources",
            index.ByType.Count,
            index.ByTag.Count,
            index.BySource.Count);

        return index;
    }
}

/// <summary>
/// 记忆索引
/// </summary>
public sealed partial class MemoryIndex
{
    /// <summary>
    /// 按类型索引
    /// </summary>
    public ConcurrentDictionary<MemoryType, List<MemoryEntry>> ByType { get; } = new();

    /// <summary>
    /// 按标签索引
    /// </summary>
    public ConcurrentDictionary<string, List<MemoryEntry>> ByTag { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 按来源索引
    /// </summary>
    public ConcurrentDictionary<string, List<MemoryEntry>> BySource { get; } = new();

    /// <summary>
    /// 查找特定类型的记忆
    /// </summary>
    public IReadOnlyList<MemoryEntry> FindByType(MemoryType type)
    {
        return ByType.TryGetValue(type, out var list)
            ? list.AsReadOnly()
            : Array.Empty<MemoryEntry>();
    }

    /// <summary>
    /// 查找特定标签的记忆
    /// </summary>
    public IReadOnlyList<MemoryEntry> FindByTag(string tag)
    {
        return ByTag.TryGetValue(tag, out var list)
            ? list.AsReadOnly()
            : Array.Empty<MemoryEntry>();
    }

    /// <summary>
    /// 查找特定来源的记忆
    /// </summary>
    public IReadOnlyList<MemoryEntry> FindBySource(string source)
    {
        return BySource.TryGetValue(source, out var list)
            ? list.AsReadOnly()
            : Array.Empty<MemoryEntry>();
    }
}

/// <summary>
/// ConcurrentDictionary 扩展
/// </summary>
internal static class ConcurrentDictionaryExtensions
{
    public static TValue AddOrUpdate<TKey, TValue>(
        this ConcurrentDictionary<TKey, TValue> dictionary,
        TKey key,
        TValue addValue,
        Func<TKey, TValue, TValue> updateValueFactory)
        where TKey : notnull
    {
        return dictionary.AddOrUpdate(key, addValue, updateValueFactory);
    }
}
