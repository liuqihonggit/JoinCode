
namespace Core.Bridge;

/// <summary>
/// 桥轮询间隔配置 — 对齐 TS 端 pollConfigDefaults.ts PollIntervalConfig
/// 定义单会话和多会话（bridgeMain.ts）两套轮询间隔，以及回收超时和 keepalive 间隔
/// </summary>
public sealed class BridgePollIntervalConfig
{
    /// <summary>非容量时轮询间隔（毫秒）— 默认 2000</summary>
    [JsonPropertyName("poll_interval_ms_not_at_capacity")]
    public int PollIntervalMsNotAtCapacity { get; init; } = 2000;

    /// <summary>容量满时轮询间隔（毫秒）— 默认 600000（10分钟），0 表示禁用</summary>
    [JsonPropertyName("poll_interval_ms_at_capacity")]
    public int PollIntervalMsAtCapacity { get; init; } = 600000;

    /// <summary>非独占心跳间隔（毫秒）— 默认 0（禁用）</summary>
    [JsonPropertyName("non_exclusive_heartbeat_interval_ms")]
    public int NonExclusiveHeartbeatIntervalMs { get; init; } = 0;

    /// <summary>多会话：非容量时轮询间隔（毫秒）— 默认 2000</summary>
    [JsonPropertyName("multisession_poll_interval_ms_not_at_capacity")]
    public int MultisessionPollIntervalMsNotAtCapacity { get; init; } = 2000;

    /// <summary>多会话：部分容量时轮询间隔（毫秒）— 默认 2000</summary>
    [JsonPropertyName("multisession_poll_interval_ms_partial_capacity")]
    public int MultisessionPollIntervalMsPartialCapacity { get; init; } = 2000;

    /// <summary>多会话：容量满时轮询间隔（毫秒）— 默认 600000</summary>
    [JsonPropertyName("multisession_poll_interval_ms_at_capacity")]
    public int MultisessionPollIntervalMsAtCapacity { get; init; } = 600000;

    /// <summary>回收超时（毫秒）— 默认 5000</summary>
    [JsonPropertyName("reclaim_older_than_ms")]
    public int ReclaimOlderThanMs { get; init; } = 5000;

    /// <summary>v2 会话 keepalive 间隔（毫秒）— 默认 120000（2分钟）</summary>
    [JsonPropertyName("session_keepalive_interval_v2_ms")]
    public int SessionKeepaliveIntervalV2Ms { get; init; } = 120000;

    /// <summary>默认配置 — 对齐 TS 端 DEFAULT_POLL_CONFIG，每次返回新实例防止篡改</summary>
    public static BridgePollIntervalConfig Defaults => new();
}

/// <summary>
/// 桥轮询间隔配置管理 — 对齐 TS 端 pollConfig.ts
/// 从配置源读取并验证轮询间隔配置，验证失败整体回退到默认值
/// </summary>
public static class BridgePollConfig
{
    /// <summary>轮询间隔最小值（毫秒）</summary>
    private const int MinPollIntervalMs = 100;

    /// <summary>回收超时最小值（毫秒）— 对齐 TS 端 .min(1)</summary>
    private const int MinReclaimMs = 1;

    /// <summary>5分钟缓存刷新窗口 — 对齐 TS 端 getFeatureValue_CACHED_WITH_REFRESH</summary>
    private static readonly TimeSpan _cacheRefreshWindow = TimeSpan.FromMinutes(5);

    private static volatile BridgePollIntervalConfig? _cachedConfig;
    private static long _lastRefreshTicks;

    /// <summary>
    /// 获取轮询间隔配置 — 对齐 TS 端 getPollIntervalConfig()
    /// 从配置源读取并验证，验证失败整体回退到默认值
    /// </summary>
    public static BridgePollIntervalConfig GetPollIntervalConfig()
    {
        // 检查缓存是否有效
        var cached = _cachedConfig;
        if (cached is not null)
        {
            var elapsed = TimeSpan.FromTicks(Environment.TickCount64 - Volatile.Read(ref _lastRefreshTicks));
            if (elapsed < _cacheRefreshWindow)
            {
                return cached;
            }
        }

        // 从环境变量读取（替代 TS 端的 GrowthBook）
        var config = ReadFromEnvironment();
        var validated = ValidateConfig(config);

        _cachedConfig = validated;
        Volatile.Write(ref _lastRefreshTicks, Environment.TickCount64);

        return validated;
    }

