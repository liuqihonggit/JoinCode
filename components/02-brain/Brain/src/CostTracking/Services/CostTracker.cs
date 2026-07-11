using JoinCode.Abstractions.Attributes;

namespace Core.CostTracking;

[Register]
public sealed partial class CostTracker : IAsyncDisposable, ICostTracker
{
    private readonly ConcurrentDictionary<string, ModelCostInfo> _modelCosts;
    private readonly ConcurrentBag<TokenUsageRecord> _usageRecords;
    private readonly string _storagePath;
    [Inject] private readonly ILogger<CostTracker>? _logger;
    private readonly IFileOperationService _fileOperationService;
    private readonly ITelemetryService? _telemetryService;
    private readonly SemaphoreSlim _budgetLock;
    private readonly IClockService _clock;
    private CancellationTokenSource? _disposeCts = new();

    private BudgetConfig? _budgetConfig;
    private readonly HashSet<double> _triggeredThresholds = [];
    private int _totalLinesAdded;
    private int _totalLinesRemoved;
    private DateTime _sessionStartTime;

    public CostTracker(IFileOperationService fileOperationService, string? storagePath = null, ILogger<CostTracker>? logger = null, BudgetConfig? budgetConfig = null, ITelemetryService? telemetryService = null, IClockService? clock = null)
    {
        _storagePath = storagePath ?? Path.Combine(AppContext.BaseDirectory, "cost-tracking.json");
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _logger = logger;
        _budgetConfig = budgetConfig;
        _telemetryService = telemetryService;
        _clock = clock ?? SystemClockService.Instance;
        _sessionStartTime = _clock.GetUtcNow();
        _modelCosts = new ConcurrentDictionary<string, ModelCostInfo>(StringComparer.OrdinalIgnoreCase);
        _usageRecords = new ConcurrentBag<TokenUsageRecord>();
        _budgetLock = new SemaphoreSlim(1, 1);

        if (_budgetConfig != null)
        {
            _budgetConfig.ValidateOrThrow();
            _logger?.LogInformation("[CostTracker] 预算管理已启用 - 日限额: ${Daily}, 月限额: ${Monthly}, 总限额: ${Total}",
                _budgetConfig.DailyLimit, _budgetConfig.MonthlyLimit, _budgetConfig.TotalLimit);
        }

        LoadDefaultModelCosts();
        var initCts = Volatile.Read(ref _disposeCts);
        if (initCts is not null)
        {
            _ = LoadUsageHistoryAsync(initCts.Token).WaitAsync(TimeSpan.FromSeconds(10), initCts.Token).ConfigureAwait(false);
        }
    }

    public event EventHandler<CostAlertEventArgs>? CostAlertTriggered;

    public void RecordUsage(string model, int promptTokens, int completionTokens, string? sessionId = null)
    {
        RecordUsage(model, promptTokens, completionTokens, 0, 0, 0, sessionId);
    }

    public void RecordUsage(string model, int promptTokens, int completionTokens, int cacheCreationTokens, int cacheReadTokens, string? sessionId = null)
    {
        RecordUsage(model, promptTokens, completionTokens, cacheCreationTokens, cacheReadTokens, 0, sessionId);
    }

    public void RecordUsage(string model, int promptTokens, int completionTokens, int cacheCreationTokens, int cacheReadTokens, double apiDurationMs, string? sessionId = null)
    {
        var record = new TokenUsageRecord
        {
            Timestamp = _clock.GetUtcNow(),
            Model = model,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            CacheCreationTokens = cacheCreationTokens,
            CacheReadTokens = cacheReadTokens,
            SessionId = sessionId ?? Guid.NewGuid().ToString("N")[..8],
            CostUsd = CalculateCost(model, promptTokens, completionTokens, cacheCreationTokens, cacheReadTokens),
            ApiDurationMs = apiDurationMs
        };

        _usageRecords.Add(record);
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
            _ = Task.Run(() => SaveUsageHistoryAsync(cts.Token)).WaitAsync(TimeSpan.FromSeconds(10), cts.Token).ConfigureAwait(false);
        }

