namespace Core.CostTracking;

/// <summary>
/// 成本跟踪器 — 记录 Token 用量、计算成本、管理预算告警与历史持久化
/// </summary>
[Register(typeof(ICostTracker), ServiceLifetime.Singleton)]
public sealed partial class CostTracker : IAsyncDisposable, ICostTracker
{
    private readonly ILogger<CostTracker>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly ModelPricing _pricing;
    private readonly CostSessionStats _stats;
    private readonly BudgetGuard _budget;
    private readonly UsageStore _store;
    private CancellationTokenSource? _disposeCts = new();

    /// <summary>
    /// 构造成本跟踪器实例 — 加载默认模型定价并异步加载历史用量记录
    /// </summary>
    /// <param name="fileOperationService">文件操作服务</param>
    /// <param name="storagePath">历史用量存储路径（可选，默认使用应用数据目录）</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <param name="budgetConfig">预算配置（可选，启用预算管理）</param>
    /// <param name="telemetryService">遥测服务（可选）</param>
    /// <param name="clock">时钟服务（可选，默认使用系统时钟）</param>
    /// <param name="modelConfigLoader">模型配置加载器（可选，用于加载模型定价）</param>
    public CostTracker(IFileOperationService fileOperationService, string? storagePath = null, ILogger<CostTracker>? logger = null, BudgetConfig? budgetConfig = null, ITelemetryService? telemetryService = null, IClockService? clock = null, IModelConfigLoader? modelConfigLoader = null)
    {
        _logger = logger;
        _telemetryService = telemetryService;
        _budget = new BudgetGuard(logger, budgetConfig);
        _stats = new CostSessionStats(clock ?? SystemClockService.Instance);
        _pricing = new ModelPricing(logger, modelConfigLoader);
        _store = new UsageStore(fileOperationService, storagePath ?? AppDataConstants.Paths.CostTrackingFilePath, logger);

        if (budgetConfig != null)
        {
            budgetConfig.ValidateOrThrow();
            _logger?.LogInformation("[CostTracker] 预算管理已启用 - 日限额: ${Daily}, 月限额: ${Monthly}, 总限额: ${Total}",
                budgetConfig.DailyLimit, budgetConfig.MonthlyLimit, budgetConfig.TotalLimit);
        }

        var initCts = Volatile.Read(ref _disposeCts);
        if (initCts is not null)
        {
            _ = _store.LoadHistoryAsync(initCts.Token).WaitAsync(TimeSpan.FromSeconds(10), initCts.Token).ConfigureAwait(false);
        }
    }

    /// <summary>成本告警触发事件 — 委托到 BudgetGuard</summary>
    public event EventHandler<CostAlertEventArgs>? CostAlertTriggered
    {
        add => _budget.CostAlertTriggered += value;
        remove => _budget.CostAlertTriggered -= value;
    }

    /// <summary>
    /// 记录 Token 用量（不含缓存 Token）
    /// </summary>
    /// <param name="model">模型名称</param>
    /// <param name="promptTokens">Prompt Token 数量</param>
    /// <param name="completionTokens">Completion Token 数量</param>
    /// <param name="sessionId">会话标识（可选，默认使用全局会话标识）</param>
    public void RecordUsage(string model, int promptTokens, int completionTokens, string? sessionId = null)
    {
        RecordUsage(model, promptTokens, completionTokens, 0, 0, 0, sessionId);
    }

    /// <summary>
    /// 记录 Token 用量（含缓存 Token，不含 API 耗时）
    /// </summary>
    /// <param name="model">模型名称</param>
    /// <param name="promptTokens">Prompt Token 数量</param>
    /// <param name="completionTokens">Completion Token 数量</param>
    /// <param name="cacheCreationTokens">缓存创建 Token 数量</param>
    /// <param name="cacheReadTokens">缓存读取 Token 数量</param>
    /// <param name="sessionId">会话标识（可选，默认使用全局会话标识）</param>
    public void RecordUsage(string model, int promptTokens, int completionTokens, int cacheCreationTokens, int cacheReadTokens, string? sessionId = null)
    {
        RecordUsage(model, promptTokens, completionTokens, cacheCreationTokens, cacheReadTokens, 0, sessionId);
    }

