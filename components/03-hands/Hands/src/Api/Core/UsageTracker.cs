
using JoinCode.Abstractions.Attributes;

namespace Services.Api;

/// <summary>
/// Token 使用记录
/// </summary>
public sealed record TokenUsageRecord
{
    /// <summary>
    /// 记录时间戳
    /// </summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// 模型名称
    /// </summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>
    /// 输入 Token 数
    /// </summary>
    public int InputTokens { get; init; }

    /// <summary>
    /// 输出 Token 数
    /// </summary>
    public int OutputTokens { get; init; }

    /// <summary>
    /// 总 Token 数
    /// </summary>
    public int TotalTokens => InputTokens + OutputTokens;

    /// <summary>
    /// 缓存创建 Token 数（Anthropic Prompt Caching）
    /// </summary>
    public int CacheCreationTokens { get; init; }

    /// <summary>
    /// 缓存读取 Token 数（Anthropic Prompt Caching）
    /// </summary>
    public int CacheReadTokens { get; init; }

    /// <summary>
    /// 估计成本（美元）
    /// </summary>
    public decimal EstimatedCostUsd { get; init; }

    /// <summary>
    /// API 端点
    /// </summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>
    /// 会话 ID
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// 请求 ID
    /// </summary>
    public string? RequestId { get; init; }
}

/// <summary>
/// Token 使用统计
/// </summary>
public sealed record TokenUsageStatistics
{
    /// <summary>
    /// 总请求数
    /// </summary>
    public int TotalRequests { get; init; }

    /// <summary>
    /// 总输入 Token 数
    /// </summary>
    public long TotalInputTokens { get; init; }

    /// <summary>
    /// 总输出 Token 数
    /// </summary>
    public long TotalOutputTokens { get; init; }

    /// <summary>
    /// 总 Token 数
    /// </summary>
    public long TotalTokens => TotalInputTokens + TotalOutputTokens;

    /// <summary>
    /// 总缓存创建 Token 数
    /// </summary>
    public long TotalCacheCreationTokens { get; init; }

    /// <summary>
    /// 总缓存读取 Token 数
    /// </summary>
    public long TotalCacheReadTokens { get; init; }

    /// <summary>
    /// 总估计成本
    /// </summary>
    public decimal TotalCostUsd { get; init; }

    /// <summary>
    /// 各模型统计
    /// </summary>
    public IReadOnlyDictionary<string, ModelTokenStatistics> ModelStatistics { get; init; } =
        new Dictionary<string, ModelTokenStatistics>();
}

/// <summary>
/// 模型 Token 统计
/// </summary>
public sealed record ModelTokenStatistics
{
    /// <summary>
    /// 模型名称
    /// </summary>
    public required string Model { get; init; }

    /// <summary>
    /// 请求数
    /// </summary>
    public int RequestCount { get; init; }

    /// <summary>
    /// 输入 Token 数
    /// </summary>
    public long InputTokens { get; init; }

    /// <summary>
    /// 输出 Token 数
    /// </summary>
    public long OutputTokens { get; init; }

    /// <summary>
    /// 总 Token 数
    /// </summary>
    public long TotalTokens => InputTokens + OutputTokens;

    /// <summary>
    /// 缓存创建 Token 数
    /// </summary>
    public long CacheCreationTokens { get; init; }

    /// <summary>
    /// 缓存读取 Token 数
    /// </summary>
    public long CacheReadTokens { get; init; }

    /// <summary>
    /// 估计成本
    /// </summary>
    public decimal EstimatedCostUsd { get; init; }
}

/// <summary>
/// Token 使用量追踪器
/// </summary>
public interface IUsageTracker
{
    /// <summary>
    /// 记录 Token 使用
    /// </summary>
    void RecordUsage(TokenUsageRecord usage);

    /// <summary>
    /// 从 API 响应中提取并记录 Token 使用
    /// </summary>
    void RecordFromResponse(string model, string endpoint, string responseContent, string? sessionId = null, string? requestId = null);

    /// <summary>
    /// 获取今日统计
    /// </summary>
    TokenUsageStatistics GetTodayStatistics();

    /// <summary>
    /// 获取会话统计
    /// </summary>
    TokenUsageStatistics GetSessionStatistics(string sessionId);

    /// <summary>
    /// 获取总统计
    /// </summary>
    TokenUsageStatistics GetTotalStatistics();

    /// <summary>
    /// 获取指定时间范围统计
    /// </summary>
    TokenUsageStatistics GetStatistics(DateTime startDate, DateTime endDate);

    /// <summary>
    /// Token 使用记录事件
    /// </summary>
    event EventHandler<TokenUsageRecord>? UsageRecorded;
}

