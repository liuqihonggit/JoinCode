namespace JoinCode.Transport;

public sealed class TransportFallbackConfig
{
    public int ConnectTimeoutMs { get; init; } = 5000;

    public int HealthCheckTimeoutMs { get; init; } = 2000;

    public int ChainTimeoutMs { get; init; } = 30000;

    public bool Enabled { get; init; } = true;

    public bool HealthCheckEnabled { get; init; } = true;

    public bool CircuitBreakerEnabled { get; init; } = true;

    public int CircuitBreakerFailureThreshold { get; init; } = 3;

    public int CircuitBreakerCoolDownMs { get; init; } = 30000;

    public static TransportFallbackConfig FromEnvironment()
    {
        var envDisable = Environment.GetEnvironmentVariable("JCC_TRANSPORT_FALLBACK");
        var envCircuitDisable = Environment.GetEnvironmentVariable("JCC_TRANSPORT_CIRCUIT_BREAKER");
        var envTimeout = Environment.GetEnvironmentVariable("JCC_TRANSPORT_CONNECT_TIMEOUT_MS");
        var envChainTimeout = Environment.GetEnvironmentVariable("JCC_TRANSPORT_CHAIN_TIMEOUT_MS");
        var envHealthCheck = Environment.GetEnvironmentVariable("JCC_TRANSPORT_HEALTH_CHECK");
        var envCbThreshold = Environment.GetEnvironmentVariable("JCC_TRANSPORT_CB_THRESHOLD");
        var envCbCooldown = Environment.GetEnvironmentVariable("JCC_TRANSPORT_CB_COOLDOWN_MS");

        return new TransportFallbackConfig
        {
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
