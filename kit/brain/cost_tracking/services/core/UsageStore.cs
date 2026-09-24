namespace Core.CostTracking;

/// <summary>
/// 用量存储 — 封装用量记录的内存存储、冗余索引与文件持久化
/// 从 CostTracker 提取,统一管理用量记录的添加、查询、加载、保存与重置
/// <para>4 个冗余不可变索引: _usageRecords(主列表/持久化) + _bySessionId(O(1)会话查询) + _byDate(O(1)日期查询) + _costByDate(O(1)日期成本汇总)</para>
/// <para>写入 4 次 CAS 原子更新,查询全部 O(1) 或 O(天数),消除全部 O(N) 遍历</para>
/// </summary>
internal sealed class UsageStore {
    private ImmutableList<TokenUsageRecord> _usageRecords = ImmutableList<TokenUsageRecord>.Empty;
    private ImmutableDictionary<string, ImmutableList<TokenUsageRecord>> _bySessionId = ImmutableDictionary<string, ImmutableList<TokenUsageRecord>>.Empty;
    private ImmutableDictionary<DateTime, ImmutableList<TokenUsageRecord>> _byDate = ImmutableDictionary<DateTime, ImmutableList<TokenUsageRecord>>.Empty;
    private ImmutableDictionary<DateTime, decimal> _costByDate = ImmutableDictionary<DateTime, decimal>.Empty;
    private readonly string _storagePath;
    private readonly IFileOperationService _fileOperationService;
    private readonly ILogger? _logger;

    /// <summary>构造 UsageStore</summary>
    public UsageStore(IFileOperationService fileOperationService, string storagePath, ILogger? logger = null) {
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _storagePath = storagePath;
        _logger = logger;
    }

    /// <summary>添加用量记录 — 无锁原子追加到主列表 + 3 个冗余索引</summary>
    public void Add(TokenUsageRecord record) {
        ImmutableInterlocked.Update(ref _usageRecords, static (list, r) => list.Add(r), record);
        ImmutableInterlocked.Update(ref _bySessionId, static (dict, r) => {
            var existing = dict.TryGetValue(r.SessionId, out var list) ? list! : ImmutableList<TokenUsageRecord>.Empty;
            return dict.SetItem(r.SessionId, existing.Add(r));
        }, record);
        var dateKey = record.Timestamp.Date;
        ImmutableInterlocked.Update(ref _byDate, static (dict, arg) => {
            var existing = dict.TryGetValue(arg.date, out var list) ? list! : ImmutableList<TokenUsageRecord>.Empty;
            return dict.SetItem(arg.date, existing.Add(arg.record));
        }, (date: dateKey, record));
        ImmutableInterlocked.Update(ref _costByDate, static (dict, arg) => {
            var existing = dict.TryGetValue(arg.date, out var cost) ? cost : 0m;
            return dict.SetItem(arg.date, existing + arg.record.CostUsd);
        }, (date: dateKey, record));
    }

    /// <summary>尝试获取会话记录;存在则返回 true — O(1) 字典查找,返回不可变引用无需拷贝</summary>
    public bool TryGetSessionRecords(string sessionId, out IReadOnlyList<TokenUsageRecord> records) {
        if (_bySessionId.TryGetValue(sessionId, out var list)) {
            records = list;
            return list.Count > 0;
        }
        records = Array.Empty<TokenUsageRecord>();
        return false;
    }

    /// <summary>获取全部用量记录快照 — 返回不可变引用,无需拷贝</summary>
    public IReadOnlyList<TokenUsageRecord> GetAllSnapshot() => _usageRecords;

    /// <summary>获取指定日期的记录 — O(1) 字典查找,返回不可变引用</summary>
    public IReadOnlyList<TokenUsageRecord> GetRecordsByDate(DateTime date) {
        return _byDate.TryGetValue(date.Date, out var list) ? list : Array.Empty<TokenUsageRecord>();
    }

    /// <summary>获取指定时间区间的记录 — O(天数) 遍历日期范围,每天 O(1) 查找</summary>
    public IReadOnlyList<TokenUsageRecord> GetRecordsByDateRange(DateTime start, DateTime end) {
        var builder = ImmutableList.CreateBuilder<TokenUsageRecord>();
        for (var date = start.Date; date <= end.Date; date = date.AddDays(1)) {
            if (_byDate.TryGetValue(date, out var list)) builder.AddRange(list);
        }
        return builder.ToImmutable();
    }

    /// <summary>全部记录的总成本 — O(天数) 遍历日期成本汇总</summary>
    public decimal SumCost() {
        var sum = 0m;
        foreach (var cost in _costByDate.Values) sum += cost;
        return sum;
    }

    /// <summary>指定日期的总成本 — O(1) 字典查找</summary>
    public decimal SumCostByDate(DateTime date) {
        return _costByDate.TryGetValue(date.Date, out var cost) ? cost : 0m;
    }

    /// <summary>指定月份的总成本 — O(31) 遍历当月日期</summary>
    public decimal SumCostByMonth(DateTime now) {
        var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var sum = 0m;
        for (var date = startOfMonth; date.Month == now.Month; date = date.AddDays(1)) {
            if (_costByDate.TryGetValue(date, out var cost)) sum += cost;
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

            var result = await _fileOperationService.WriteFileAsync(_storagePath, json, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result.Success) {
                _logger?.LogInformation("[CostTracker] 已保存 {Count} 条用量记录", records.Count);
            } else {
                _logger?.LogError("[CostTracker] 保存用量历史失败: {Error}", result.ErrorMessage);
            }
        } catch (Exception ex) {
            _logger?.LogError(ex, "[CostTracker] 保存用量历史失败");
        }
    }

    /// <summary>清空全部用量记录与所有冗余索引</summary>
    public void Reset() {
        _usageRecords = ImmutableList<TokenUsageRecord>.Empty;
        _bySessionId = ImmutableDictionary<string, ImmutableList<TokenUsageRecord>>.Empty;
        _byDate = ImmutableDictionary<DateTime, ImmutableList<TokenUsageRecord>>.Empty;
        _costByDate = ImmutableDictionary<DateTime, decimal>.Empty;
    }
}
