
namespace JoinCode.Abstractions.Models.Ssh;

public sealed class SshSessionConfig {
    /// <summary>获取主机地址。</summary>
    public required string Host { get; init; }
    /// <summary>获取端口。</summary>
    public int Port { get; init; } = 22;
    /// <summary>获取用户名。</summary>
    public required string Username { get; init; }
    /// <summary>获取认证方式。</summary>
    public SshAuthMethod AuthMethod { get; init; } = SshAuthMethod.PrivateKey;

    /// <summary>获取私钥内容。</summary>
    public string? PrivateKey { get; init; }

    /// <summary>获取私钥口令。</summary>
    public string? Passphrase { get; init; }

    /// <summary>获取密码。</summary>
    public string? Password { get; init; }

    /// <summary>获取已知主机策略。</summary>
    public SshKnownHostsPolicy KnownHostsPolicy { get; init; } = SshKnownHostsPolicy.AcceptNew;

    /// <summary>获取连接超时时间(毫秒)。</summary>
    public int ConnectionTimeoutMs { get; init; } = 30000;

    /// <summary>获取保活间隔(毫秒)。</summary>
    public int KeepAliveIntervalMs { get; init; } = 30000;

    /// <summary>获取最大重连次数。</summary>
    public int MaxReconnectAttempts { get; init; } = 10;

    /// <summary>获取重连延迟(毫秒)。</summary>
    public int ReconnectDelayMs { get; init; } = 1000;

    /// <summary>获取最大重连延迟(毫秒)。</summary>
    public int MaxReconnectDelayMs { get; init; } = 30000;

    /// <summary>获取是否自动重连。</summary>
    public bool AutoReconnect { get; init; } = true;
}