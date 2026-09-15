
namespace JoinCode.Abstractions.Models.Ssh;

/// <summary>
/// SSH 连接状态 — [EnumValue] 由 EnumMetadataGenerator 自动生成映射
/// </summary>
public enum SshConnectionState
{
    [EnumValue("disconnected")] Disconnected,
    [EnumValue("connecting")] Connecting,
    [EnumValue("connected")] Connected,
    [EnumValue("reconnecting")] Reconnecting,
    [EnumValue("error")] Error
}

/// <summary>
/// SSH 认证方式 — [EnumValue] 由 EnumMetadataGenerator 自动生成映射
/// </summary>
public enum SshAuthMethod
{
    [EnumValue("password")] Password,
    [EnumValue("private_key")] PrivateKey,
    [EnumValue("ssh_agent")] SshAgent,
    [EnumValue("certificate")] Certificate
}

/// <summary>
/// SSH 端口转发类型 — [EnumValue] 由 EnumMetadataGenerator 自动生成映射
/// </summary>
public enum SshForwardType
{
    [EnumValue("local")] Local,
    [EnumValue("remote")] Remote,
    [EnumValue("dynamic")] Dynamic
}

/// <summary>
/// SSH known_hosts 策略 — [EnumValue] 由 EnumMetadataGenerator 自动生成映射
/// </summary>
public enum SshKnownHostsPolicy
{
    [EnumValue("strict")] Strict,
    [EnumValue("accept_new")] AcceptNew,
    [EnumValue("ignore")] Ignore
}
