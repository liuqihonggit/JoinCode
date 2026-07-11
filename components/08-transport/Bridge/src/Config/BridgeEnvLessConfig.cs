
namespace Core.Bridge;

/// <summary>
/// Env-less 桥配置 — 对齐 TS 端 envLessBridgeConfig.ts EnvLessBridgeConfig
/// 定义 v2 路径（无环境层）的所有计时参数
/// </summary>
public sealed class BridgeEnvLessConfig
{
    /// <summary>初始化重试最大次数 — 默认 3</summary>
    [JsonPropertyName("init_retry_max_attempts")]
    public int InitRetryMaxAttempts { get; init; } = 3;

    /// <summary>初始化重试基础延迟（毫秒）— 默认 500</summary>
    [JsonPropertyName("init_retry_base_delay_ms")]
    public int InitRetryBaseDelayMs { get; init; } = 500;

    /// <summary>初始化重试抖动比例 — 默认 0.25</summary>
    [JsonPropertyName("init_retry_jitter_fraction")]
    public double InitRetryJitterFraction { get; init; } = 0.25;

    /// <summary>初始化重试最大延迟（毫秒）— 默认 4000</summary>
    [JsonPropertyName("init_retry_max_delay_ms")]
    public int InitRetryMaxDelayMs { get; init; } = 4000;

    /// <summary>HTTP 超时（毫秒）— 默认 10000</summary>
    [JsonPropertyName("http_timeout_ms")]
    public int HttpTimeoutMs { get; init; } = 10000;

    /// <summary>UUID 去重缓冲区大小 — 默认 2000</summary>
    [JsonPropertyName("uuid_dedup_buffer_size")]
    public int UuidDedupBufferSize { get; init; } = 2000;

    /// <summary>心跳间隔（毫秒）— 默认 20000</summary>
    [JsonPropertyName("heartbeat_interval_ms")]
    public int HeartbeatIntervalMs { get; init; } = 20000;

    /// <summary>心跳抖动比例 — 默认 0.1</summary>
    [JsonPropertyName("heartbeat_jitter_fraction")]
    public double HeartbeatJitterFraction { get; init; } = 0.1;

    /// <summary>Token 刷新缓冲（毫秒）— 默认 300000（5分钟）</summary>
    [JsonPropertyName("token_refresh_buffer_ms")]
    public int TokenRefreshBufferMs { get; init; } = 300000;

    /// <summary>拆卸归档超时（毫秒）— 默认 1500</summary>
    [JsonPropertyName("teardown_archive_timeout_ms")]
    public int TeardownArchiveTimeoutMs { get; init; } = 1500;

    /// <summary>连接超时（毫秒）— 默认 15000</summary>
    [JsonPropertyName("connect_timeout_ms")]
    public int ConnectTimeoutMs { get; init; } = 15000;

    /// <summary>最低版本要求 — 默认 "0.0.0"</summary>
    [JsonPropertyName("min_version")]
    public string MinVersion { get; init; } = "0.0.0";

    /// <summary>是否提示用户升级 claude.ai 应用 — 默认 false</summary>
    [JsonPropertyName("should_show_app_upgrade_message")]
    public bool ShouldShowAppUpgradeMessage { get; init; } = false;

    /// <summary>默认配置 — 每次返回新实例防止篡改</summary>
    public static BridgeEnvLessConfig Defaults => new();

    /// <summary>
    /// 验证配置 — 对齐 TS 端 Zod schema 验证
    /// 任何一个字段不合法，整个配置回退到默认值（整体拒绝策略）
    /// </summary>
    public static BridgeEnvLessConfig Validate(BridgeEnvLessConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.InitRetryMaxAttempts < 1) return Defaults;
        if (config.InitRetryBaseDelayMs < 0) return Defaults;
        if (config.InitRetryJitterFraction < 0 || config.InitRetryJitterFraction > 1) return Defaults;
        if (config.InitRetryMaxDelayMs < 0) return Defaults;
        if (config.HttpTimeoutMs < 1000) return Defaults;
        if (config.UuidDedupBufferSize < 0) return Defaults;
        if (config.HeartbeatIntervalMs < 0) return Defaults;
        if (config.HeartbeatJitterFraction < 0 || config.HeartbeatJitterFraction > 1) return Defaults;
        if (config.TokenRefreshBufferMs < 0) return Defaults;
        if (config.TeardownArchiveTimeoutMs < 0) return Defaults;
        if (config.ConnectTimeoutMs < 0) return Defaults;
        if (string.IsNullOrEmpty(config.MinVersion)) return Defaults;