/// <summary>
/// Token 使用量追踪器实现
/// </summary>
[Register]
public sealed partial class UsageTracker : IUsageTracker, IDisposable
{
    private readonly ConcurrentBag<TokenUsageRecord> _usageRecords;
    [Inject] private readonly ILogger<UsageTracker>? _logger;
    private readonly ICostTracker? _costTracker;
    private readonly Dictionary<string, (decimal InputCostPer1K, decimal OutputCostPer1K)> _modelPricing;

    public UsageTracker(ILogger<UsageTracker>? logger = null, ICostTracker? costTracker = null)
    {
        _usageRecords = new ConcurrentBag<TokenUsageRecord>();
        _logger = logger;
        _costTracker = costTracker;
        _modelPricing = InitializeModelPricing();
    }

    /// <inheritdoc />
    public event EventHandler<TokenUsageRecord>? UsageRecorded;

    /// <inheritdoc />
    public void RecordUsage(TokenUsageRecord usage)
    {
        _usageRecords.Add(usage);
        UsageRecorded?.Invoke(this, usage);

        _logger?.LogInformation(
            "[UsageTracker] Token 使用记录 - 模型: {Model}, 输入: {InputTokens}, 输出: {OutputTokens}, 缓存创建: {CacheCreate}, 缓存读取: {CacheRead}, 总: {TotalTokens}, 成本: ${Cost:F6}",
            usage.Model, usage.InputTokens, usage.OutputTokens, usage.CacheCreationTokens, usage.CacheReadTokens, usage.TotalTokens, usage.EstimatedCostUsd);

        _costTracker?.RecordUsage(usage.Model, usage.InputTokens, usage.OutputTokens, usage.CacheCreationTokens, usage.CacheReadTokens, usage.SessionId);
    }

    /// <inheritdoc />
    public void RecordFromResponse(string model, string endpoint, string responseContent, string? sessionId = null, string? requestId = null)
    {
        try
        {
            var (inputTokens, outputTokens, cacheCreationTokens, cacheReadTokens) = ExtractTokenUsage(responseContent);

            if (inputTokens == 0 && outputTokens == 0)
            {
                return;
            }

            var cost = CalculateCost(model, inputTokens, outputTokens, cacheCreationTokens, cacheReadTokens);

            var record = new TokenUsageRecord
            {
                Timestamp = DateTime.UtcNow,
                Model = model,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                CacheCreationTokens = cacheCreationTokens,
                CacheReadTokens = cacheReadTokens,
                EstimatedCostUsd = cost,
                Endpoint = endpoint,
                SessionId = sessionId,
                RequestId = requestId
            };

            RecordUsage(record);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[UsageTracker] 从响应中提取 Token 使用失败");
        }
    }

    /// <inheritdoc />
    public TokenUsageStatistics GetTodayStatistics()
    {
        var today = DateTime.UtcNow.Date;
        var records = _usageRecords.Where(r => r.Timestamp.Date == today).ToList();
        return CalculateStatistics(records);
    }

    /// <inheritdoc />
    public TokenUsageStatistics GetSessionStatistics(string sessionId)
    {
        var records = _usageRecords.Where(r => r.SessionId == sessionId).ToList();
        return CalculateStatistics(records);
    }

    /// <inheritdoc />
    public TokenUsageStatistics GetTotalStatistics()
    {
        return CalculateStatistics(_usageRecords.ToList());
    }

    /// <inheritdoc />
    public TokenUsageStatistics GetStatistics(DateTime startDate, DateTime endDate)
    {
        var records = _usageRecords.Where(r => r.Timestamp >= startDate && r.Timestamp <= endDate).ToList();
        return CalculateStatistics(records);
    }

    /// <summary>
    /// 从响应内容中提取 Token 使用信息
    /// </summary>
    private static (int InputTokens, int OutputTokens, int CacheCreationTokens, int CacheReadTokens) ExtractTokenUsage(string responseContent)
    {
        if (string.IsNullOrWhiteSpace(responseContent))
            return (0, 0, 0, 0);

        try
        {
            var response = JsonSerializer.Deserialize(responseContent, ApiJsonContext.Default.TokenUsageResponse);
            if (response?.Usage is not null)
            {
                var input = response.Usage.PromptTokens ?? response.Usage.InputTokens ?? 0;
                var output = response.Usage.CompletionTokens ?? response.Usage.OutputTokens ?? 0;
                var cacheCreate = response.Usage.CacheCreationInputTokens ?? 0;
                var cacheRead = response.Usage.CacheReadInputTokens ?? 0;
                return (input, output, cacheCreate, cacheRead);
            }
        }
        catch (JsonException ex)
        {
            // JSON 解析失败时返回零值，但需记录日志
            System.Diagnostics.Trace.WriteLine($"UsageTracker.ParseUsage failed: {ex.Message}");
        }

        return (0, 0, 0, 0);
    }

