namespace Core.CostTracking;

/// <summary>
/// 用量存储 — 封装用量记录的内存存储、会话索引与文件持久化
/// 从 CostTracker 提取,统一管理用量记录的添加、查询、加载、保存与重置
/// </summary>
internal sealed class UsageStore {
    private ImmutableList<TokenUsageRecord> _usageRecords = ImmutableList<TokenUsageRecord>.Empty;
    private readonly string _storagePath;
    private readonly IFileOperationService _fileOperationService;
    private readonly ILogger? _logger;

    /// <summary>构造 UsageStore</summary>
    public UsageStore(IFileOperationService fileOperationService, string storagePath, ILogger? logger = null) {
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _storagePath = storagePath;
        _logger = logger;
    }

    /// <summary>添加用量记录 — 无锁原子追加到不可变列表</summary>
    public void Add(TokenUsageRecord record) {
        ImmutableInterlocked.Update(ref _usageRecords, static (list, r) => list.Add(r), record);
    }

    /// <summary>尝试获取会话记录;存在则返回 true</summary>
    public bool TryGetSessionRecords(string sessionId, out List<TokenUsageRecord> records) {
        var snapshot = _usageRecords;
        var found = new List<TokenUsageRecord>();
        foreach (var r in snapshot) {
            if (string.Equals(r.SessionId, sessionId, StringComparison.OrdinalIgnoreCase)) {
                found.Add(r);
            }
        }
        records = found;
        return found.Count > 0;
    }

    /// <summary>获取全部用量记录快照</summary>
    public List<TokenUsageRecord> GetAllSnapshot() => [.. _usageRecords];

    /// <summary>获取指定日期的记录</summary>
    public List<TokenUsageRecord> GetRecordsByDate(DateTime date) {
        var result = new List<TokenUsageRecord>();
        foreach (var r in _usageRecords) {
            if (r.Timestamp.Date == date) result.Add(r);
        }
        return result;
    }

    /// <summary>获取指定时间区间的记录</summary>
    public List<TokenUsageRecord> GetRecordsByDateRange(DateTime start, DateTime end) {
        var result = new List<TokenUsageRecord>();
        foreach (var r in _usageRecords) {
            if (r.Timestamp >= start && r.Timestamp <= end) result.Add(r);
        }
        return result;
    }

    /// <summary>全部记录的总成本</summary>
    public decimal SumCost() {
        var sum = 0m;
        foreach (var r in _usageRecords) sum += r.CostUsd;
        return sum;
    }

    /// <summary>指定日期的总成本</summary>
    public decimal SumCostByDate(DateTime date) {
        var sum = 0m;
        foreach (var r in _usageRecords) {
            if (r.Timestamp.Date == date) sum += r.CostUsd;
        }
        return sum;
    }

    /// <summary>指定月份的总成本</summary>
    public decimal SumCostByMonth(DateTime now) {
        var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var sum = 0m;
        foreach (var r in _usageRecords) {
            if (r.Timestamp >= startOfMonth) sum += r.CostUsd;
        }
        return sum;
    }

    /// <summary>从文件加载历史用量记录</summary>
    public async Task LoadHistoryAsync(CancellationToken cancellationToken = default) {
        try {
            var result = await _fileOperationService.ReadFileAsync(_storagePath, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!result.Success) {
                return;
            }

            var records = RelaxedJsonSerializer.Deserialize(result.Content, CostTrackingJsonContext.Default.ListTokenUsageRecord);

            if (records != null) {
                foreach (var record in records) {
                    Add(record);
                }

                _logger?.LogInformation("[CostTracker] 加载了 {Count} 条历史用量记录", records.Count);
            }
        } catch (Exception ex) {
            _logger?.LogError(ex, "[CostTracker] 加载用量历史失败");
        }
    }

    /// <summary>保存用量记录到文件</summary>
    public async Task SaveHistoryAsync(CancellationToken cancellationToken = default) {
        try {
            var records = _usageRecords.ToList();
            var json = JsonSerializer.Serialize(records, CostTrackingJsonContext.Default.ListTokenUsageRecord);

            var directory = Path.GetDirectoryName(_storagePath);
            if (!string.IsNullOrEmpty(directory) && !_fileOperationService.DirectoryExists(directory)) {
                _fileOperationService.CreateDirectory(directory);
            }

            var result = await _fileOperationService.WriteFileAsync(_storagePath, json, cancellationToken).ConfigureAwait(false);
            if (result.Success) {
                _logger?.LogInformation("[CostTracker] 已保存 {Count} 条用量记录", records.Count);
            } else {
                _logger?.LogError("[CostTracker] 保存用量历史失败: {Error}", result.ErrorMessage);
            }
        } catch (Exception ex) {
            _logger?.LogError(ex, "[CostTracker] 保存用量历史失败");
        }
    }

    /// <summary>清空全部用量记录与会话索引</summary>
    public void Reset() {
        _usageRecords = ImmutableList<TokenUsageRecord>.Empty;
    }
}