    /// <summary>
    /// 记录 Token 用量（含缓存 Token 与 API 耗时） — 计算成本、更新会话索引、上报遥测并触发预算检查
    /// </summary>
    /// <param name="model">模型名称</param>
    /// <param name="promptTokens">Prompt Token 数量</param>
    /// <param name="completionTokens">Completion Token 数量</param>
    /// <param name="cacheCreationTokens">缓存创建 Token 数量</param>
    /// <param name="cacheReadTokens">缓存读取 Token 数量</param>
    /// <param name="apiDurationMs">API 调用耗时（毫秒）</param>
    /// <param name="sessionId">会话标识（可选，默认使用全局会话标识）</param>
    public void RecordUsage(string model, int promptTokens, int completionTokens, int cacheCreationTokens, int cacheReadTokens, double apiDurationMs, string? sessionId = null)
    {
        var record = new TokenUsageRecord
        {
            Timestamp = _stats.CurrentTime,
            Model = model,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            CacheCreationTokens = cacheCreationTokens,
            CacheReadTokens = cacheReadTokens,
            SessionId = sessionId ?? global::Core.Utils.SessionIdFactory.DefaultSessionId,
            CostUsd = CalculateCost(model, promptTokens, completionTokens, cacheCreationTokens, cacheReadTokens),
            ApiDurationMs = apiDurationMs
        };

        _store.Add(record);

        _logger?.LogInformation("[CostTracker] 记录用量 - 模型: {Model}, Prompt: {PromptTokens}, Completion: {CompletionTokens}, CacheCreate: {CacheCreate}, CacheRead: {CacheRead}, 成本: ${Cost:F6}",
            model, promptTokens, completionTokens, cacheCreationTokens, cacheReadTokens, record.CostUsd);

        if (_telemetryService != null)
        {
            var tokenCounter = _telemetryService.GetCounter("cost.tracker.tokens", "tokens", "Token usage count");
            tokenCounter.Add(promptTokens, new Dictionary<string, string> { ["model"] = model, ["type"] = "prompt" });
            tokenCounter.Add(completionTokens, new Dictionary<string, string> { ["model"] = model, ["type"] = "completion" });

            var costHistogram = _telemetryService.GetHistogram("cost.tracker.usd", "usd", "Cost per request");
            costHistogram.Record((double)record.CostUsd, new Dictionary<string, string> { ["model"] = model });
        }

        var cts = Volatile.Read(ref _disposeCts);
        if (cts is not null)
        {
            _ = Task.Run(() => _store.SaveHistoryAsync(cts.Token)).WaitAsync(TimeSpan.FromSeconds(10), cts.Token).ConfigureAwait(false);
        }

        if (_budget.IsEnabled && cts is not null)
        {
            _ = _budget.CheckAlertsAsync(GetCostSnapshot, cts.Token).WaitAsync(TimeSpan.FromSeconds(10), cts.Token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 获取指定会话的成本统计信息
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>会话成本统计信息；若会话不存在则返回空统计</returns>
    public CostStatistics GetSessionStatistics(string sessionId)
    {
        if (!_store.TryGetSessionRecords(sessionId, out var records))
            return new CostStatistics();
        lock (records)
        {
            return CalculateStatistics(new List<TokenUsageRecord>(records));
        }
    }

    /// <summary>
    /// 获取今日的成本统计信息
    /// </summary>
    /// <returns>今日成本统计信息</returns>
    public CostStatistics GetTodayStatistics()
    {
        var today = _stats.CurrentTime.Date;
        var records = _store.GetRecordsByDate(today);
        return CalculateStatistics(records);
    }

    /// <summary>
    /// 获取全部用量记录的成本统计信息
    /// </summary>
    /// <returns>全部成本统计信息</returns>
    public CostStatistics GetTotalStatistics()
    {
        return CalculateStatistics(_store.GetAllSnapshot());
    }

    /// <summary>
    /// 获取指定时间区间内的成本统计信息
    /// </summary>
    /// <param name="startDate">起始时间</param>
    /// <param name="endDate">结束时间</param>
    /// <returns>指定时间区间内的成本统计信息</returns>
    public CostStatistics GetStatistics(DateTime startDate, DateTime endDate)
    {
        var records = _store.GetRecordsByDateRange(startDate, endDate);
        return CalculateStatistics(records);
    }

    /// <summary>
    /// 记录代码行变更数 — 用于在统计中反映代码增删量
    /// </summary>
    /// <param name="added">新增行数</param>
    /// <param name="removed">删除行数</param>
    public void RecordLinesChanged(int added, int removed)
    {
        _stats.RecordLinesChanged(added, removed);
    }

    /// <summary>
    /// 设置模型定价 — 覆盖默认定价表
    /// </summary>
    /// <param name="model">模型名称</param>
    /// <param name="promptCostPer1K">每 1K Prompt Token 成本 (USD)</param>
    /// <param name="completionCostPer1K">每 1K Completion Token 成本 (USD)</param>
    public void SetModelCost(string model, decimal promptCostPer1K, decimal completionCostPer1K) => _pricing.Set(model, promptCostPer1K, completionCostPer1K);

    /// <summary>
    /// 获取指定模型的定价信息
    /// </summary>
    /// <param name="model">模型名称</param>
    /// <returns>模型成本信息；若未配置则返回 null</returns>
    public ModelCostInfo? GetModelCost(string model) => _pricing.Get(model);

    /// <summary>
    /// 获取所有已配置模型定价的只读字典快照
    /// </summary>
    /// <returns>模型定价只读字典，键为模型名称</returns>
    public IReadOnlyDictionary<string, ModelCostInfo> GetAllModelCosts() => _pricing.GetAll();

    /// <summary>
    /// 判断是否超出预算限制
    /// </summary>
    /// <returns>若预算管理启用且超出日或月预算则返回 true，否则返回 false</returns>
    public bool IsBudgetExceeded() => _budget.IsExceeded(GetCostSnapshot);

    /// <summary>
    /// 获取当前预算状态 — 包含日、月已用金额与限额
    /// </summary>
    /// <returns>预算状态实例</returns>
    public BudgetStatus GetBudgetStatus() => _budget.GetStatus(GetCostSnapshot);

    /// <summary>
    /// 异步更新预算配置 — 加锁保护，重置已触发阈值集合
    /// </summary>
    /// <param name="config">新的预算配置</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public Task SetBudgetAsync(BudgetConfig config, CancellationToken ct = default) => _budget.SetAsync(config, ct);

    private CostSnapshot GetCostSnapshot() => new(_store.SumCostByDate(_stats.CurrentTime.Date), _store.SumCostByMonth(_stats.CurrentTime), _store.SumCost());

    private decimal CalculateCost(string model, int promptTokens, int completionTokens, int cacheCreationTokens = 0, int cacheReadTokens = 0) => _pricing.CalculateCost(model, promptTokens, completionTokens, cacheCreationTokens, cacheReadTokens);

    private CostStatistics CalculateStatistics(List<TokenUsageRecord> records)
    {
        if (records.Count == 0)
        {
            return new CostStatistics();
        }

        var totalPromptTokens = records.Sum(r => r.PromptTokens);
        var totalCompletionTokens = records.Sum(r => r.CompletionTokens);
        var totalCacheCreationTokens = records.Sum(r => r.CacheCreationTokens);
        var totalCacheReadTokens = records.Sum(r => r.CacheReadTokens);
        var totalCost = records.Sum(r => r.CostUsd);

        var cacheSavings = records
            .Where(r => r.CacheReadTokens > 0 && _pricing.TryGetCost(r.Model, out var costInfo))
            .Sum(r =>
            {
                var costInfo = _pricing.Get(r.Model)!;
                var normalCost = (r.CacheReadTokens / 1000m) * costInfo.PromptCostPer1KTokens;
                var cacheCost = (r.CacheReadTokens / 1000m) * costInfo.PromptCostPer1KTokens * 0.1m;
                return normalCost - cacheCost;
            });

        var apiDurationMs = records.Sum(r => r.ApiDurationMs);
        var firstTimestamp = records.Min(r => r.Timestamp);
        var lastTimestamp = records.Max(r => r.Timestamp);
        var wallDuration = lastTimestamp - firstTimestamp;

        var hasUnknownModel = records.Any(r => !_pricing.Contains(r.Model) && _pricing.GetDefaultCostInfo(r.Model).PromptCostPer1KTokens == ModelPricingTable.DefaultPromptCostPer1K);

        var modelBreakdown = records
            .GroupBy(r => r.Model)
            .Select(g => new ModelCostStatistics
            {
                Model = g.Key,
                RequestCount = g.Count(),
                PromptTokens = g.Sum(r => r.PromptTokens),
                CompletionTokens = g.Sum(r => r.CompletionTokens),
                CacheCreationTokens = g.Sum(r => r.CacheCreationTokens),
                CacheReadTokens = g.Sum(r => r.CacheReadTokens),
                TotalCost = g.Sum(r => r.CostUsd)
            })
            .ToList();

        return new CostStatistics
        {
            RequestCount = records.Count,
            PromptTokens = totalPromptTokens,
            CompletionTokens = totalCompletionTokens,
            TotalCostUsd = totalCost,
            CacheCreationTokens = totalCacheCreationTokens,
            CacheReadTokens = totalCacheReadTokens,
            CacheSavingsUsd = cacheSavings,
            ModelBreakdown = modelBreakdown,
            ApiDuration = TimeSpan.FromMilliseconds(apiDurationMs),
            WallDuration = wallDuration,
            LinesAdded = _stats.TotalLinesAdded,
            LinesRemoved = _stats.TotalLinesRemoved,
            HasUnknownModelCost = hasUnknownModel
        };
    }

    /// <summary>
    /// 重置所有用量记录 — 对齐 TS login.tsx resetCostState
    /// </summary>
    public void Reset()
    {
        _store.Reset();
        _budget.Reset();
        _stats.Reset();
        _logger?.LogInformation("[CostTracker] 用量记录已重置");
    }

    /// <summary>
    /// 异步释放资源 — 取消内部令牌并释放预算锁
    /// </summary>
    /// <returns>表示异步操作的任务</returns>
    public ValueTask DisposeAsync()
    {
        var cts = Interlocked.Exchange(ref _disposeCts, null);
        if (cts is not null)
        {
            cts.Cancel();
            _budget.Dispose();
            cts.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// 模型成本信息 — 描述单个模型的 Prompt 与 Completion Token 定价
/// </summary>
public sealed partial class ModelCostInfo
{
    /// <summary>
    /// 模型名称
    /// </summary>
    public required string Model { get; init; }

    /// <summary>
    /// 每 1K Prompt Token 成本 (USD)
    /// </summary>
    public decimal PromptCostPer1KTokens { get; init; }

    /// <summary>
    /// 每 1K Completion Token 成本 (USD)
    /// </summary>
    public decimal CompletionCostPer1KTokens { get; init; }
}

internal enum BudgetType
{
    Daily,
    Monthly,
    Total
}
