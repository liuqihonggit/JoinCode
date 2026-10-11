namespace JoinCode.Transport;

/// <summary>
/// 传输回退配置 — 控制传输层故障转移、健康检查和熔断器的参数
/// </summary>
public sealed class TransportFallbackConfig {
    /// <summary>连接超时时间（毫秒），默认 5000</summary>
    public int ConnectTimeoutMs { get; init; } = 5000;

    /// <summary>健康检查超时时间（毫秒），默认 2000</summary>
    public int HealthCheckTimeoutMs { get; init; } = 2000;

    /// <summary>链式回退总超时时间（毫秒），默认 30000</summary>
    public int ChainTimeoutMs { get; init; } = 30000;

    /// <summary>是否启用传输回退，默认 true</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>是否启用健康检查，默认 true</summary>
    public bool HealthCheckEnabled { get; init; } = true;

    /// <summary>是否启用熔断器，默认 true</summary>
    public bool CircuitBreakerEnabled { get; init; } = true;

    /// <summary>熔断器失败阈值（连续失败次数），默认 3</summary>
    public int CircuitBreakerFailureThreshold { get; init; } = 3;

    /// <summary>熔断器冷却时间（毫秒），默认 30000</summary>
    public int CircuitBreakerCoolDownMs { get; init; } = 30000;

    /// <summary>
    /// 从环境变量创建配置实例
    /// 读取 JCC_TRANSPORT_FALLBACK、JCC_TRANSPORT_CIRCUIT_BREAKER、JCC_TRANSPORT_CONNECT_TIMEOUT_MS 等环境变量
    /// </summary>
    /// <returns>从环境变量填充的配置实例</returns>
    public static TransportFallbackConfig FromEnvironment() {
        var envDisable = Environment.GetEnvironmentVariable("JCC_TRANSPORT_FALLBACK");
        var envCircuitDisable = Environment.GetEnvironmentVariable("JCC_TRANSPORT_CIRCUIT_BREAKER");
        var envTimeout = Environment.GetEnvironmentVariable("JCC_TRANSPORT_CONNECT_TIMEOUT_MS");
        var envChainTimeout = Environment.GetEnvironmentVariable("JCC_TRANSPORT_CHAIN_TIMEOUT_MS");
        var envHealthCheck = Environment.GetEnvironmentVariable("JCC_TRANSPORT_HEALTH_CHECK");
        var envCbThreshold = Environment.GetEnvironmentVariable("JCC_TRANSPORT_CB_THRESHOLD");
        var envCbCooldown = Environment.GetEnvironmentVariable("JCC_TRANSPORT_CB_COOLDOWN_MS");

        return new TransportFallbackConfig {
            Enabled = envDisable != "0",
            CircuitBreakerEnabled = envCircuitDisable != "0",
            HealthCheckEnabled = envHealthCheck != "0",
            ConnectTimeoutMs = ParseInt(envTimeout, 5000),
            ChainTimeoutMs = ParseInt(envChainTimeout, 30000),
            CircuitBreakerFailureThreshold = ParseInt(envCbThreshold, 3),
            CircuitBreakerCoolDownMs = ParseInt(envCbCooldown, 30000),
        };
    }

    private static int ParseInt(string? value, int defaultValue) =>
        value is not null && int.TryParse(value, out var result) && result > 0
            ? result
            : defaultValue;
}