    /// <summary>
    /// 验证配置 — 对齐 TS 端 Zod schema 验证
    /// 任何一个字段不合法，整个配置回退到默认值（整体拒绝策略，对齐 TS 端）
    /// </summary>
    public static BridgePollIntervalConfig ValidateConfig(BridgePollIntervalConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        // 单字段验证 — 对齐 TS 端 Zod .min() 约束
        if (config.PollIntervalMsNotAtCapacity < MinPollIntervalMs) return BridgePollIntervalConfig.Defaults;
        if (!IsValidAtCapacityValue(config.PollIntervalMsAtCapacity)) return BridgePollIntervalConfig.Defaults;
        if (config.NonExclusiveHeartbeatIntervalMs < 0) return BridgePollIntervalConfig.Defaults;
        if (config.MultisessionPollIntervalMsNotAtCapacity < MinPollIntervalMs) return BridgePollIntervalConfig.Defaults;
        if (config.MultisessionPollIntervalMsPartialCapacity < MinPollIntervalMs) return BridgePollIntervalConfig.Defaults;
        if (!IsValidAtCapacityValue(config.MultisessionPollIntervalMsAtCapacity)) return BridgePollIntervalConfig.Defaults;
        if (config.ReclaimOlderThanMs < MinReclaimMs) return BridgePollIntervalConfig.Defaults;
        if (config.SessionKeepaliveIntervalV2Ms < 0) return BridgePollIntervalConfig.Defaults;

        // 对象级 refine: 单会话 at-capacity liveness — 对齐 TS 端 .refine()
        // 非独占心跳或 at_capacity 轮询必须至少有一个启用
        if (config.NonExclusiveHeartbeatIntervalMs <= 0 && config.PollIntervalMsAtCapacity <= 0)
        {
            return BridgePollIntervalConfig.Defaults;
        }

        // 对象级 refine: 多会话 at-capacity liveness — 对齐 TS 端 .refine()
        if (config.NonExclusiveHeartbeatIntervalMs <= 0 && config.MultisessionPollIntervalMsAtCapacity <= 0)
        {
            return BridgePollIntervalConfig.Defaults;
        }

        return config;
    }

    /// <summary>at_capacity 值合法: 0（禁用）或 >= MinPollIntervalMs</summary>
    private static bool IsValidAtCapacityValue(int value) => value == 0 || value >= MinPollIntervalMs;

    /// <summary>重置缓存（用于测试）</summary>
    public static void ResetCache()
    {
        _cachedConfig = null;
        Volatile.Write(ref _lastRefreshTicks, 0);
    }

    /// <summary>从环境变量读取配置</summary>
    private static BridgePollIntervalConfig ReadFromEnvironment()
    {
        return new BridgePollIntervalConfig
        {
            PollIntervalMsNotAtCapacity = TryGetEnvInt("JCC_BRIDGE_POLL_INTERVAL_NOT_AT_CAPACITY", out var p1) ? p1 : 2000,
            PollIntervalMsAtCapacity = TryGetEnvInt("JCC_BRIDGE_POLL_INTERVAL_AT_CAPACITY", out var p2) ? p2 : 600000,
            NonExclusiveHeartbeatIntervalMs = TryGetEnvInt("JCC_BRIDGE_HEARTBEAT_INTERVAL", out var p3) ? p3 : 0,
            MultisessionPollIntervalMsNotAtCapacity = TryGetEnvInt("JCC_BRIDGE_MS_POLL_NOT_AT_CAPACITY", out var p4) ? p4 : 2000,
            MultisessionPollIntervalMsPartialCapacity = TryGetEnvInt("JCC_BRIDGE_MS_POLL_PARTIAL_CAPACITY", out var p5) ? p5 : 2000,
            MultisessionPollIntervalMsAtCapacity = TryGetEnvInt("JCC_BRIDGE_MS_POLL_AT_CAPACITY", out var p6) ? p6 : 600000,
            ReclaimOlderThanMs = TryGetEnvInt("JCC_BRIDGE_RECLAIM_OLDER_THAN_MS", out var p7) ? p7 : 5000,
            SessionKeepaliveIntervalV2Ms = TryGetEnvInt("JCC_BRIDGE_SESSION_KEEPALIVE_V2_MS", out var p8) ? p8 : 120000,
        };
    }

    private static bool TryGetEnvInt(string name, out int value)
    {
        value = 0;
        var env = Environment.GetEnvironmentVariable(name);
        return env is not null && int.TryParse(env, out value);
    }
}
