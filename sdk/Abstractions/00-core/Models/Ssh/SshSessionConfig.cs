
namespace JoinCode.Abstractions.Models.Ssh;

public sealed class SshSessionConfig
{
    public required string Host { get; init; }
    public int Port { get; init; } = 22;
    public required string Username { get; init; }
    public SshAuthMethod AuthMethod { get; init; } = SshAuthMethod.PrivateKey;

    public string? PrivateKey { get; init; }

    public string? Passphrase { get; init; }

    public string? Password { get; init; }

    public SshKnownHostsPolicy KnownHostsPolicy { get; init; } = SshKnownHostsPolicy.AcceptNew;

    public int ConnectionTimeoutMs { get; init; } = 30000;

    public int KeepAliveIntervalMs { get; init; } = 30000;

    public int MaxReconnectAttempts { get; init; } = 10;

    public int ReconnectDelayMs { get; init; } = 1000;

    public int MaxReconnectDelayMs { get; init; } = 30000;

    public bool AutoReconnect { get; init; } = true;
}
