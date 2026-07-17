
namespace Core.Bridge;

/// <summary>
/// 轮询配置 - 控制轮询间隔、退避策略和超时
/// </summary>
public sealed partial class PollConfig
{
    /// <summary>默认轮询间隔（毫秒）</summary>
    public const int DefaultIntervalMs = 100;

    /// <summary>默认最大轮询间隔（毫秒）</summary>
    public const int DefaultMaxIntervalMs = 30000;

    /// <summary>默认退避倍数</summary>
    public const double DefaultBackoffMultiplier = 1.5;

    /// <summary>默认抖动百分比</summary>
    public const double DefaultJitterPercent = 0.1;

    /// <summary>默认超时（毫秒）</summary>
    public const int DefaultTimeoutMs = 30000;

    /// <summary>轮询间隔（毫秒）</summary>
    [JsonPropertyName("intervalMs")]
    public int IntervalMs { get; set; } = DefaultIntervalMs;

    /// <summary>最大轮询间隔（毫秒）</summary>
    [JsonPropertyName("maxIntervalMs")]
    public int MaxIntervalMs { get; set; } = DefaultMaxIntervalMs;

    /// <summary>指数退避倍数</summary>
    [JsonPropertyName("backoffMultiplier")]
    public double BackoffMultiplier { get; set; } = DefaultBackoffMultiplier;

    /// <summary>抖动百分比（0.0 ~ 1.0），用于防止惊群效应</summary>
    [JsonPropertyName("jitterPercent")]
    public double JitterPercent { get; set; } = DefaultJitterPercent;

    /// <summary>单次轮询超时（毫秒）</summary>
    [JsonPropertyName("timeoutMs")]
    public int TimeoutMs { get; set; } = DefaultTimeoutMs;
}

/// <summary>
/// 轮询配置管理器 - 管理动态轮询配置，支持指数退避和抖动
/// </summary>
[Register]
public sealed partial class PollConfigManager : IDisposable
{
    [Inject] private readonly ILogger<PollConfigManager>? _logger;
    private readonly AsyncLock _configLock = new();
    private PollConfig _currentConfig;
    private int _consecutiveErrors;

    public PollConfigManager(
        PollConfig? initialConfig = null,
        ILogger<PollConfigManager>? logger = null)
    {
        _logger = logger;
        _currentConfig = initialConfig ?? new PollConfig();
        _consecutiveErrors = 0;
    }

    /// <summary>
    /// 获取当前轮询配置
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>当前配置快照</returns>
    public async Task<PollConfig> GetCurrentConfigAsync(CancellationToken ct = default)
    {
                using (await _configLock.LockAsync(ct).ConfigureAwait(false))
        {
            return new PollConfig
            {
                IntervalMs = _currentConfig.IntervalMs,
                MaxIntervalMs = _currentConfig.MaxIntervalMs,
                BackoffMultiplier = _currentConfig.BackoffMultiplier,
                JitterPercent = _currentConfig.JitterPercent,
                TimeoutMs = _currentConfig.TimeoutMs
            };
        }
    }

    /// <summary>
    /// 更新轮询配置
    /// </summary>
    /// <param name="newConfig">新配置</param>
    /// <param name="ct">取消令牌</param>
    public async Task UpdateConfigAsync(PollConfig newConfig, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(newConfig);

                using (await _configLock.LockAsync(ct).ConfigureAwait(false))
        {
            _currentConfig = new PollConfig
            {
                IntervalMs = newConfig.IntervalMs,
                MaxIntervalMs = newConfig.MaxIntervalMs,
                BackoffMultiplier = newConfig.BackoffMultiplier,
                JitterPercent = newConfig.JitterPercent,
                TimeoutMs = newConfig.TimeoutMs
            };

            _logger?.LogInformation(
                "[PollConfigManager] 轮询配置已更新，间隔: {IntervalMs}ms，最大间隔: {MaxIntervalMs}ms",
                _currentConfig.IntervalMs, _currentConfig.MaxIntervalMs);
        }
    }

    /// <summary>
    /// 计算下一次轮询间隔 - 应用指数退避和抖动
    /// </summary>
    /// <param name="hasError">上次轮询是否出错</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>下一次轮询间隔（毫秒）</returns>
    public async Task<int> CalculateNextIntervalAsync(bool hasError, CancellationToken ct = default)
    {
                using (await _configLock.LockAsync(ct).ConfigureAwait(false))
        {
            if (hasError)
            {
                _consecutiveErrors++;
            }
            else
            {
                _consecutiveErrors = 0;
            }

            var config = _currentConfig;

            // 基础间隔 × 退避倍数的连续错误次方
            var baseInterval = config.IntervalMs * Math.Pow(config.BackoffMultiplier, _consecutiveErrors);

            // 限制不超过最大间隔
            var clampedInterval = Math.Min(baseInterval, config.MaxIntervalMs);

            // 应用抖动：在 [1 - jitter, 1 + jitter] 范围内随机
            var jitterRange = config.JitterPercent;
            var jitterFactor = 1.0 + (Random.Shared.NextDouble() * 2.0 - 1.0) * jitterRange;
            var finalInterval = clampedInterval * jitterFactor;

            var result = (int)Math.Max(config.IntervalMs, Math.Round(finalInterval));

            _logger?.LogDebug(
                "[PollConfigManager] 计算下次轮询间隔: {IntervalMs}ms (连续错误: {Errors}, 退避倍数: {Multiplier})",
                result, _consecutiveErrors, config.BackoffMultiplier);

            return result;
        }
    }

    /// <summary>
    /// 重置为默认配置
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public async Task ResetToDefaultAsync(CancellationToken ct = default)
    {
                using (await _configLock.LockAsync(ct).ConfigureAwait(false))
        {
            _currentConfig = new PollConfig();
            _consecutiveErrors = 0;

            _logger?.LogInformation("[PollConfigManager] 已重置为默认配置");
        }
    }

    public void Dispose() => _configLock.Dispose();
}