    /// <summary>
    /// 计算成本
    /// </summary>
    private decimal CalculateCost(string model, int inputTokens, int outputTokens, int cacheCreationTokens = 0, int cacheReadTokens = 0)
    {
        var pricing = GetModelPricing(model);
        var inputCost = (inputTokens / 1000m) * pricing.InputCostPer1K;
        var outputCost = (outputTokens / 1000m) * pricing.OutputCostPer1K;
        var cacheCreationCost = (cacheCreationTokens / 1000m) * pricing.InputCostPer1K * 1.25m;
        var cacheReadCost = (cacheReadTokens / 1000m) * pricing.InputCostPer1K * 0.1m;
        return inputCost + outputCost + cacheCreationCost + cacheReadCost;
    }

    /// <summary>
    /// 获取模型定价
    /// </summary>
    private (decimal InputCostPer1K, decimal OutputCostPer1K) GetModelPricing(string model)
    {
        var match = _modelPricing.FirstOrDefault(kvp => model.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase));
        return match.Key != null ? match.Value : (0.01m, 0.03m);
    }

    /// <summary>
    /// 计算统计数据
    /// </summary>
    private static TokenUsageStatistics CalculateStatistics(List<TokenUsageRecord> records)
    {
        if (records.Count == 0)
        {
            return new TokenUsageStatistics();
        }

        var totalInputTokens = records.Sum(r => (long)r.InputTokens);
        var totalOutputTokens = records.Sum(r => (long)r.OutputTokens);
        var totalCacheCreationTokens = records.Sum(r => (long)r.CacheCreationTokens);
        var totalCacheReadTokens = records.Sum(r => (long)r.CacheReadTokens);
        var totalCost = records.Sum(r => r.EstimatedCostUsd);

        var modelStats = records
            .GroupBy(r => r.Model)
            .Select(g => new ModelTokenStatistics
            {
                Model = g.Key,
                RequestCount = g.Count(),
                InputTokens = g.Sum(r => (long)r.InputTokens),
                OutputTokens = g.Sum(r => (long)r.OutputTokens),
                CacheCreationTokens = g.Sum(r => (long)r.CacheCreationTokens),
                CacheReadTokens = g.Sum(r => (long)r.CacheReadTokens),
                EstimatedCostUsd = g.Sum(r => r.EstimatedCostUsd)
            })
            .ToDictionary(m => m.Model, StringComparer.OrdinalIgnoreCase);

        return new TokenUsageStatistics
        {
            TotalRequests = records.Count,
            TotalInputTokens = totalInputTokens,
            TotalOutputTokens = totalOutputTokens,
            TotalCostUsd = totalCost,
            TotalCacheCreationTokens = totalCacheCreationTokens,
            TotalCacheReadTokens = totalCacheReadTokens,
            ModelStatistics = modelStats
        };
    }

    /// <summary>
    /// 初始化模型定价
    /// </summary>
    private static Dictionary<string, (decimal, decimal)> InitializeModelPricing()
    {
        return new Dictionary<string, (decimal, decimal)>(StringComparer.OrdinalIgnoreCase)
        {
            ["gpt-4"] = (0.03m, 0.06m),
            ["gpt-4-turbo"] = (0.01m, 0.03m),
            ["gpt-4-turbo-preview"] = (0.01m, 0.03m),
            ["gpt-4-0125-preview"] = (0.01m, 0.03m),
            ["gpt-4-1106-preview"] = (0.01m, 0.03m),
            ["gpt-4o"] = (0.005m, 0.015m),
            ["gpt-4o-mini"] = (0.00015m, 0.0006m),
            ["gpt-3.5-turbo"] = (0.0005m, 0.0015m),
            ["gpt-3.5-turbo-0125"] = (0.0005m, 0.0015m),
            ["gpt-3.5-turbo-1106"] = (0.001m, 0.002m),
            ["claude-3-opus"] = (0.015m, 0.075m),
            ["claude-3-sonnet"] = (0.003m, 0.015m),
            ["claude-3-haiku"] = (0.00025m, 0.00125m),
            ["azure-gpt-4"] = (0.03m, 0.06m),
            ["azure-gpt-4-turbo"] = (0.01m, 0.03m),
            ["azure-gpt-35-turbo"] = (0.0005m, 0.0015m)
        };
    }

    public void Dispose()
    {
        // ConcurrentBag 不需要显式释放，Dispose 仅为接口契约
        GC.SuppressFinalize(this);
    }
}
