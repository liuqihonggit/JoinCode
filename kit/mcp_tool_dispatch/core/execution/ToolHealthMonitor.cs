namespace McpToolDispatch;

/// <summary>
/// 工具健康监控命令 — Actor 消息类型
/// </summary>
public interface IToolHealthCommand;

public sealed record RecordSuccessCmd(string ToolName, TaskCompletionSource<ToolHealthRecord> Tcs) : IToolHealthCommand;
public sealed record RecordFailureCmd(string ToolName, string? ErrorMessage, TaskCompletionSource<ToolHealthRecord> Tcs) : IToolHealthCommand;
public sealed record ResetToolCmd(string ToolName, TaskCompletionSource Tcs) : IToolHealthCommand;
public sealed record DecayTickCmd : IToolHealthCommand;

/// <summary>
/// 工具健康监控服务 — Actor 化：复合操作（RecordSuccess/RecordFailure/Decay）由 Consumer 串行处理，消除 AsyncLock。
/// <para>_records 保留 ConcurrentDictionary 供 GetEffectiveScore 等读多写少方法直接读取（最终一致性）。</para>
/// <para>_blacklistSnapshot/_penalties 保留 volatile 双变量切换。</para>
/// 设计原则：永远不禁用工具，连续失败只注入提示词提醒LLM换策略
/// </summary>
[Register(typeof(IToolHealthMonitor), ServiceLifetime.Singleton)]
public sealed class ToolHealthMonitor : ActorBase<IToolHealthCommand, Unit>, IToolHealthMonitor, IDisposable
{
    private readonly ILogger<ToolHealthMonitor>? _logger;
    private readonly IFileSystem _fs;
    private readonly ToolScoreConfig _config;
    internal ToolScoreConfig Config => _config;
    private readonly ConcurrentDictionary<string, ToolHealthRecord> _records = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _configPath;
    private readonly Timer? _decayTimer;
    private volatile BlacklistSnapshot _blacklistSnapshot;
    private volatile Dictionary<string, int> _penalties;
    private int _disposed;

    private sealed record BlacklistSnapshot
    {
        public required FrozenSet<string> Exact { get; init; }
        public required FrozenSet<string> Patterns { get; init; }
    }

    public ToolHealthMonitor(IFileSystem fs, ILogger<ToolHealthMonitor>? logger = null, ToolScoreConfig? config = null,
        HashSet<string>? blacklist = null, Dictionary<string, int>? penalties = null)
        : base()
    {
        _fs = fs;
        _logger = logger;
        _config = config ?? new ToolScoreConfig();
        var bl = blacklist ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _blacklistSnapshot = new BlacklistSnapshot
        {
            Exact = bl.Where(b => !b.Contains('*')).ToFrozenSet(StringComparer.OrdinalIgnoreCase),
            Patterns = bl.Where(b => b.Contains('*')).ToFrozenSet(StringComparer.OrdinalIgnoreCase)
        };
        _penalties = penalties ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        _configPath = Path.Combine(
            JoinCode.Abstractions.Configuration.AppData.AppDataConstants.JccDirectory,
            "tool-health.json");
        LoadFromDisk();

        _decayTimer = new Timer(_ => TrySend(new DecayTickCmd()), null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
    }

    private static TaskCompletionSource<T> CreateTcs<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource CreateTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// 热更新黑名单 — 双变量切换模式：构建新快照 → 原子替换引用
    /// </summary>
    public void UpdateBlacklist(HashSet<string> newBlacklist)
    {
        var snapshot = new BlacklistSnapshot
        {
            Exact = newBlacklist.Where(b => !b.Contains('*')).ToFrozenSet(StringComparer.OrdinalIgnoreCase),
            Patterns = newBlacklist.Where(b => b.Contains('*')).ToFrozenSet(StringComparer.OrdinalIgnoreCase)
        };
        _blacklistSnapshot = snapshot;
        _logger?.LogInformation("黑名单已热更新: {Exact} 个精确匹配, {Pattern} 个通配符模式",
            snapshot.Exact.Count, snapshot.Patterns.Count);
    }

    /// <summary>
    /// 热更新降权配置 — 双变量切换模式：构建新字典 → 原子替换引用
    /// </summary>
    public void UpdatePenalties(Dictionary<string, int> newPenalties)
    {
        _penalties = new Dictionary<string, int>(newPenalties, StringComparer.OrdinalIgnoreCase);
        _logger?.LogInformation("降权配置已热更新: {Count} 条规则", newPenalties.Count);
    }

    public bool IsBlacklisted(string toolName)
    {
        var snapshot = _blacklistSnapshot;
        if (snapshot.Exact.Contains(toolName)) return true;

        foreach (var pattern in snapshot.Patterns)
        {
            if (MatchesPattern(pattern, toolName)) return true;
        }

        return false;
    }

    /// <summary>
    /// 简单通配符匹配 — 支持 * 通配任意字符
    /// </summary>
    private static bool MatchesPattern(string pattern, string toolName)
    {
        var parts = pattern.Split('*');
        if (parts.Length == 1) return string.Equals(parts[0], toolName, StringComparison.OrdinalIgnoreCase);

        if (!toolName.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase)) return false;
        if (!toolName.EndsWith(parts[^1], StringComparison.OrdinalIgnoreCase)) return false;

        var idx = parts[0].Length;
        for (var i = 1; i < parts.Length - 1; i++)
        {
            var pos = toolName.IndexOf(parts[i], idx, StringComparison.OrdinalIgnoreCase);
            if (pos < 0) return false;
            idx = pos + parts[i].Length;
        }

        return true;
    }

