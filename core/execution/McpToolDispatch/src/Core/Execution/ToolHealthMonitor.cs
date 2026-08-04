namespace McpToolDispatch;

/// <summary>
/// 工具健康监控服务 — 追踪工具执行成功率、评分、熔断状态
/// 持久化到 AppData JSON 文件，支持热更新
/// 支持黑名单（完全禁用）和降权（额外扣分）配置
/// </summary>
[Register]
public sealed class ToolHealthMonitor : IToolHealthMonitor, IDisposable
{
    private readonly ILogger<ToolHealthMonitor>? _logger;
    private readonly IFileSystem _fs;
    private readonly ToolScoreConfig _config;
    private readonly Dictionary<string, ToolHealthRecord> _records = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _configPath;
    private readonly Timer? _decayTimer;
    private volatile BlacklistSnapshot _blacklistSnapshot;
    private volatile Dictionary<string, int> _penalties;

    private sealed record BlacklistSnapshot
    {
        public required FrozenSet<string> Exact { get; init; }
        public required FrozenSet<string> Patterns { get; init; }
    }

    public ToolHealthMonitor(IFileSystem fs, ILogger<ToolHealthMonitor>? logger = null, ToolScoreConfig? config = null,
        HashSet<string>? blacklist = null, Dictionary<string, int>? penalties = null)
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
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JoinCode",
            "tool-health.json");
        LoadFromDisk();

        _decayTimer = new Timer(_ => ApplyTimeDecay(), null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
    }

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
    /// 例如: "shell_*" 匹配 "shell_check", "shell_background_get"
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
        var record = _records.GetValueOrDefault(toolName);
        var baseScore = record?.Score ?? 0;
        return Math.Clamp(baseScore + GetPenalty(toolName), _config.ScoreMin, _config.ScoreMax);
    }

    public async Task<ToolHealthRecord> RecordSuccessAsync(string toolName, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var record = GetOrCreate(toolName);
            record.Score = Math.Clamp(record.Score + _config.SuccessDelta, _config.ScoreMin, _config.ScoreMax);
            record.SuccessCount++;
            record.ConsecutiveFailures = 0;
            record.LastAdjusted = DateTime.UtcNow;
            record.LastErrorMessage = null;

            if (!record.IsEnabled && record.Score > _config.ScoreMin / 2)
            {
                record.IsEnabled = true;
                _logger?.LogInformation("工具 {ToolName} 评分恢复，自动重新启用", toolName);
            }

            SaveToDisk();
            return record;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ToolHealthRecord> RecordFailureAsync(string toolName, string? errorMessage, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var record = GetOrCreate(toolName);
            record.Score = Math.Clamp(record.Score + _config.FailDelta, _config.ScoreMin, _config.ScoreMax);
            record.FailCount++;
            record.ConsecutiveFailures++;
            record.LastAdjusted = DateTime.UtcNow;
            record.LastErrorMessage = errorMessage;

            if (record.ConsecutiveFailures >= _config.CircuitBreakerThreshold && record.IsEnabled)
            {
                record.IsEnabled = false;
                _logger?.LogWarning("工具 {ToolName} 连续失败 {Count} 次，自动禁用（熔断）", toolName, record.ConsecutiveFailures);
            }

            SaveToDisk();
            return record;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ToolHealthRecord?> GetRecordAsync(string toolName, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _records.GetValueOrDefault(toolName);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, ToolHealthRecord>> GetAllRecordsAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _records.ToFrozenDictionary();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ResetToolAsync(string toolName, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_records.TryGetValue(toolName, out var record))
            {
                record.Score = 0;
                record.ConsecutiveFailures = 0;
                record.IsEnabled = true;
                record.LastAdjusted = DateTime.UtcNow;
                SaveToDisk();
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private ToolHealthRecord GetOrCreate(string toolName)
    {
        if (!_records.TryGetValue(toolName, out var record))
        {
            record = new ToolHealthRecord { ToolName = toolName };
            _records[toolName] = record;
        }
        return record;
    }

    private void ApplyTimeDecay()
    {
        _lock.Wait();
        try
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
        finally
        {
            _lock.Release();
        }
    }

    private void LoadFromDisk()
    {
        try
        {
            if (!_fs.FileExists(_configPath)) return;
            var json = _fs.ReadAllText(_configPath);
            var data = JsonSerializer.Deserialize(json, ToolHealthJsonContext.Default.DictionaryStringToolHealthRecord);
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
            var json = JsonSerializer.Serialize(_records, ToolHealthJsonContext.Default.DictionaryStringToolHealthRecord);
            _fs.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "保存工具健康记录失败");
        }
    }

    public void Dispose()
    {
        _decayTimer?.Dispose();
        _lock.Dispose();
    }
}

[JsonSerializable(typeof(Dictionary<string, ToolHealthRecord>))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class ToolHealthJsonContext : JsonSerializerContext;
