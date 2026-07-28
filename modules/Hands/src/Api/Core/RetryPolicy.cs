
using JoinCode.Abstractions.Attributes;

namespace Services.Api;

/// <summary>
/// 重试策略配置
/// </summary>
[Register]
public sealed record RetryPolicyOptions
{
    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetryCount { get; init; } = 3;

    /// <summary>
    /// 初始退避延迟
    /// </summary>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 最大退避延迟
    /// </summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 退避乘数（指数退避）
    /// </summary>
    public double BackoffMultiplier { get; init; } = 2.0;

    /// <summary>
    /// 是否启用抖动
    /// </summary>
    public bool EnableJitter { get; init; } = true;

    /// <summary>
    /// 抖动因子 (0-1)
    /// </summary>
    public double JitterFactor { get; init; } = 0.1;

    /// <summary>
    /// 可重试的 HTTP 状态码
    /// </summary>
    public IReadOnlySet<int> RetryableStatusCodes { get; init; } = new HashSet<int>
    {
        408, // Request Timeout
        429, // Too Many Requests
        500, // Internal Server Error
        502, // Bad Gateway
        503, // Service Unavailable
        504  // Gateway Timeout
    };

    /// <summary>
    /// 可重试的异常类型
    /// </summary>
    public IReadOnlySet<Type> RetryableExceptions { get; init; } = new HashSet<Type>
    {
        typeof(HttpRequestException),
        typeof(TaskCanceledException),
        typeof(TimeoutException),
        typeof(IOException)
    };

    /// <summary>
    /// 创建默认配置
    /// </summary>
    public static RetryPolicyOptions Default => new();

    /// <summary>
    /// 创建激进重试配置（更多重试次数）
    /// </summary>
    public static RetryPolicyOptions Aggressive => new()
    {
        MaxRetryCount = 5,
        InitialDelay = TimeSpan.FromMilliseconds(500),
        MaxDelay = TimeSpan.FromSeconds(60),
        BackoffMultiplier = 2.0,
        EnableJitter = true,
        JitterFactor = 0.2
    };

    /// <summary>
    /// 创建保守重试配置（较少重试次数）
    /// </summary>
    public static RetryPolicyOptions Conservative => new()
    {
        MaxRetryCount = 2,
        InitialDelay = TimeSpan.FromSeconds(2),
        MaxDelay = TimeSpan.FromSeconds(10),
        BackoffMultiplier = 2.0,
        EnableJitter = true,
        JitterFactor = 0.05
    };

    /// <summary>
    /// DI 构造函数 — 从 ApiSettings 映射
    /// </summary>
    public RetryPolicyOptions(IOptions<ApiSettings>? settings = null)
    {
        if (settings?.Value is { } s)
        {
            MaxRetryCount = s.MaxRetryCount;
            InitialDelay = TimeSpan.FromMilliseconds(s.InitialDelayMs);
            MaxDelay = TimeSpan.FromMilliseconds(s.MaxDelayMs);
            BackoffMultiplier = s.BackoffMultiplier;
            EnableJitter = s.EnableJitter;
            JitterFactor = s.JitterFactor;
        }
    }
}

/// <summary>
/// 重试策略执行器
/// </summary>
[Register]
public sealed partial class RetryPolicy
{
    private readonly RetryPolicyOptions _options;
    private readonly Random _random;

    public RetryPolicy(RetryPolicyOptions? options = null)
    {
        _options = options ?? RetryPolicyOptions.Default;
        _random = new Random();
    }

    /// <summary>
    /// 执行带重试的异步操作
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<Exception, bool>? isRetryable = null,
        Action<int, TimeSpan, Exception>? onRetry = null,
        CancellationToken cancellationToken = default)
    {
        var attempt = 0;
        Exception? lastException = null;

        while (attempt <= _options.MaxRetryCount)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ShouldRetry(ex, isRetryable))
            {
                lastException = ex;
                attempt++;

                if (attempt > _options.MaxRetryCount)
                {
                    break;
                }

                var delay = CalculateDelay(attempt);
                onRetry?.Invoke(attempt, delay, ex);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new RetryExhaustedException(
            $"操作在 {_options.MaxRetryCount} 次重试后仍然失败",
            lastException ?? throw new InvalidOperationException("No exception after retries."));
    }

    /// <summary>
    /// 执行带重试的异步操作（无返回值）
    /// </summary>
    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        Func<Exception, bool>? isRetryable = null,
        Action<int, TimeSpan, Exception>? onRetry = null,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(
            async ct =>
            {
                await operation(ct).ConfigureAwait(false);
                return true;
            },
            isRetryable,
            onRetry,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 计算退避延迟
    /// </summary>
    private TimeSpan CalculateDelay(int attempt)
    {
        var exponentialDelay = _options.InitialDelay *
            Math.Pow(_options.BackoffMultiplier, attempt - 1);

        var delay = TimeSpan.FromMilliseconds(
            Math.Min(exponentialDelay.TotalMilliseconds, _options.MaxDelay.TotalMilliseconds));

        if (_options.EnableJitter)
        {
            var jitter = delay.TotalMilliseconds * _options.JitterFactor * (_random.NextDouble() * 2 - 1);
            delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds + jitter);
        }

        return delay;
    }

    /// <summary>
    /// 判断异常是否可重试
    /// </summary>
    private bool ShouldRetry(Exception ex, Func<Exception, bool>? customPredicate)
    {
        if (customPredicate?.Invoke(ex) == true)
        {
            return true;
        }

        if (IsRetryableException(ex))
        {
            return true;
        }

        if (ex is ApiException apiEx && apiEx.IsRetryable)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 判断异常是否属于可重试类型（AOT安全：使用 is 模式匹配替代 IsAssignableFrom）
    /// </summary>
    private bool IsRetryableException(Exception ex)
    {
        // 默认可重试异常类型：HttpRequestException, TaskCanceledException, TimeoutException, IOException
        // 使用 is 模式匹配，AOT 编译器可完全静态分析
        if (ex is HttpRequestException or TaskCanceledException or TimeoutException or IOException)
            return true;

        // 检查用户自定义的可重试异常类型
        foreach (var retryableType in _options.RetryableExceptions)
        {
            if (retryableType == typeof(HttpRequestException) && ex is HttpRequestException) return true;
            if (retryableType == typeof(TaskCanceledException) && ex is TaskCanceledException) return true;
            if (retryableType == typeof(TimeoutException) && ex is TimeoutException) return true;
            if (retryableType == typeof(IOException) && ex is IOException) return true;
        }

        return false;
    }
}

/// <summary>
/// 重试耗尽异常
/// </summary>
public sealed class RetryExhaustedException : WorkflowException
{
    public RetryExhaustedException(string message, Exception innerException)
        : base(message, innerException, errorCode: global::JoinCode.Abstractions.Exceptions.ErrorCode.ApiRetryExhausted.ToValue(), category: ErrorCategory.Api)
    {
    }
}
