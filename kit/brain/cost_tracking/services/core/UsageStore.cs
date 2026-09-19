namespace Core.CostTracking;

/// <summary>
/// 用量存储 — 封装用量记录的内存存储、会话索引与文件持久化
/// 从 CostTracker 提取,统一管理用量记录的添加、查询、加载、保存与重置
/// </summary>
internal sealed class UsageStore {
    private readonly ConcurrentBag<TokenUsageRecord> _usageRecords = new();
    private readonly ConcurrentDictionary<string, List<TokenUsageRecord>> _sessionIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _storagePath;
    private readonly IFileOperationService _fileOperationService;
    private readonly ILogger? _logger;

    /// <summary>构造 UsageStore</summary>
    public UsageStore(IFileOperationService fileOperationService, string storagePath, ILogger? logger = null) {
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _storagePath = storagePath;
        _logger = logger;
    }

    /// <summary>添加用量记录 — 同时更新会话索引</summary>
    public void Add(TokenUsageRecord record) {
        _usageRecords.Add(record);

        var sessionKey = record.SessionId;
        _sessionIndex.AddOrUpdate(
            sessionKey,
            _ => [record],
            (_, existing) => { lock (existing) { existing.Add(record); } return existing; });
    }

    /// <summary>尝试获取会话记录;存在则返回 true</summary>
    public bool TryGetSessionRecords(string sessionId, out List<TokenUsageRecord> records) => _sessionIndex.TryGetValue(sessionId, out records!);

    /// <summary>获取全部用量记录快照</summary>
    public List<TokenUsageRecord> GetAllSnapshot() => _usageRecords.ToList();

    /// <summary>获取指定日期的记录</summary>
    public List<TokenUsageRecord> GetRecordsByDate(DateTime date) => _usageRecords.Where(r => r.Timestamp.Date == date).ToList();

    /// <summary>获取指定时间区间的记录</summary>
    public List<TokenUsageRecord> GetRecordsByDateRange(DateTime start, DateTime end) => _usageRecords.Where(r => r.Timestamp >= start && r.Timestamp <= end).ToList();

    /// <summary>全部记录的总成本</summary>
    public decimal SumCost() => _usageRecords.Sum(r => r.CostUsd);

    /// <summary>指定日期的总成本</summary>
    public decimal SumCostByDate(DateTime date) => _usageRecords.Where(r => r.Timestamp.Date == date).Sum(r => r.CostUsd);

    /// <summary>指定月份的总成本</summary>
    public decimal SumCostByMonth(DateTime now) {
        var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return _usageRecords.Where(r => r.Timestamp >= startOfMonth).Sum(r => r.CostUsd);
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
        while (_usageRecords.TryTake(out _)) { }
        _sessionIndex.Clear();
    }
}