    public int GetPenalty(string toolName)
    {
        var penalties = _penalties;
        if (penalties.TryGetValue(toolName, out var penalty)) return penalty;

        foreach (var kvp in penalties)
        {
            if (kvp.Key.Contains('*') && MatchesPattern(kvp.Key, toolName))
                return kvp.Value;
        }

        return 0;
    }

    public int GetEffectiveScore(string toolName)
    {
        if (IsBlacklisted(toolName)) return _config.ScoreMin;
        _records.TryGetValue(toolName, out var record);
        var baseScore = record?.Score ?? 0;
        return Math.Clamp(baseScore + GetPenalty(toolName), _config.ScoreMin, _config.ScoreMax);
    }

    public async Task<ToolHealthRecord> RecordSuccessAsync(string toolName, CancellationToken ct = default)
    {
        var tcs = CreateTcs<ToolHealthRecord>();
        await SendAsync(new RecordSuccessCmd(toolName, tcs), ct).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    public async Task<ToolHealthRecord> RecordFailureAsync(string toolName, string? errorMessage, CancellationToken ct = default)
    {
        var tcs = CreateTcs<ToolHealthRecord>();
        await SendAsync(new RecordFailureCmd(toolName, errorMessage, tcs), ct).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    public Task<ToolHealthRecord?> GetRecordAsync(string toolName, CancellationToken ct = default)
    {
        _records.TryGetValue(toolName, out var record);
        return Task.FromResult(record);
    }

    public Task<IReadOnlyDictionary<string, ToolHealthRecord>> GetAllRecordsAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyDictionary<string, ToolHealthRecord>>(_records.ToFrozenDictionary());
    }

    public async Task ResetToolAsync(string toolName, CancellationToken ct = default)
    {
        var tcs = CreateTcs();
        await SendAsync(new ResetToolCmd(toolName, tcs), ct).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    protected override async ValueTask HandleAsync(IToolHealthCommand command, CancellationToken ct)
    {
        switch (command)
        {
            case RecordSuccessCmd success:
                {
                    var record = GetOrCreate(success.ToolName);
                    record.Score = Math.Clamp(record.Score + _config.SuccessDelta, _config.ScoreMin, _config.ScoreMax);
                    record.SuccessCount++;
                    record.ConsecutiveFailures = 0;
                    record.LastAdjusted = DateTime.UtcNow;
                    record.LastErrorMessage = null;
                    SaveToDisk();
                    success.Tcs.TrySetResult(record);
                }
                break;

            case RecordFailureCmd failure:
                {
                    var record = GetOrCreate(failure.ToolName);
                    record.Score = Math.Clamp(record.Score + _config.FailDelta, _config.ScoreMin, _config.ScoreMax);
                    record.FailCount++;
                    record.ConsecutiveFailures++;
                    record.LastAdjusted = DateTime.UtcNow;
                    record.LastErrorMessage = failure.ErrorMessage;

                    if (record.ConsecutiveFailures >= _config.WarningThreshold)
                    {
                        _logger?.LogWarning("工具 {ToolName} 连续失败 {Count} 次，评分 {Score}，将在下次调用时注入提示词",
                            failure.ToolName, record.ConsecutiveFailures, record.Score);
                    }

                    SaveToDisk();
                    failure.Tcs.TrySetResult(record);
                }
                break;

            case ResetToolCmd reset:
                {
                    if (_records.TryGetValue(reset.ToolName, out var record))
                    {
                        record.Score = 0;
                        record.ConsecutiveFailures = 0;
                        record.IsEnabled = true;
                        record.LastAdjusted = DateTime.UtcNow;
                        SaveToDisk();
                    }
                    reset.Tcs.TrySetResult();
                }
                break;

            case DecayTickCmd:
                ApplyTimeDecay();
                break;
        }
    }

    protected override void OnConsumerError(Exception ex)
    {
        _logger?.LogError(ex, "[ToolHealthMonitor] 消费者异常");
    }

    private ToolHealthRecord GetOrCreate(string toolName)
    {
        return _records.GetOrAdd(toolName, _ => new ToolHealthRecord { ToolName = toolName });
    }

    private void ApplyTimeDecay()
    {
        var now = DateTime.UtcNow;
        foreach (var record in _records.Values)
        {
            if (!record.IsEnabled) continue;

            var idleHours = (now - record.LastAdjusted).TotalHours;
            if (idleHours < 1) continue;

            var decay = (int)Math.Floor(idleHours * _config.DecayRatePerHour * _config.DecayRecoveryScore);
            if (record.Score < 0 && decay > 0)
            {
                record.Score = Math.Min(0, record.Score + decay);
            }
        }

        SaveToDisk();
    }

    private void LoadFromDisk()
    {
        try
        {
            if (!_fs.FileExists(_configPath)) return;
            var json = _fs.ReadAllText(_configPath);
            var data = RelaxedJsonSerializer.Deserialize(json, ToolHealthJsonContext.Default.DictionaryStringToolHealthRecord);
            if (data is null) return;

            foreach (var kvp in data)
                _records[kvp.Key] = kvp.Value;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "加载工具健康记录失败，使用空记录");
        }
    }

    private void SaveToDisk()
    {
        try
        {
            var dir = Path.GetDirectoryName(_configPath)!;
            if (!_fs.DirectoryExists(dir)) _fs.CreateDirectory(dir);
            var dict = _records.ToDictionary();
            var json = JsonSerializer.Serialize(dict, ToolHealthJsonContext.Default.DictionaryStringToolHealthRecord);
            _fs.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "保存工具健康记录失败");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _decayTimer?.Dispose();
        try
        {
            DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[ToolHealthMonitor] Dispose 超时");
        }
    }
}

[JsonSerializable(typeof(Dictionary<string, ToolHealthRecord>))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class ToolHealthJsonContext : JsonSerializerContext;
