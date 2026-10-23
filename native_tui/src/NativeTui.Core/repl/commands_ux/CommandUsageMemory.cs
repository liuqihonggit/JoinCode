namespace NativeTui.Core.Repl;

/// <summary>命令使用快照 — 单命令的使用计数与最近使用时间（LINQ 链式查询友好形态）。</summary>
/// <param name="Count">使用计数（&gt; 0）。</param>
/// <param name="LastUsed">最近使用时间（UTC）。</param>
public sealed record CommandUsageView(int Count, DateTimeOffset LastUsed);

/// <summary>
/// 命令使用记忆 — 空查询分组视图的 recently-used 数据源（基准 commandSuggestions.ts getSkillUsageScore）：
/// 使用计数 + 最近使用时间戳；容量上限 LRU 淘汰（最旧 lastUsed 先出局，刷新保活）；
/// JSON 持久化到 ~/.jcc/command-history.json（DTO 双向转换经 CommandUsageJsonContext，
/// 坏文件静默降级空记忆 — 历史不阻塞启动，HistorySeeder 先例）。
/// </summary>
public sealed class CommandUsageMemory
{
    /// <summary>默认容量（LRU 上限）。</summary>
    public const int DefaultCapacity = 32;

    /// <summary>默认持久化路径（~/.jcc/command-history.json — 经 JccHome 跟随 JCC_APP_DATA_FOLDER 覆盖，测试隔离不污染真实用户目录）。</summary>
    public static string DefaultFilePath => JccHome.Combine("command-history.json");

    private readonly int _capacity;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, CommandUsageView> _entries = new(StringComparer.Ordinal);

    /// <summary>创建空记忆 — capacity 为 LRU 容量上限；timeProvider 为时间注入面（测试确定性，默认系统时钟）。</summary>
    public CommandUsageMemory(int capacity = DefaultCapacity, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>当前记录条数。</summary>
    public int Count => _entries.Count;

    /// <summary>记录一次使用 — 计数 +1、刷新最近使用时间（LRU 保活）；超容量时按最近使用时间最旧淘汰。</summary>
    public void RecordUse(string commandName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        var now = _timeProvider.GetUtcNow();
        if (_entries.TryGetValue(commandName, out var existing))
        {
            _entries[commandName] = existing with { Count = existing.Count + 1, LastUsed = now };
        }
        else
        {
            _entries[commandName] = new CommandUsageView(1, now);
        }

        EvictBeyondCapacity();
    }

    /// <summary>查询使用快照 — 未记录返回 null。</summary>
    public CommandUsageView? TryGetUsage(string commandName) =>
        _entries.TryGetValue(commandName, out var usage) ? usage : null;

    /// <summary>快照清单（使用计数降序 → 最近使用降序 → 名字字母序 — recently-used 分组排序同源）。</summary>
    public IReadOnlyList<(string Name, CommandUsageView Usage)> Snapshot() =>
        _entries.Select(entry => (entry.Key, entry.Value))
            .OrderByDescending(pair => pair.Value.Count)
            .ThenByDescending(pair => pair.Value.LastUsed)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .ToList();

    /// <summary>从 JSON 文件加载 — 文件不存在/损坏/字段异常均降级为空记忆（历史数据不阻塞启动）。</summary>
    public static async Task<CommandUsageMemory> LoadAsync(
        string path,
        int capacity = DefaultCapacity,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        var memory = new CommandUsageMemory(capacity, timeProvider);
        try
        {
            if (!File.Exists(path))
            {
                return memory;
            }

            await using var stream = File.OpenRead(path);
            var file = await JsonSerializer.DeserializeAsync(
                stream, CommandUsageJsonContext.Default.CommandUsageFileDto, cancellationToken);
            foreach (var entry in file?.Entries ?? [])
            {
                memory.MergeEntry(entry);
            }

            memory.EvictBeyondCapacity();
            return memory;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            return new CommandUsageMemory(capacity, timeProvider);
        }
    }

    /// <summary>持久化到 JSON 文件 — 临时文件 + 原子替换（防半写损坏）；父目录不存在时创建。</summary>
    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var dto = new CommandUsageFileDto(
            _entries.Select(entry => new CommandUsageEntryDto(entry.Key, entry.Value.Count, entry.Value.LastUsed))
                .OrderByDescending(entry => entry.Count)
                .ThenByDescending(entry => entry.LastUsed)
                .ToList());
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(
                stream, dto, CommandUsageJsonContext.Default.CommandUsageFileDto, cancellationToken);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    /// <summary>合并持久化条目 — 同名取计数较大者（重复记录防倒退）；空名/非正计数跳过。</summary>
    private void MergeEntry(CommandUsageEntryDto entry)
    {
        if (string.IsNullOrEmpty(entry.Name) || entry.Count <= 0)
        {
            return;
        }

        if (_entries.TryGetValue(entry.Name, out var existing) && existing.Count >= entry.Count)
        {
            return;
        }

        _entries[entry.Name] = new CommandUsageView(entry.Count, entry.LastUsed);
    }

    /// <summary>LRU 淘汰 — 超容量时按最近使用时间最旧出局（时间并列取计数低者）。</summary>
    private void EvictBeyondCapacity()
    {
        if (_entries.Count <= _capacity)
        {
            return;
        }

        foreach (var victim in _entries.OrderBy(entry => entry.Value.LastUsed)
            .ThenBy(entry => entry.Value.Count)
            .Take(_entries.Count - _capacity)
            .Select(entry => entry.Key)
            .ToList())
        {
            _entries.Remove(victim);
        }
    }
}