        if (_budgetConfig?.Enabled == true && cts is not null)
        {
            _ = CheckBudgetAlertAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(10), cts.Token).ConfigureAwait(false);
        }
    }

    public CostStatistics GetSessionStatistics(string sessionId)
    {
        var records = _usageRecords.Where(r => r.SessionId == sessionId).ToList();
        return CalculateStatistics(records);
    }

    public CostStatistics GetTodayStatistics()
    {
        var today = _clock.GetUtcNow().Date;
        var records = _usageRecords.Where(r => r.Timestamp.Date == today).ToList();
        return CalculateStatistics(records);
    }

    public CostStatistics GetTotalStatistics()
    {
        return CalculateStatistics(_usageRecords.ToList());
    }

    public CostStatistics GetStatistics(DateTime startDate, DateTime endDate)
    {
        var records = _usageRecords.Where(r => r.Timestamp >= startDate && r.Timestamp <= endDate).ToList();
        return CalculateStatistics(records);
    }

    public void RecordLinesChanged(int added, int removed)
    {
        Interlocked.Add(ref _totalLinesAdded, added);
        Interlocked.Add(ref _totalLinesRemoved, removed);
    }

    public void SetModelCost(string model, decimal promptCostPer1K, decimal completionCostPer1K)
    {
        _modelCosts[model] = new ModelCostInfo
        {
            Model = model,
            PromptCostPer1KTokens = promptCostPer1K,
            CompletionCostPer1KTokens = completionCostPer1K
        };

        _logger?.LogInformation("[CostTracker] 设置模型定价 - {Model}: Prompt ${PromptCost}/1K, Completion ${CompletionCost}/1K",
            model, promptCostPer1K, completionCostPer1K);
    }

    public ModelCostInfo? GetModelCost(string model)
    {
        return _modelCosts.GetValueOrDefault(model);
    }

    public IReadOnlyDictionary<string, ModelCostInfo> GetAllModelCosts()
    {
        return _modelCosts.ToFrozenDictionary();
    }

    public bool IsBudgetExceeded()
    {
        if (_budgetConfig?.Enabled != true)
        {
            return false;
        }

        var status = GetBudgetStatus();
        return status.IsAnyBudgetExceeded();
    }

    public BudgetStatus GetBudgetStatus()
    {
        if (_budgetConfig == null)
        {
            return new BudgetStatus
            {
                DailyUsed = 0,
                DailyLimit = 0,
                MonthlyUsed = 0,
                MonthlyLimit = 0
            };
        }

        var dailyCost = CalculateDailyCost();
        var monthlyCost = CalculateMonthlyCost();

        return new BudgetStatus
        {
            DailyUsed = dailyCost,
            DailyLimit = _budgetConfig.DailyLimit,
            MonthlyUsed = monthlyCost,
            MonthlyLimit = _budgetConfig.MonthlyLimit
        };
    }

    public async Task SetBudgetAsync(BudgetConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.ValidateOrThrow();

        await _budgetLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _budgetConfig = config;
            _triggeredThresholds.Clear();
        }
        finally
        {
            _budgetLock.Release();
        }

        _logger?.LogInformation("[CostTracker] 预算配置已更新 - 日限额: ${Daily}, 月限额: ${Monthly}, 总限额: ${Total}",
            config.DailyLimit, config.MonthlyLimit, config.TotalLimit);
    }

    private async Task CheckBudgetAlertAsync(CancellationToken ct = default)
    {
        if (_budgetConfig?.Enabled != true || _budgetConfig.AlertThresholds.Count == 0)
        {
            return;
        }

        await _budgetLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var dailyCost = CalculateDailyCost();
            var monthlyCost = CalculateMonthlyCost();
            var totalCost = _usageRecords.Sum(r => r.CostUsd);

            CheckThresholdAlert(dailyCost, _budgetConfig.DailyLimit, BudgetType.Daily);
            CheckThresholdAlert(monthlyCost, _budgetConfig.MonthlyLimit, BudgetType.Monthly);
            CheckThresholdAlert(totalCost, _budgetConfig.TotalLimit, BudgetType.Total);
        }
        finally
        {
            _budgetLock.Release();
        }
    }

    private void CheckThresholdAlert(decimal currentCost, decimal budgetLimit, BudgetType budgetType)
    {
        if (budgetLimit <= 0)
        {
            return;
        }

        var percentageUsed = (double)(currentCost / budgetLimit);

        foreach (var threshold in _budgetConfig!.AlertThresholds)
        {
            if (percentageUsed >= threshold && !_triggeredThresholds.Contains(threshold))
            {
                _triggeredThresholds.Add(threshold);

                var level = threshold switch
                {
                    >= 1.0 => CostAlertLevel.Critical,
                    >= 0.8 => CostAlertLevel.Warning,
                    _ => CostAlertLevel.Info
                };

                var message = $"{budgetType}预算告警: 已使用 {percentageUsed:P1} (限额: ${budgetLimit:F2})";

                var alert = CostAlert.Create(level, message, currentCost, budgetLimit);
                var args = CostAlertEventArgs.Create(alert);

                _logger?.LogWarning("[CostTracker] {Message}", message);
                CostAlertTriggered?.Invoke(this, args);

                break;
            }
        }
    }

    private decimal CalculateDailyCost()
    {
        var today = _clock.GetUtcNow().Date;
        return _usageRecords
            .Where(r => r.Timestamp.Date == today)
            .Sum(r => r.CostUsd);
    }

    private decimal CalculateMonthlyCost()
    {
        var now = _clock.GetUtcNow();
        var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return _usageRecords
            .Where(r => r.Timestamp >= startOfMonth)
            .Sum(r => r.CostUsd);
    }

    private decimal CalculateCost(string model, int promptTokens, int completionTokens, int cacheCreationTokens = 0, int cacheReadTokens = 0)
    {
        if (!_modelCosts.TryGetValue(model, out var costInfo))
        {
            costInfo = GetDefaultCostInfo(model);
        }

        var promptCost = (promptTokens / 1000m) * costInfo.PromptCostPer1KTokens;
        var completionCost = (completionTokens / 1000m) * costInfo.CompletionCostPer1KTokens;
        var cacheCreationCost = (cacheCreationTokens / 1000m) * costInfo.PromptCostPer1KTokens * 1.25m;
        var cacheReadCost = (cacheReadTokens / 1000m) * costInfo.PromptCostPer1KTokens * 0.1m;

        return promptCost + completionCost + cacheCreationCost + cacheReadCost;
    }

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
            .Where(r => r.CacheReadTokens > 0 && _modelCosts.TryGetValue(r.Model, out var costInfo))
            .Sum(r =>
            {
                var costInfo = _modelCosts[r.Model];
                var normalCost = (r.CacheReadTokens / 1000m) * costInfo.PromptCostPer1KTokens;
                var cacheCost = (r.CacheReadTokens / 1000m) * costInfo.PromptCostPer1KTokens * 0.1m;
                return normalCost - cacheCost;
            });

        var apiDurationMs = records.Sum(r => r.ApiDurationMs);
        var firstTimestamp = records.Min(r => r.Timestamp);
        var lastTimestamp = records.Max(r => r.Timestamp);
        var wallDuration = lastTimestamp - firstTimestamp;

        var hasUnknownModel = records.Any(r => !_modelCosts.ContainsKey(r.Model) && GetDefaultCostInfo(r.Model).PromptCostPer1KTokens == ModelPricingTable.DefaultPromptCostPer1K);

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
            LinesAdded = Volatile.Read(ref _totalLinesAdded),
            LinesRemoved = Volatile.Read(ref _totalLinesRemoved),
            HasUnknownModelCost = hasUnknownModel
        };
    }

    private void LoadDefaultModelCosts()
    {
        foreach (var (keyword, promptCost, completionCost) in ModelPricingTable.GetAllEntries())
        {
            SetModelCost(keyword, promptCost, completionCost);
        }
    }

    private ModelCostInfo GetDefaultCostInfo(string model)
    {
        foreach (var kvp in _modelCosts)
        {
            if (model.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value;
            }
        }

        return new ModelCostInfo
        {
            Model = model,
            PromptCostPer1KTokens = ModelPricingTable.DefaultPromptCostPer1K,
            CompletionCostPer1KTokens = ModelPricingTable.DefaultCompletionCostPer1K
        };
    }

    private async Task LoadUsageHistoryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _fileOperationService.ReadFileAsync(_storagePath, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                return;
            }

            var records = JsonSerializer.Deserialize(result.Content, CostTrackingJsonContext.Default.ListTokenUsageRecord);

            if (records != null)
            {
                foreach (var record in records)
                {
                    _usageRecords.Add(record);
                }

                _logger?.LogInformation("[CostTracker] 加载了 {Count} 条历史用量记录", records.Count);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[CostTracker] 加载用量历史失败");
        }
    }

    private async Task SaveUsageHistoryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var records = _usageRecords.ToList();
            var json = JsonSerializer.Serialize(records, CostTrackingJsonContext.Default.ListTokenUsageRecord);

            var directory = Path.GetDirectoryName(_storagePath);
            if (!string.IsNullOrEmpty(directory) && !_fileOperationService.DirectoryExists(directory))
            {
                _fileOperationService.CreateDirectory(directory);
            }

            var result = await _fileOperationService.WriteFileAsync(_storagePath, json, cancellationToken).ConfigureAwait(false);
            if (result.Success)
            {
                _logger?.LogInformation("[CostTracker] 已保存 {Count} 条用量记录", records.Count);
            }
            else
            {
                _logger?.LogError("[CostTracker] 保存用量历史失败: {Error}", result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[CostTracker] 保存用量历史失败");
        }
    }

    /// <summary>
    /// 重置所有用量记录 — 对齐 TS login.tsx resetCostState
    /// </summary>
    public void Reset()
    {
        while (_usageRecords.TryTake(out _)) { }
        _triggeredThresholds.Clear();
        Interlocked.Exchange(ref _totalLinesAdded, 0);
        Interlocked.Exchange(ref _totalLinesRemoved, 0);
        _sessionStartTime = _clock.GetUtcNow();
        _logger?.LogInformation("[CostTracker] 用量记录已重置");
    }

    public async ValueTask DisposeAsync()
    {
        var cts = Interlocked.Exchange(ref _disposeCts, null);
        if (cts is not null)
        {
            cts.Cancel();
            _budgetLock.Dispose();
            cts.Dispose();
        }
        await ValueTask.CompletedTask.ConfigureAwait(false);
    }
}

public sealed partial class ModelCostInfo
{
    public required string Model { get; init; }
    public decimal PromptCostPer1KTokens { get; init; }
    public decimal CompletionCostPer1KTokens { get; init; }
}

internal enum BudgetType
{
    Daily,
    Monthly,
    Total
}
