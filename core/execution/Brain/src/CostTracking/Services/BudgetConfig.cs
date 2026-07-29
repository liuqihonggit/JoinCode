
namespace Core.CostTracking;

/// <summary>
/// 预算配置类
/// </summary>
public sealed class BudgetConfig
{
    /// <summary>
    /// 每日预算限额 (USD)
    /// </summary>
    [JsonPropertyName("dailyLimit")]
    public required decimal DailyLimit { get; init; }

    /// <summary>
    /// 每月预算限额 (USD)
    /// </summary>
    [JsonPropertyName("monthlyLimit")]
    public required decimal MonthlyLimit { get; init; }

    /// <summary>
    /// 总预算限额 (USD)
    /// </summary>
    [JsonPropertyName("totalLimit")]
    public required decimal TotalLimit { get; init; }

    /// <summary>
    /// 告警阈值列表 (0.0 - 1.0)
    /// </summary>
    [JsonPropertyName("alertThresholds")]
    public List<double> AlertThresholds { get; init; } = [0.5, 0.8, 1.0];

    /// <summary>
    /// 是否启用预算管理
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// 验证配置有效性
    /// </summary>
    /// <returns>验证结果，如果有效返回空字符串，否则返回错误信息</returns>
    public string? Validate()
    {
        if (DailyLimit < 0)
        {
            return "每日预算限额不能为负数";
        }

        if (MonthlyLimit < 0)
        {
            return "每月预算限额不能为负数";
        }

        if (TotalLimit < 0)
        {
            return "总预算限额不能为负数";
        }

        if (AlertThresholds == null || AlertThresholds.Count == 0)
        {
            return "告警阈值列表不能为空";
        }

        foreach (var threshold in AlertThresholds)
        {
            if (threshold < 0 || threshold > 1)
            {
                return $"告警阈值必须在 0.0 到 1.0 之间，当前值: {threshold}";
            }
        }

        var sortedThresholds = AlertThresholds.OrderBy(t => t).ToList();
        if (!AlertThresholds.SequenceEqual(sortedThresholds))
        {
            return "告警阈值应该按升序排列";
        }

        return null;
    }

    /// <summary>
    /// 验证配置有效性，无效时抛出异常
    /// </summary>
    /// <exception cref="InvalidOperationException">配置无效时抛出</exception>
    public void ValidateOrThrow()
    {
        var error = Validate();
        if (error != null)
        {
            throw new InvalidOperationException($"预算配置无效: {error}");
        }
    }

    /// <summary>
    /// 获取默认预算配置
    /// </summary>
    public static BudgetConfig Default => new()
    {
        DailyLimit = 10.0m,
        MonthlyLimit = WorkflowConstants.Budget.DefaultMonthlyLimit,
        TotalLimit = WorkflowConstants.Budget.DefaultTotalLimit,
        AlertThresholds = [0.5, 0.8, 1.0],
        Enabled = true
    };
}

/// <summary>
/// 预算配置构建器 - 支持链式配置
/// </summary>
public sealed class BudgetConfigBuilder
{
    private decimal _dailyLimit;
    private decimal _monthlyLimit;
    private decimal _totalLimit;
    private List<double> _alertThresholds = [0.5, 0.8, 1.0];
    private bool _enabled = true;

    private BudgetConfigBuilder()
    {
    }

    /// <summary>
    /// 创建新的构建器
    /// </summary>
    public static BudgetConfigBuilder Create() => new();

    /// <summary>
    /// 从默认配置开始
    /// </summary>
    public static BudgetConfigBuilder CreateFromDefault() => Create()
        .WithDailyLimit(10.0m)
        .WithMonthlyLimit(100.0m)
        .WithTotalLimit(1000.0m);

    /// <summary>
    /// 设置每日预算限额
    /// </summary>
    public BudgetConfigBuilder WithDailyLimit(decimal limit)
    {
        _dailyLimit = limit;
        return this;
    }

    /// <summary>
    /// 设置每月预算限额
    /// </summary>
    public BudgetConfigBuilder WithMonthlyLimit(decimal limit)
    {
        _monthlyLimit = limit;
        return this;
    }

    /// <summary>
    /// 设置总预算限额
    /// </summary>
    public BudgetConfigBuilder WithTotalLimit(decimal limit)
    {
        _totalLimit = limit;
        return this;
    }

    /// <summary>
    /// 同时设置所有预算限额
    /// </summary>
    public BudgetConfigBuilder WithLimits(decimal daily, decimal monthly, decimal total)
    {
        _dailyLimit = daily;
        _monthlyLimit = monthly;
        _totalLimit = total;
        return this;
    }

    /// <summary>
    /// 设置告警阈值列表
    /// </summary>
    public BudgetConfigBuilder WithAlertThresholds(params double[] thresholds)
    {
        _alertThresholds = thresholds.ToList();
        return this;
    }

    /// <summary>
    /// 添加告警阈值
    /// </summary>
    public BudgetConfigBuilder AddAlertThreshold(double threshold)
    {
        _alertThresholds.Add(threshold);
        return this;
    }

    /// <summary>
    /// 启用预算管理
    /// </summary>
    public BudgetConfigBuilder Enable()
    {
        _enabled = true;
        return this;
    }

    /// <summary>
    /// 禁用预算管理
    /// </summary>
    public BudgetConfigBuilder Disable()
    {
        _enabled = false;
        return this;
    }

    /// <summary>
    /// 设置是否启用预算管理
    /// </summary>
    public BudgetConfigBuilder WithEnabled(bool enabled)
    {
        _enabled = enabled;
        return this;
    }

    /// <summary>
    /// 使用宽松预算（开发环境）
    /// </summary>
    public BudgetConfigBuilder UseDevelopmentBudget()
    {
        _dailyLimit = 50.0m;
        _monthlyLimit = 500.0m;
        _totalLimit = 5000.0m;
        _alertThresholds = [0.7, 0.9, 1.0];
        return this;
    }

    /// <summary>
    /// 使用严格预算（生产环境）
    /// </summary>
    public BudgetConfigBuilder UseProductionBudget()
    {
        _dailyLimit = 5.0m;
        _monthlyLimit = 50.0m;
        _totalLimit = 500.0m;
        _alertThresholds = [0.5, 0.75, 0.9, 1.0];
        return this;
    }

    /// <summary>
    /// 构建预算配置
    /// </summary>
    /// <exception cref="InvalidOperationException">配置无效时抛出</exception>
    public BudgetConfig Build()
    {
        var config = new BudgetConfig
        {
            DailyLimit = _dailyLimit,
            MonthlyLimit = _monthlyLimit,
            TotalLimit = _totalLimit,
            AlertThresholds = new List<double>(_alertThresholds),
            Enabled = _enabled
        };

        config.ValidateOrThrow();
        return config;
    }

    /// <summary>
    /// 尝试构建预算配置，返回验证错误信息
    /// </summary>
    public (BudgetConfig? Config, string? Error) TryBuild()
    {
        var config = new BudgetConfig
        {
            DailyLimit = _dailyLimit,
            MonthlyLimit = _monthlyLimit,
            TotalLimit = _totalLimit,
            AlertThresholds = new List<double>(_alertThresholds),
            Enabled = _enabled
        };

        var error = config.Validate();
        return error == null ? (config, null) : (null, error);
    }
}