        return config;
    }

    /// <summary>
    /// 获取配置 — 对齐 TS 端 getEnvLessBridgeConfig()
    /// 从环境变量读取并验证，验证失败回退到默认值
    /// </summary>
    public static BridgeEnvLessConfig GetConfig()
    {
        var config = ReadFromEnvironment();
        return Validate(config);
    }

    /// <summary>
    /// 检查最低版本 — 对齐 TS 端 checkEnvLessBridgeMinVersion()
    /// 返回 null 表示通过，否则返回错误消息
    /// </summary>
    public static string? CheckMinVersion(string currentVersion)
    {
        var cfg = GetConfig();
        if (cfg.MinVersion == "0.0.0") return null;

        // 简单版本比较：按点分隔逐段比较
        if (IsVersionLessThan(currentVersion, cfg.MinVersion))
        {
            return $"Bridge requires version >= {cfg.MinVersion}, current: {currentVersion}";
        }

        return null;
    }

    /// <summary>是否提示用户升级 — 对齐 TS 端 shouldShowAppUpgradeMessage()</summary>
    public static bool QueryAppUpgradeMessage()
    {
        return GetConfig().ShouldShowAppUpgradeMessage;
    }

    /// <summary>从环境变量读取配置</summary>
    private static BridgeEnvLessConfig ReadFromEnvironment()
    {
        return new BridgeEnvLessConfig
        {
            InitRetryMaxAttempts = TryGetEnvInt("JCC_BRIDGE_INIT_RETRY_MAX", out var p1) ? p1 : 3,
            InitRetryBaseDelayMs = TryGetEnvInt("JCC_BRIDGE_INIT_RETRY_BASE_MS", out var p2) ? p2 : 500,
            InitRetryMaxDelayMs = TryGetEnvInt("JCC_BRIDGE_INIT_RETRY_MAX_MS", out var p3) ? p3 : 4000,
            HttpTimeoutMs = TryGetEnvInt("JCC_BRIDGE_HTTP_TIMEOUT_MS", out var p4) ? p4 : 10000,
            HeartbeatIntervalMs = TryGetEnvInt("JCC_BRIDGE_HEARTBEAT_MS", out var p5) ? p5 : 20000,
            TokenRefreshBufferMs = TryGetEnvInt("JCC_BRIDGE_TOKEN_REFRESH_MS", out var p6) ? p6 : 300000,
            TeardownArchiveTimeoutMs = TryGetEnvInt("JCC_BRIDGE_TEARDOWN_MS", out var p7) ? p7 : 1500,
            ConnectTimeoutMs = TryGetEnvInt("JCC_BRIDGE_CONNECT_TIMEOUT_MS", out var p8) ? p8 : 15000,
            MinVersion = Environment.GetEnvironmentVariable("JCC_BRIDGE_MIN_VERSION") ?? "0.0.0",
        };
    }

    private static bool TryGetEnvInt(string name, out int value)
    {
        value = 0;
        var env = Environment.GetEnvironmentVariable(name);
        return env is not null && int.TryParse(env, out value);
    }

    /// <summary>简单版本比较: a &lt; b</summary>
    private static bool IsVersionLessThan(string a, string b)
    {
        var aParts = a.Split('.');
        var bParts = b.Split('.');

        for (var i = 0; i < Math.Max(aParts.Length, bParts.Length); i++)
        {
            var aVal = i < aParts.Length && int.TryParse(aParts[i], out var av) ? av : 0;
            var bVal = i < bParts.Length && int.TryParse(bParts[i], out var bv) ? bv : 0;

            if (aVal < bVal) return true;
            if (aVal > bVal) return false;
        }

        return false;
    }
}
